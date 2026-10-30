using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Prisma.Api.Infrastructure;
using Prisma.Api.Tests.Infrastructure;
using Prisma.Domain.FixedIncome;
using Shouldly;

namespace Prisma.Api.Tests.Features;

// "Investido" por conta (docs/renda-fixa.md, D20, R.32): `byAccount` em `GET /fixed-income/balances` traz o rendimento bruto do que
// sobra dos aportes que entraram numa conta de investimento. O oráculo é o próprio rendimento do aporte, na mesma resposta: a soma por
// conta nunca pode diferir da soma dos aportes que a alimentam.
[Collection(ApiCollection.Name)]
public sealed class FixedIncomeInvestedByAccountApiTests(PostgresFixture postgres)
{
    private sealed record IdDto(Guid Id);

    private sealed record DepositDto(Guid DepositId, long? GrossYieldCents);

    private sealed record LooseDto(List<DepositDto> Deposits);

    private sealed record EntryDto(Guid Id, Guid? TransferPairId);

    private sealed record AccountYieldDto(Guid AccountId, int DepositCount, long GrossYieldCents, long PrincipalCents);

    private sealed record BalancesDto(LooseDto Unassigned, List<AccountYieldDto> ByAccount);

    private static readonly DateTime Monday = new(2026, 10, 19, 15, 0, 0, DateTimeKind.Utc);

    private sealed class Scenario(PrismaApiFactory factory, HttpClient client) : IAsyncDisposable
    {
        public PrismaApiFactory Factory { get; } = factory;
        public HttpClient Client { get; } = client;
        public Guid Checking { get; set; }
        public Guid InvestmentA { get; set; }
        public Guid InvestmentB { get; set; }

        public async ValueTask DisposeAsync()
        {
            Client.Dispose();
            await Factory.DisposeAsync();
        }
    }

    private async Task<Scenario> Start()
    {
        var factory = new PrismaApiFactory(
            postgres.ConnectionString, new Dictionary<string, string> { ["Rates:Sync:Enabled"] = "false" }, new FakeClock(Monday));
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await db.ReferenceRates.ExecuteDeleteAsync();
            db.ReferenceRates.AddRange(RealCdi2026.Rates.Select(r => ReferenceRate.From(ReferenceRateSeries.Cdi, r, "bcb-sgs-12", DateTime.UtcNow)));
            await db.SaveChangesAsync();
        }

