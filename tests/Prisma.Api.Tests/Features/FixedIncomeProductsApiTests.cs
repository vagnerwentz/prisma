using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Prisma.Api.Infrastructure;
using Prisma.Api.Tests.Infrastructure;
using Prisma.Domain.FixedIncome;
using Shouldly;

namespace Prisma.Api.Tests.Features;

// LCI e LCA na API (docs/renda-fixa.md, 6.5, R.13): o produto vive no aporte, a remuneração é a mesma do CDB e o imposto é outro: IR
// zero para pessoa física (Lei 11.033/2004, art. 3º, II e IV), IOF zero na LCA e tabela do CDB na LCI (Decreto 6.306/2007, art. 32, § 2º).
// O saldo bruto sai do mesmo cálculo de FixedIncomeBalancesApiTests (taxas reais do Banco Central de 21/07 a 15/10/2026).
[Collection(ApiCollection.Name)]
public sealed class FixedIncomeProductsApiTests(PostgresFixture postgres)
{
    private sealed record DepositDto(Guid Id, string ProductKind, string IndexKind, long RateE4);

    private sealed record TaxDto(
        long IofCents, long IrCents, long TotalCents, int IrRateBasisPoints, string RuleVersion, long NetYieldCents, long NetBalanceCents);

    private sealed record DepositBalanceDto(
        Guid DepositId, long PrincipalCents, string ProductKind, long? GrossBalanceCents, long? GrossYieldCents, TaxDto? Tax);

    private sealed record GroupDto(long GrossBalanceCents, long EstimatedTaxCents, long NetYieldCents, long NetBalanceCents);

    private sealed record LooseDto(GroupDto Balance, List<DepositBalanceDto> Deposits);

    private sealed record BalancesDto(LooseDto Unassigned, GroupDto Total);

    // Segunda-feira, 12h em São Paulo: "hoje" é 19/10/2026.
    private static readonly DateTime Monday = new(2026, 10, 19, 15, 0, 0, DateTimeKind.Utc);

    private PrismaApiFactory Factory() => new(
        postgres.ConnectionString, new Dictionary<string, string> { ["Rates:Sync:Enabled"] = "false" }, new FakeClock(Monday));

