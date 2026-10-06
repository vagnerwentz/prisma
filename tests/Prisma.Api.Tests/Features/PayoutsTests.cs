using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Prisma.Api.Features.Assets;
using Prisma.Api.Infrastructure.Market;
using Prisma.Api.Tests.Infrastructure;
using Prisma.Api.Tests.Market;
using Prisma.Domain.Market;
using Shouldly;
using static Prisma.Api.Tests.Market.FakeAssetListSource;

namespace Prisma.Api.Tests.Features;

// docs/investimentos.md, etapa 5b: o provento é uma receita na conta onde o dinheiro caiu, ligada ao ativo.
[Collection(ApiCollection.Name)]
public sealed class PayoutsTests(PostgresFixture postgres)
{
    private sealed record IdDto(Guid Id);

    private sealed record AssetDto(Guid Id, string Symbol);

    private sealed record PayoutDto(
        Guid Id, Guid AccountId, Guid AssetId, string Symbol, string AssetName, string AssetKind, string Kind, long AmountCents, string Date);

    private sealed record TransactionDto(
        Guid Id, string Type, long AmountCents, string PurchaseDate, string SettlementDate, Guid? CategoryId, string Description,
        Guid? AssetId, string? PayoutKind, string? AssetSymbol);

    private sealed record HoldingDto(Guid Id, string Symbol);

    private sealed record BalanceDto(Guid AccountId, long? BalanceCents, long? ProjectedBalanceCents);

    private sealed record SummaryDto(long IncomeCents, long ExpenseCents);

    private sealed record CategoryDto(Guid Id, string Name, string Type);

    // 5 de outubro de 2026, meio-dia em São Paulo.
    private readonly FakeClock _clock = new(new DateTime(2026, 10, 5, 15, 0, 0, DateTimeKind.Utc));
    private readonly FakeAssetListSource _source = new();

    [Fact]
    public async Task Payout_is_income_in_the_chosen_account_tied_to_the_asset()
    {
        var (factory, client) = await Start();
        await using var _ = factory;
        var ion = await Account(client, "Íon", "Investment");
        var bbas = await AssetId(client, "BBAS3");
        // Ao abrir o app, a pessoa recebe as categorias padrão (GET /categories), entre elas "Rendimentos".
        var rendimentos = await IncomeCategory(client, "Rendimentos");

        var payout = await Create(client, ion, bbas, "InterestOnEquity", 3579, "2026-09-30");

        (payout.Symbol, payout.AssetName, payout.AssetKind, payout.Kind).ShouldBe(("BBAS3", "BCO BRASIL S.A.", "Stock", "InterestOnEquity"));
        (payout.AccountId, payout.AmountCents, payout.Date).ShouldBe((ion, 3579L, "2026-09-30"));

        var transaction = await client.GetFromJsonAsync<TransactionDto>($"/transactions/{payout.Id}");
        transaction!.Type.ShouldBe("Income");
        (transaction.PurchaseDate, transaction.SettlementDate).ShouldBe(("2026-09-30", "2026-09-30"));
        (transaction.AssetId, transaction.PayoutKind, transaction.AssetSymbol).ShouldBe((bbas, "InterestOnEquity", "BBAS3"));
        transaction.Description.ShouldBe("");
        rendimentos.ShouldNotBeNull();
        transaction.CategoryId.ShouldBe(rendimentos);
    }

    [Fact]
    public async Task Payout_in_a_checking_account_raises_its_balance_and_counts_as_income()
    {
        var (factory, client) = await Start();
        await using var _ = factory;
        var itau = await Account(client, "Itaú", "Checking");

        await Create(client, itau, await AssetId(client, "MXRF11"), "FundIncome", 980, "2026-09-15");
        await Create(client, itau, await AssetId(client, "BBAS3"), "Dividend", 4120, "2026-09-30");

        var balances = await client.GetFromJsonAsync<List<BalanceDto>>("/accounts/balances");
        balances!.Single(b => b.AccountId == itau).BalanceCents.ShouldBe(5100);
        var september = await client.GetFromJsonAsync<SummaryDto>("/dashboard/summary?month=2026-09");
        september!.IncomeCents.ShouldBe(5100);
        september.ExpenseCents.ShouldBe(0);
    }