        var s = new Scenario(factory, await factory.CreateAuthenticatedClientAsync());
        s.Checking = await Account(s.Client, new { name = "Itaú", type = "Checking", initialBalanceCents = 5_000_000 });
        s.InvestmentA = await Account(s.Client, new { name = "Íon", type = "Investment", initialBalanceCents = 0 });
        s.InvestmentB = await Account(s.Client, new { name = "Inter Invest", type = "Investment", initialBalanceCents = 0 });
        return s;
    }

    private static async Task<Guid> Account(HttpClient client, object body)
    {
        var response = await client.PostAsJsonAsync("/accounts", body);
        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        return (await response.Content.ReadFromJsonAsync<IdDto>())!.Id;
    }

    private static async Task<Guid> Deposit(HttpClient client, long cents, DateOnly date, Guid? from, Guid? to)
    {
        var response = await client.PostAsJsonAsync("/fixed-income-deposits", new
        {
            goalId = (Guid?)null, clientRequestId = Guid.NewGuid(), depositDate = date, amountCents = cents, productKind = "Cdb",
            indexKind = "Cdi", rateE4 = 1_000_000, maturityDate = (DateOnly?)null, liquidity = "Unknown", note = (string?)null,
            funding = from is null ? null : new { fromAccountId = from, toAccountId = to, method = "Pix" },
        });
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<IdDto>())!.Id;
    }

    private static async Task<BalancesDto> Balances(HttpClient client) =>
        (await client.GetFromJsonAsync<BalancesDto>("/fixed-income/balances"))!;

    // O rendimento de um aporte vem na lista dos "soltos" (sem objetivo), na mesma resposta.
    private static long Yield(BalancesDto b, Guid deposit) => b.Unassigned.Deposits.Single(d => d.DepositId == deposit).GrossYieldCents!.Value;

    [Fact]
    public async Task A_funded_deposit_reports_its_yield_on_the_investment_account()
    {
        await using var s = await Start();
        var deposit = await Deposit(s.Client, 1_000_000, new DateOnly(2026, 7, 21), s.Checking, s.InvestmentA);

        var b = await Balances(s.Client);

        var entry = b.ByAccount.ShouldHaveSingleItem();
        entry.AccountId.ShouldBe(s.InvestmentA);
        entry.DepositCount.ShouldBe(1);
        entry.GrossYieldCents.ShouldBe(Yield(b, deposit));
        entry.GrossYieldCents.ShouldBeGreaterThan(0);
        // O principal que sobra do aporte (I.9): é o que o saldo da conta guarda e o "Investido" já conta pela carteira.
        entry.PrincipalCents.ShouldBe(1_000_000);
    }

    [Fact]
    public async Task A_deposit_that_is_only_tracked_has_no_account_and_is_left_out()
    {
        await using var s = await Start();
        await Deposit(s.Client, 1_000_000, new DateOnly(2026, 7, 21), null, null);

        (await Balances(s.Client)).ByAccount.ShouldBeEmpty();
    }

    [Fact]
    public async Task Deposits_are_summed_per_account_and_never_across_accounts()
    {
        await using var s = await Start();
        var a1 = await Deposit(s.Client, 1_000_000, new DateOnly(2026, 7, 21), s.Checking, s.InvestmentA);
        var a2 = await Deposit(s.Client, 500_000, new DateOnly(2026, 8, 10), s.Checking, s.InvestmentA);
        var b1 = await Deposit(s.Client, 700_000, new DateOnly(2026, 9, 1), s.Checking, s.InvestmentB);
        await Deposit(s.Client, 300_000, new DateOnly(2026, 9, 2), null, null);

        var b = await Balances(s.Client);

        b.ByAccount.Count.ShouldBe(2);
        var a = b.ByAccount.Single(x => x.AccountId == s.InvestmentA);
        a.DepositCount.ShouldBe(2);
        a.GrossYieldCents.ShouldBe(Yield(b, a1) + Yield(b, a2));
        var other = b.ByAccount.Single(x => x.AccountId == s.InvestmentB);
        other.DepositCount.ShouldBe(1);
        other.GrossYieldCents.ShouldBe(Yield(b, b1));
        (a.PrincipalCents, other.PrincipalCents).ShouldBe((1_500_000L, 700_000L));
    }

    [Fact]
    public async Task A_partial_redemption_leaves_only_the_yield_of_what_remains()
    {
        await using var s = await Start();
        var deposit = await Deposit(s.Client, 1_000_000, new DateOnly(2026, 7, 21), s.Checking, s.InvestmentA);
        var before = (await Balances(s.Client)).ByAccount.Single().GrossYieldCents;

        var redeem = await s.Client.PostAsJsonAsync($"/fixed-income-deposits/{deposit}/redemptions", new
        {
            clientRequestId = Guid.NewGuid(), redemptionDate = new DateOnly(2026, 9, 15), grossCents = 300_000L, netCents = (long?)null,
            funding = (object?)null,
        });
        redeem.StatusCode.ShouldBe(HttpStatusCode.OK, await redeem.Content.ReadAsStringAsync());

        var after = await Balances(s.Client);
        var entry = after.ByAccount.Single();
        entry.GrossYieldCents.ShouldBe(Yield(after, deposit));
        entry.GrossYieldCents.ShouldBeLessThan(before);
        entry.GrossYieldCents.ShouldBeGreaterThan(0);
        // Resgatou R$ 3.000,00 brutos: o principal que sobra é o do que não saiu.
        entry.PrincipalCents.ShouldBeLessThan(1_000_000);
        entry.PrincipalCents.ShouldBeGreaterThan(0);
    }

    [Fact]
    public async Task A_deleted_deposit_leaves_the_account_without_yield()
    {
        await using var s = await Start();
        var deposit = await Deposit(s.Client, 1_000_000, new DateOnly(2026, 7, 21), s.Checking, s.InvestmentA);

        (await s.Client.DeleteAsync($"/fixed-income-deposits/{deposit}?transfer=keep")).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        (await Balances(s.Client)).ByAccount.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_transfer_deleted_on_its_own_leaves_the_deposit_without_an_account()
    {
        await using var s = await Start();
        var deposit = await Deposit(s.Client, 1_000_000, new DateOnly(2026, 7, 21), s.Checking, s.InvestmentA);
        (await Balances(s.Client)).ByAccount.ShouldHaveSingleItem();

        // Excluir uma ponta da transferência pela tela de lançamentos leva a outra junto: o aporte volta a ser só acompanhado.
        var entries = await s.Client.GetFromJsonAsync<List<EntryDto>>("/transactions?from=2026-07-01&to=2026-07-31");
        (await s.Client.DeleteAsync($"/transactions/{entries!.First(e => e.TransferPairId is not null).Id}")).IsSuccessStatusCode.ShouldBeTrue();

        var b = await Balances(s.Client);
        b.ByAccount.ShouldBeEmpty();
        Yield(b, deposit).ShouldBeGreaterThan(0);
    }

    [Fact]
    public async Task Another_person_never_sees_the_accounts_of_someone_else()
    {
        await using var s = await Start();
        await Deposit(s.Client, 1_000_000, new DateOnly(2026, 7, 21), s.Checking, s.InvestmentA);

        using var stranger = await s.Factory.CreateAuthenticatedClientAsync();

        (await Balances(stranger)).ByAccount.ShouldBeEmpty();
    }
}