    private static async Task SeedRates(PrismaApiFactory factory)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.ReferenceRates.ExecuteDeleteAsync();
        db.ReferenceRates.AddRange(RealCdi2026.Rates.Select(r => ReferenceRate.From(ReferenceRateSeries.Cdi, r, "bcb-sgs-12", DateTime.UtcNow)));
        await db.SaveChangesAsync();
    }

    private static object Body(string product, DateOnly date, long amount, string index = "Cdi", long rate = 1_000_000) => new
    {
        goalId = (Guid?)null, clientRequestId = Guid.NewGuid(), depositDate = date, amountCents = amount, productKind = product,
        indexKind = index, rateE4 = rate, maturityDate = (DateOnly?)null, liquidity = "Unknown", note = (string?)null,
    };

    private static async Task<DepositDto> Create(HttpClient client, object body)
    {
        var response = await client.PostAsJsonAsync("/fixed-income-deposits", body);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<DepositDto>())!;
    }

    private static async Task<BalancesDto> Balances(HttpClient client, string asOf) =>
        (await client.GetFromJsonAsync<BalancesDto>($"/fixed-income/balances?asOf={asOf}"))!;

    // R$ 4.797,31 a 100% do CDI de 21/07 a 16/10/2026: saldo bruto R$ 4.950,51 e rendimento R$ 153,20 (o caso A de FixedIncomeBalancesApiTests),
    // 87 dias corridos. O CDB paga 22,5% de IR (R$ 34,47); a LCI e a LCA, nada.
    [Theory]
    [InlineData("Cdb", 3_447, 2_250, "cdb-lei-11033-2005")]
    [InlineData("Lci", 0, 0, "lci-lei-11033-2005")]
    [InlineData("Lca", 0, 0, "lca-lei-11033-2005")]
    public async Task The_same_deposit_pays_ir_in_the_cdb_and_not_in_the_letters(string product, long ir, int rateBasisPoints, string rule)
    {
        await using var factory = Factory();
        var client = await factory.CreateAuthenticatedClientAsync();
        await SeedRates(factory);

        var created = await Create(client, Body(product, new DateOnly(2026, 7, 21), 479_731));
        var balances = await Balances(client, "2026-10-16");

        created.ProductKind.ShouldBe(product);
        var item = balances.Unassigned.Deposits.Single();
        item.ProductKind.ShouldBe(product);
        item.GrossBalanceCents.ShouldBe(495_051);
        item.GrossYieldCents.ShouldBe(15_320);
        item.Tax.ShouldNotBeNull();
        item.Tax!.IofCents.ShouldBe(0);
        item.Tax.IrCents.ShouldBe(ir);
        item.Tax.IrRateBasisPoints.ShouldBe(rateBasisPoints);
        item.Tax.RuleVersion.ShouldBe(rule);
        item.Tax.NetYieldCents.ShouldBe(15_320 - ir);
        item.Tax.NetBalanceCents.ShouldBe(495_051 - ir);
        balances.Total.EstimatedTaxCents.ShouldBe(ir);
        balances.Total.NetBalanceCents.ShouldBe(495_051 - ir);
    }

    // 15 dias corridos (01/10 a 16/10): a tabela do IOF dá 50% do rendimento. A LCI paga isso e nenhum IR; a LCA, nada; o CDB, IOF e IR.
    [Fact]
    public async Task Within_30_days_the_lci_pays_iof_only_and_the_lca_pays_nothing()
    {
        await using var factory = Factory();
        var client = await factory.CreateAuthenticatedClientAsync();
        await SeedRates(factory);
        await Create(client, Body("Lci", new DateOnly(2026, 10, 1), 1_000_000));
        await Create(client, Body("Lca", new DateOnly(2026, 10, 1), 1_000_000));
        await Create(client, Body("Cdb", new DateOnly(2026, 10, 1), 1_000_000));

        var deposits = (await Balances(client, "2026-10-16")).Unassigned.Deposits;

        // Os três aportes são iguais, então têm o mesmo rendimento bruto; o imposto é que difere.
        var yield = deposits.Select(d => d.GrossYieldCents!.Value).Distinct().Single();
        yield.ShouldBeGreaterThan(100);
        var byRule = deposits.ToDictionary(d => d.Tax!.RuleVersion, d => d.Tax!);
        var expectedIof = (yield * 50 + 50) / 100;

        byRule["lci-lei-11033-2005"].IofCents.ShouldBe(expectedIof);
        byRule["lci-lei-11033-2005"].IrCents.ShouldBe(0);
        byRule["lca-lei-11033-2005"].TotalCents.ShouldBe(0);
        byRule["cdb-lei-11033-2005"].IofCents.ShouldBe(expectedIof);
        byRule["cdb-lei-11033-2005"].IrCents.ShouldBeGreaterThan(0);
    }

    [Fact]
    public async Task The_group_tax_sums_only_what_each_product_owes()
    {
        await using var factory = Factory();
        var client = await factory.CreateAuthenticatedClientAsync();
        await SeedRates(factory);
        await Create(client, Body("Cdb", new DateOnly(2026, 7, 21), 479_731));
        await Create(client, Body("Lci", new DateOnly(2026, 7, 21), 479_731));
        await Create(client, Body("Lca", new DateOnly(2026, 7, 21), 479_731, index: "Prefixed", rate: 140_000));

        var balances = await Balances(client, "2026-10-16");

        // Só o CDB paga: R$ 34,47. O resto do grupo sai igual ao bruto.
        balances.Total.EstimatedTaxCents.ShouldBe(3_447);
        balances.Total.NetBalanceCents.ShouldBe(balances.Total.GrossBalanceCents - 3_447);
    }

    [Fact]
    public async Task An_edit_can_change_the_product_and_the_tax_follows()
    {
        await using var factory = Factory();
        var client = await factory.CreateAuthenticatedClientAsync();
        await SeedRates(factory);
        var created = await Create(client, Body("Cdb", new DateOnly(2026, 7, 21), 479_731));

        var response = await client.PutAsJsonAsync($"/fixed-income-deposits/{created.Id}", new
        {
            depositDate = new DateOnly(2026, 7, 21), amountCents = 479_731L, productKind = "Lci", indexKind = "Cdi", rateE4 = 1_000_000L,
            maturityDate = (DateOnly?)null, liquidity = "Unknown", note = (string?)null,
        });

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await response.Content.ReadFromJsonAsync<DepositDto>())!.ProductKind.ShouldBe("Lci");
        (await Balances(client, "2026-10-16")).Unassigned.Deposits.Single().Tax!.IrCents.ShouldBe(0);
    }

    [Fact]
    public async Task An_unknown_product_is_refused()
    {
        await using var factory = Factory();
        var client = await factory.CreateAuthenticatedClientAsync();
        await SeedRates(factory);

        var response = await client.PostAsJsonAsync("/fixed-income-deposits", Body("Lcd", new DateOnly(2026, 10, 1), 100_000));

        response.IsSuccessStatusCode.ShouldBeFalse();
    }

    [Fact]
    public async Task The_database_refuses_a_product_it_does_not_know()
    {
        await using var factory = Factory();
        var client = await factory.CreateAuthenticatedClientAsync();
        await SeedRates(factory);
        var created = await Create(client, Body("Lci", new DateOnly(2026, 10, 1), 100_000));

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var update = () => db.Database.ExecuteSqlAsync($"UPDATE fixed_income_deposits SET product_kind = 'Lcd' WHERE id = {created.Id}");

        var thrown = await Should.ThrowAsync<PostgresException>(update);
        thrown.ConstraintName.ShouldBe("ck_fixed_income_deposits_product_kind");
    }
}