    // Hoje é 5 de outubro: o provento anunciado para o dia 15 ainda não está no saldo, só no previsto.
    [Fact]
    public async Task Future_payout_is_only_in_the_projected_balance_until_its_day()
    {
        var (factory, client) = await Start();
        await using var _ = factory;
        var ion = await Account(client, "Íon", "Investment");
        await Create(client, ion, await AssetId(client, "BBAS3"), "Dividend", 4120, "2026-10-05");
        await Create(client, ion, await AssetId(client, "MXRF11"), "FundIncome", 980, "2026-10-15");

        var balance = (await client.GetFromJsonAsync<List<BalanceDto>>("/accounts/balances"))!.Single(b => b.AccountId == ion);

        (balance.BalanceCents, balance.ProjectedBalanceCents).ShouldBe((4120L, 5100L));
    }

    // A importação do histórico (docs/investimentos.md, I.6, decisão do dono): o dinheiro dos proventos antigos já
    // saiu da conta e não há gasto lançado; descontar a soma deles do saldo inicial deixa o saldo igual ao do banco,
    // sem inventar saída, e o mês continua mostrando o que entrou.
    [Fact]
    public async Task Past_payouts_offset_by_the_initial_balance_keep_the_bank_balance()
    {
        var (factory, client) = await Start();
        await using var _ = factory;
        var ion = await Account(client, "Íon", "Investment", initialBalanceCents: 100000);
        await Create(client, ion, await AssetId(client, "BBAS3"), "Dividend", 4120, "2026-02-27");
        await Create(client, ion, await AssetId(client, "MXRF11"), "FundIncome", 980, "2026-03-13");

        var edit = await client.PatchAsJsonAsync($"/accounts/{ion}", new
        {
            name = "Íon", initialBalanceCents = 100000 - 5100, closingDay = (int?)null, dueDay = (int?)null,
            creditLimitCents = (long?)null, isActive = true,
        });

        edit.StatusCode.ShouldBe(HttpStatusCode.OK);
        var balance = (await client.GetFromJsonAsync<List<BalanceDto>>("/accounts/balances"))!.Single(b => b.AccountId == ion);
        (balance.BalanceCents, balance.ProjectedBalanceCents).ShouldBe((100000L, 100000L));
        (await client.GetFromJsonAsync<SummaryDto>("/dashboard/summary?month=2026-02"))!.ShouldBe(new SummaryDto(4120, 0));
        (await client.GetFromJsonAsync<SummaryDto>("/dashboard/summary?month=2026-03"))!.ShouldBe(new SummaryDto(980, 0));
    }

    [Fact]
    public async Task List_brings_every_payout_newest_first()
    {
        var (factory, client) = await Start();
        await using var _ = factory;
        var ion = await Account(client, "Íon", "Investment");
        await Create(client, ion, await AssetId(client, "MXRF11"), "FundIncome", 980, "2026-08-15");
        await Create(client, ion, await AssetId(client, "BBAS3"), "Dividend", 4120, "2026-09-30");

        var payouts = await List(client);

        payouts.Select(p => (p.Symbol, p.Date)).ShouldBe([("BBAS3", "2026-09-30"), ("MXRF11", "2026-08-15")]);
        payouts.Single(p => p.Symbol == "MXRF11").AssetName.ShouldBe("Maxi Renda FII");
    }

    [Fact]
    public async Task Payout_puts_the_asset_in_the_portfolio_and_brings_back_one_that_was_removed()
    {
        var (factory, client) = await Start();
        await using var _ = factory;
        var ion = await Account(client, "Íon", "Investment");
        var bbas = await AssetId(client, "BBAS3");
        var held = await (await client.PostAsJsonAsync("/holdings", new { assetId = bbas })).Content.ReadFromJsonAsync<HoldingDto>();
        await client.DeleteAsync($"/holdings/{held!.Id}");

        await Create(client, ion, bbas, "Dividend", 4120, "2026-09-30");
        await Create(client, ion, await AssetId(client, "MXRF11"), "FundIncome", 980, "2026-09-15");

        var holdings = (await client.GetFromJsonAsync<List<HoldingDto>>("/holdings"))!;
        holdings.Select(h => h.Symbol).ShouldBe(["BBAS3", "MXRF11"]);
        holdings.Single(h => h.Symbol == "BBAS3").Id.ShouldBe(held.Id);
    }

