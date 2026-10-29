using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Prisma.Api.Features.Assets;
using Prisma.Api.Infrastructure;
using Prisma.Api.Infrastructure.Market;
using Prisma.Api.Tests.Infrastructure;
using Prisma.Api.Tests.Market;
using Prisma.Domain.FixedIncome;
using Prisma.Domain.Market;
using Shouldly;
using static Prisma.Api.Tests.Market.FakeAssetListSource;

namespace Prisma.Api.Tests.Features;

// docs/patrimonio.md, I.9: o "Investido" vem dos ativos (a carteira e o rendimento da renda fixa), com ou sem trilha de caixa; a
// conta de investimento só entra se acompanha o caixa. O oráculo do "Investido" é a própria Carteira (`GET /portfolio`).
[Collection(ApiCollection.Name)]
public sealed class NetWorthApiTests(PostgresFixture postgres)
{
    private sealed record IdDto(Guid Id);

    private sealed record AssetDto(Guid Id, string Symbol);

    private sealed record BrokerDto(Guid AccountId, bool TracksCash, long? FreeCashCents);

    private sealed record NetWorthDto(
        long AvailableCents, int AvailableCount, long InvestedCents, long YieldCents, long BrokerCashCents, long TotalCents, List<BrokerDto> Brokers);

    private sealed record FixedIncomeLineDto(long PrincipalCents, long GrossYieldCents);

    private sealed record PortfolioDto(long InvestedCents, List<FixedIncomeLineDto> FixedIncome);

    private static readonly DateTime Monday = new(2026, 10, 19, 15, 0, 0, DateTimeKind.Utc);

    private readonly FakeAssetListSource _source = new();

    private sealed record Setup(PrismaApiFactory Factory, HttpClient Client, Guid Checking, Guid Broker, Guid Petr4);

    private async Task<Setup> Begin()
    {
        await MarketCatalog.ClearAsync(postgres.ConnectionString);
        var factory = new PrismaApiFactory(
            postgres.ConnectionString, new Dictionary<string, string> { ["Rates:Sync:Enabled"] = "false" }, new FakeClock(Monday), null,
            services => services.AddScoped<IAssetListSource>(_ => _source));
        _source.Assets = [Listed("PETR4", AssetKind.Stock, "PETROBRAS PN")];
        await factory.Services.GetRequiredService<AssetListSync>().RunAsync(CancellationToken.None);
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await db.ReferenceRates.ExecuteDeleteAsync();
            db.ReferenceRates.AddRange(RealCdi2026.Rates.Select(r => ReferenceRate.From(ReferenceRateSeries.Cdi, r, "bcb-sgs-12", DateTime.UtcNow)));
            await db.SaveChangesAsync();
        }