    [Fact]
    public async Task Editing_changes_the_payout_and_keeps_it_tied_to_an_asset()
    {
        var (factory, client) = await Start();
        await using var _ = factory;
        var ion = await Account(client, "Íon", "Investment");
        var itau = await Account(client, "Itaú", "Checking");
        var created = await Create(client, ion, await AssetId(client, "BBAS3"), "Dividend", 4120, "2026-09-30");

        var response = await client.PutAsJsonAsync($"/payouts/{created.Id}", Body(itau, await AssetId(client, "MXRF11"), "FundIncome", 990, "2026-10-01"));

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var edited = (await List(client)).Single();
        (edited.Id, edited.AccountId, edited.Symbol, edited.Kind, edited.AmountCents, edited.Date)
            .ShouldBe((created.Id, itau, "MXRF11", "FundIncome", 990L, "2026-10-01"));
        // Era MXRF11, não BBAS3: o ativo certo entra na carteira, e o outro fica (a pessoa o tira se quiser).
        (await client.GetFromJsonAsync<List<HoldingDto>>("/holdings"))!.Select(h => h.Symbol).ShouldBe(["BBAS3", "MXRF11"]);
    }

    // A empresa fechou o capital e o código saiu da lista (fica inativo, nunca apagado): o que ela pagou antes ainda se lança.
    [Fact]
    public async Task Payout_of_an_asset_that_left_the_exchange_is_accepted()
    {
        var (factory, client) = await Start();
        await using var _ = factory;
        var ion = await Account(client, "Íon", "Investment");
        var bbas = await AssetId(client, "BBAS3");
        _source.Assets = [Listed("MXRF11", AssetKind.Fii, "MXRF11", "Maxi Renda FII"), Listed("ITSA4")];
        await factory.Services.GetRequiredService<AssetListSync>().RunAsync(CancellationToken.None);

        await Create(client, ion, bbas, "Dividend", 4120, "2026-03-05");

        (await List(client)).Single().Symbol.ShouldBe("BBAS3");
        (await client.GetFromJsonAsync<List<HoldingDto>>("/holdings"))!.Select(h => h.Symbol).ShouldBe(["BBAS3"]);
    }

    [Fact]
    public async Task The_common_edit_refuses_a_payout_and_the_payout_edit_refuses_other_entries()
    {
        var (factory, client) = await Start();
        await using var _ = factory;
        var ion = await Account(client, "Íon", "Investment");
        var payout = await Create(client, ion, await AssetId(client, "BBAS3"), "Dividend", 4120, "2026-09-30");
        var salary = await (await client.PostAsJsonAsync("/transactions", new
        {
            accountId = ion, type = "Income", amountCents = 500000, purchaseDate = "2026-09-05", method = "Pix", description = "Salário",
        })).Content.ReadFromJsonAsync<List<IdDto>>();

        var common = await client.PatchAsJsonAsync($"/transactions/{payout.Id}", new
        {
            accountId = ion, type = "Expense", amountCents = 4120, purchaseDate = "2026-09-30", method = "Pix", description = "x",
        });
        var asPayout = await client.PutAsJsonAsync($"/payouts/{salary!.Single().Id}", Body(ion, await AssetId(client, "BBAS3"), "Dividend", 1, "2026-09-05"));

        common.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await common.Content.ReadAsStringAsync()).ShouldContain("Provento usa a edição de provento.");
        asPayout.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await List(client)).Single().Kind.ShouldBe("Dividend");
    }

    [Fact]
    public async Task Deleting_and_undoing_use_the_entry_endpoints()
    {
        var (factory, client) = await Start();
        await using var _ = factory;
        var ion = await Account(client, "Íon", "Investment");
        var payout = await Create(client, ion, await AssetId(client, "BBAS3"), "Dividend", 4120, "2026-09-30");

        (await client.DeleteAsync($"/transactions/{payout.Id}")).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await List(client)).ShouldBeEmpty();

        (await client.PostAsync($"/transactions/{payout.Id}/restore", null)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await List(client)).Single().Id.ShouldBe(payout.Id);
    }

    [Fact]
    public async Task Payout_does_not_become_a_recurring_series()
    {
        var (factory, client) = await Start();
        await using var _ = factory;
        var ion = await Account(client, "Íon", "Investment");
        var payout = await Create(client, ion, await AssetId(client, "MXRF11"), "FundIncome", 980, "2026-09-15");

        var response = await client.PostAsJsonAsync($"/transactions/{payout.Id}/recurrence", new { frequency = "Monthly" });

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync()).ShouldContain("Provento não se repete.");
    }

    [Theory]
    [InlineData("card", HttpStatusCode.BadRequest)]
    [InlineData("unknown-account", HttpStatusCode.NotFound)]
    [InlineData("unknown-asset", HttpStatusCode.NotFound)]
    [InlineData("zero", HttpStatusCode.BadRequest)]
    [InlineData("bad-kind", HttpStatusCode.BadRequest)]
    public async Task Invalid_payout_is_refused(string problem, HttpStatusCode expected)
    {
        var (factory, client) = await Start();
        await using var _ = factory;
        var ion = await Account(client, "Íon", "Investment");
        var visa = await Account(client, "Visa", "CreditCard");
        var bbas = await AssetId(client, "BBAS3");

        var body = problem switch
        {
            "card" => Body(visa, bbas, "Dividend", 100, "2026-09-30"),
            "unknown-account" => Body(Guid.CreateVersion7(), bbas, "Dividend", 100, "2026-09-30"),
            "unknown-asset" => Body(ion, Guid.CreateVersion7(), "Dividend", 100, "2026-09-30"),
            "zero" => Body(ion, bbas, "Dividend", 0, "2026-09-30"),
            _ => Body(ion, bbas, "Bonus", 100, "2026-09-30"),
        };
        var response = await client.PostAsJsonAsync("/payouts", body);

        response.StatusCode.ShouldBe(expected);
        (await List(client)).ShouldBeEmpty();
    }

    [Fact]
    public async Task Each_person_sees_and_edits_only_their_own_payouts()
    {
        var (factory, ana) = await Start();
        await using var _ = factory;
        var bia = await factory.CreateAuthenticatedClientAsync();
        var anasAccount = await Account(ana, "Íon", "Investment");
        var biasAccount = await Account(bia, "XP", "Investment");
        var bbas = await AssetId(ana, "BBAS3");
        var anas = await Create(ana, anasAccount, bbas, "Dividend", 4120, "2026-09-30");

        (await List(bia)).ShouldBeEmpty();
        (await bia.PutAsJsonAsync($"/payouts/{anas.Id}", Body(biasAccount, bbas, "Dividend", 1, "2026-09-30")))
            .StatusCode.ShouldBe(HttpStatusCode.NotFound);
        // Nem lançar na conta de outra pessoa.
        (await bia.PostAsJsonAsync("/payouts", Body(anasAccount, bbas, "Dividend", 1, "2026-09-30")))
            .StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await List(ana)).Single().AmountCents.ShouldBe(4120);
    }

    [Fact]
    public async Task Requires_login()
    {
        var (factory, _) = await Start();
        await using var __ = factory;
        var anonymous = factory.CreateHttpsClient();

        (await anonymous.GetAsync("/payouts")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    private async Task<(PrismaApiFactory, HttpClient)> Start()
    {
        await MarketCatalog.ClearAsync(postgres.ConnectionString);
        var factory = new PrismaApiFactory(postgres.ConnectionString, null, _clock, null,
            services => services.AddScoped<IAssetListSource>(_ => _source));
        _source.Assets =
        [
            Listed("BBAS3", AssetKind.Stock, "BCO BRASIL S.A.", "Banco do Brasil S.A."),
            Listed("MXRF11", AssetKind.Fii, "MXRF11", "Maxi Renda FII"),
        ];
        await factory.Services.GetRequiredService<AssetListSync>().RunAsync(CancellationToken.None);
        return (factory, await factory.CreateAuthenticatedClientAsync());
    }

    private static object Body(Guid accountId, Guid assetId, string kind, long amountCents, string date) =>
        new { accountId, assetId, kind, amountCents, date };

    private static async Task<PayoutDto> Create(HttpClient client, Guid accountId, Guid assetId, string kind, long amountCents, string date)
    {
        var response = await client.PostAsJsonAsync("/payouts", Body(accountId, assetId, kind, amountCents, date));
        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        return (await response.Content.ReadFromJsonAsync<PayoutDto>())!;
    }

    private static async Task<List<PayoutDto>> List(HttpClient client) =>
        (await client.GetFromJsonAsync<List<PayoutDto>>("/payouts"))!;

    private static async Task<Guid> Account(HttpClient client, string name, string type, long initialBalanceCents = 0)
    {
        object body = type == "CreditCard"
            ? new { name, type, initialBalanceCents, closingDay = 26, dueDay = 5 }
            : new { name, type, initialBalanceCents };
        return (await (await client.PostAsJsonAsync("/accounts", body)).Content.ReadFromJsonAsync<IdDto>())!.Id;
    }

    private static async Task<Guid> AssetId(HttpClient client, string symbol) =>
        (await client.GetFromJsonAsync<List<AssetDto>>($"/assets?q={symbol}"))!.First(a => a.Symbol == symbol).Id;

    private static async Task<Guid?> IncomeCategory(HttpClient client, string name) =>
        (await client.GetFromJsonAsync<List<CategoryDto>>("/categories"))!.SingleOrDefault(c => c.Name == name && c.Type == "Income")?.Id;
}