        var client = await factory.CreateAuthenticatedClientAsync();
        var checking = await PostId(client, "/accounts", new { name = "Itaú", type = "Checking", initialBalanceCents = 5_000_000 });
        var broker = await PostId(client, "/accounts", new { name = "Íon", type = "Investment", initialBalanceCents = 0 });
        var petr4 = (await client.GetFromJsonAsync<List<AssetDto>>("/assets?q=PETR4"))!.First(a => a.Symbol == "PETR4").Id;
        return new Setup(factory, client, checking, broker, petr4);
    }

    private static async Task<Guid> PostId(HttpClient client, string url, object body)
    {
        var response = await client.PostAsJsonAsync(url, body);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<IdDto>())!.Id;
    }

    private static async Task Deposit(Setup s, long cents, string date, bool funded)
    {
        var response = await s.Client.PostAsJsonAsync("/fixed-income-deposits", new
        {
            goalId = (Guid?)null, clientRequestId = Guid.NewGuid(), depositDate = date, amountCents = cents, productKind = "Cdb",
            indexKind = "Cdi", rateE4 = 1_000_000, maturityDate = (string?)null, liquidity = "Unknown", note = (string?)null,
            funding = funded ? new { fromAccountId = s.Checking, toAccountId = s.Broker, method = "Pix" } : null,
        });
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
    }

    private static async Task Transfer(Setup s, long cents, string date) =>
        (await s.Client.PostAsJsonAsync("/transfers", new
        {
            fromAccountId = s.Checking, toAccountId = s.Broker, amountCents = cents, date, method = "Pix",
        })).EnsureSuccessStatusCode();

    private static async Task Note(Setup s, string date, Guid? account) =>
        (await s.Client.PostAsJsonAsync("/trade-notes", new
        {
            broker = "Íon", date, feesCents = 4970, brokerageFeeCents = 0, settlementAccountId = account,
            trades = new[] { new { assetId = s.Petr4, side = "Buy", quantity = 100, unitPriceCents = 5700 } },
        })).EnsureSuccessStatusCode();

    private static async Task Track(Setup s, bool on) =>
        (await s.Client.SendAsync(new HttpRequestMessage(HttpMethod.Patch, $"/accounts/{s.Broker}")
        {
            Content = JsonContent.Create(new { name = "Íon", initialBalanceCents = 0, isActive = true, tracksCash = on }),
        })).EnsureSuccessStatusCode();

    private static async Task<NetWorthDto> NetWorth(Setup s) => (await s.Client.GetFromJsonAsync<NetWorthDto>("/dashboard/net-worth"))!;

    private static async Task<PortfolioDto> Portfolio(Setup s) => (await s.Client.GetFromJsonAsync<PortfolioDto>("/portfolio"))!;

    [Fact]
    public async Task Without_anything_everything_is_zero_but_the_available_money()
    {
        var s = await Begin();
        await using var _ = s.Factory;

        var net = await NetWorth(s);

        (net.AvailableCents, net.AvailableCount, net.InvestedCents, net.YieldCents, net.BrokerCashCents, net.TotalCents)
            .ShouldBe((5_000_000L, 1, 0L, 0L, 0L, 5_000_000L));
        net.Brokers.ShouldBe([new BrokerDto(s.Broker, false, null)]);
    }

    // O achado do dono: só um aporte tinha trilha de caixa, e a nota não estava vinculada. Agora todos os ativos contam.
    [Fact]
    public async Task Invested_counts_every_deposit_and_every_note_whatever_the_cash_trail()
    {
        var s = await Begin();
        await using var _ = s.Factory;
        await Deposit(s, 1_000_000, "2026-07-21", funded: true);
        await Deposit(s, 300_000, "2026-09-02", funded: false);
        await Note(s, "2026-10-01", account: null);

        var net = await NetWorth(s);
        var wallet = await Portfolio(s);

        // O que a Carteira diz: R$ 13.000,00 de renda fixa mais R$ 5.749,70 de ações ao custo.
        wallet.InvestedCents.ShouldBe(1_874_970);
        net.YieldCents.ShouldBe(wallet.FixedIncome.Sum(l => l.GrossYieldCents));
        net.YieldCents.ShouldBeGreaterThan(0);
        net.InvestedCents.ShouldBe(wallet.InvestedCents + net.YieldCents);
        // O aporte só acompanhado conta mesmo sem transferência, e o rendimento dele também (mais que o do aporte financiado sozinho).
        net.AvailableCents.ShouldBe(4_000_000);
        net.BrokerCashCents.ShouldBe(0);
    }

    [Fact]
    public async Task An_investment_account_that_does_not_track_cash_is_left_out_of_the_sums()
    {
        var s = await Begin();
        await using var _ = s.Factory;
        await Transfer(s, 250_000, "2026-10-01");

        var net = await NetWorth(s);

        // O dinheiro saiu do Disponível e a conta não acompanha o caixa: ele não aparece em lugar nenhum (limitação aceita).
        (net.AvailableCents, net.BrokerCashCents, net.InvestedCents, net.TotalCents).ShouldBe((4_750_000L, 0L, 0L, 4_750_000L));
    }

    [Fact]
    public async Task Tracking_the_cash_shows_what_is_parked_in_the_broker_without_counting_the_fixed_income_twice()
    {
        var s = await Begin();
        await using var _ = s.Factory;
        await Deposit(s, 1_000_000, "2026-07-21", funded: true);
        await Transfer(s, 250_000, "2026-10-01");
        await Track(s, true);

        var net = await NetWorth(s);
        var wallet = await Portfolio(s);

        // A Íon tem R$ 12.500,00 de saldo, dos quais R$ 10.000,00 já são o CDB: o caixa livre é R$ 2.500,00.
        net.BrokerCashCents.ShouldBe(250_000);
        net.Brokers.ShouldBe([new BrokerDto(s.Broker, true, 250_000)]);
        net.InvestedCents.ShouldBe(wallet.InvestedCents + net.YieldCents);
        net.AvailableCents.ShouldBe(3_750_000);
        net.TotalCents.ShouldBe(net.AvailableCents + net.InvestedCents + net.BrokerCashCents);
    }

    [Fact]
    public async Task A_linked_note_on_a_tracked_account_does_not_count_twice()
    {
        var s = await Begin();
        await using var _ = s.Factory;
        await Transfer(s, 574_970, "2026-10-01");
        await Track(s, true);
        var before = await NetWorth(s);

        await Note(s, "2026-10-02", account: s.Broker);
        var after = await NetWorth(s);

        // O saldo da Íon zerou, a compra entrou em "Investido" pelo custo, e o total não mudou.
        before.BrokerCashCents.ShouldBe(574_970);
        (after.BrokerCashCents, after.InvestedCents).ShouldBe((0L, 574_970L));
        after.TotalCents.ShouldBe(before.TotalCents);
    }

    [Fact]
    public async Task Turning_the_tracking_off_takes_the_broker_out_of_the_sums_and_changes_no_balance()
    {
        var s = await Begin();
        await using var _ = s.Factory;
        await Transfer(s, 250_000, "2026-10-01");
        await Track(s, true);
        (await NetWorth(s)).BrokerCashCents.ShouldBe(250_000);

        await Track(s, false);

        (await NetWorth(s)).BrokerCashCents.ShouldBe(0);
        (await s.Client.GetFromJsonAsync<List<BalanceDto>>("/accounts/balances"))!.Single(b => b.AccountId == s.Broker).BalanceCents.ShouldBe(250_000);
    }

    private sealed record BalanceDto(Guid AccountId, long? BalanceCents);

    [Fact]
    public async Task Inactive_accounts_and_cards_are_not_counted()
    {
        var s = await Begin();
        await using var _ = s.Factory;
        await PostId(s.Client, "/accounts", new { name = "Visa", type = "CreditCard", initialBalanceCents = 0, closingDay = 5, dueDay = 12 });
        var old = await PostId(s.Client, "/accounts", new { name = "Antiga", type = "Checking", initialBalanceCents = 777_000 });
        (await s.Client.SendAsync(new HttpRequestMessage(HttpMethod.Patch, $"/accounts/{old}")
        {
            Content = JsonContent.Create(new { name = "Antiga", initialBalanceCents = 777_000, isActive = false }),
        })).EnsureSuccessStatusCode();

        var net = await NetWorth(s);

        (net.AvailableCents, net.AvailableCount).ShouldBe((5_000_000L, 1));
    }

    [Fact]
    public async Task Another_person_sees_none_of_it()
    {
        var s = await Begin();
        await using var _ = s.Factory;
        await Deposit(s, 1_000_000, "2026-07-21", funded: true);
        using var stranger = await s.Factory.CreateAuthenticatedClientAsync();

        var net = (await stranger.GetFromJsonAsync<NetWorthDto>("/dashboard/net-worth"))!;

        (net.AvailableCents, net.InvestedCents, net.BrokerCashCents, net.TotalCents).ShouldBe((0L, 0L, 0L, 0L));
        net.Brokers.ShouldBeEmpty();
    }
}
