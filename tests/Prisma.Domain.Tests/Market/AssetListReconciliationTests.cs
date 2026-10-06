using CsCheck;
using Prisma.Domain.Market;
using Shouldly;

namespace Prisma.Domain.Tests.Market;

// docs/investimentos.md, seção 4: a lista do fornecedor contra o catálogo.
public sealed class AssetListReconciliationTests
{
    private static readonly DateTime Day1 = new(2026, 10, 4, 7, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime Day2 = Day1.AddDays(1);
    private static readonly DateTime Day3 = Day1.AddDays(2);

    [Fact]
    public void Empty_catalog_takes_the_whole_list()
    {
        var changes = AssetListReconciliation.Apply([], [Stock("BBAS3"), Fii("MXRF11")], Day1);

        changes.IsRefused.ShouldBeFalse();
        changes.Added.Select(a => a.Symbol).ShouldBe(["BBAS3", "MXRF11"]);
        changes.Added.ShouldAllBe(a => a.IsActive && a.CreatedAt == Day1 && a.UpdatedAt == Day1);
        var fii = changes.Added.Single(a => a.Symbol == "MXRF11");
        fii.Kind.ShouldBe(AssetKind.Fii);
        fii.LongName.ShouldBe("Fundo MXRF11");
    }

    [Fact]
    public void Same_list_again_changes_nothing()
    {
        var catalog = Catalog(Stock("BBAS3"), Fii("MXRF11"));

        var changes = AssetListReconciliation.Apply(catalog, [Stock("BBAS3"), Fii("MXRF11")], Day2);

        changes.Added.ShouldBeEmpty();
        (changes.Updated, changes.Reactivated, changes.Deactivated).ShouldBe((0, 0, 0));
        catalog.ShouldAllBe(a => a.UpdatedAt == Day1);
    }

    [Fact]
    public void Known_symbol_takes_the_new_name_and_kind_keeping_its_id()
    {
        var catalog = Catalog(Stock("TAEE11"));
        var id = catalog.Single().Id;

        var changes = AssetListReconciliation.Apply(catalog, [Listed("TAEE11", AssetKind.Unit, "TAESA", "Taesa Unit")], Day2);

        changes.Added.ShouldBeEmpty();
        changes.Updated.ShouldBe(1);
        var asset = catalog.Single();
        asset.Id.ShouldBe(id);
        (asset.Name, asset.LongName, asset.Kind).ShouldBe(("TAESA", "Taesa Unit", AssetKind.Unit));
        asset.UpdatedAt.ShouldBe(Day2);
        asset.CreatedAt.ShouldBe(Day1);
    }

    [Fact]
    public void Missing_symbol_becomes_inactive_and_stays_in_the_catalog()
    {
        var catalog = Catalog(Stock("BBAS3"), Stock("VIIA3"));

        var changes = AssetListReconciliation.Apply(catalog, [Stock("BBAS3")], Day2);

        changes.Deactivated.ShouldBe(1);
        var gone = catalog.Single(a => a.Symbol == "VIIA3");
        gone.IsActive.ShouldBeFalse();
        gone.InactiveSince.ShouldBe(Day2);
        gone.UpdatedAt.ShouldBe(Day2);
    }

    [Fact]
    public void Inactive_symbol_still_missing_keeps_the_day_it_left()
    {
        var catalog = Catalog(Stock("BBAS3"), Stock("VIIA3"));
        AssetListReconciliation.Apply(catalog, [Stock("BBAS3")], Day2);

        var changes = AssetListReconciliation.Apply(catalog, [Stock("BBAS3")], Day3);

        changes.Deactivated.ShouldBe(0);
        catalog.Single(a => a.Symbol == "VIIA3").InactiveSince.ShouldBe(Day2);
    }

    [Fact]
    public void Symbol_that_comes_back_is_reactivated_with_the_same_id()
    {
        var catalog = Catalog(Stock("BBAS3"), Stock("ITSA4"));
        var id = catalog.Single(a => a.Symbol == "ITSA4").Id;
        AssetListReconciliation.Apply(catalog, [Stock("BBAS3")], Day2);

        var changes = AssetListReconciliation.Apply(catalog, [Stock("BBAS3"), Stock("ITSA4")], Day3);

        changes.Added.ShouldBeEmpty();
        changes.Reactivated.ShouldBe(1);
        changes.Updated.ShouldBe(0);
        var back = catalog.Single(a => a.Symbol == "ITSA4");
        back.Id.ShouldBe(id);
        back.IsActive.ShouldBeTrue();
        back.InactiveSince.ShouldBeNull();
    }

    [Fact]
    public void List_with_less_than_half_of_the_active_assets_is_refused_and_changes_nothing()
    {
        var catalog = Catalog(Stock("AAAA3"), Stock("BBBB3"), Stock("CCCC3"), Stock("DDDD3"), Stock("EEEE3"));

        // 2 de 5 ativos: menos da metade.
        var changes = AssetListReconciliation.Apply(catalog, [Listed("AAAA3", AssetKind.Unit), Stock("ZZZZ3")], Day2);

        changes.IsRefused.ShouldBeTrue();
        (changes.ActiveBefore, changes.Listed).ShouldBe((5, 2));
        changes.Added.ShouldBeEmpty();
        catalog.ShouldAllBe(a => a.IsActive && a.UpdatedAt == Day1 && a.Kind == AssetKind.Stock);
    }

    [Fact]
    public void Empty_list_is_refused_when_the_catalog_has_assets()
    {
        var catalog = Catalog(Stock("BBAS3"));

        AssetListReconciliation.Apply(catalog, [], Day2).IsRefused.ShouldBeTrue();
        catalog.Single().IsActive.ShouldBeTrue();
    }

    [Fact]
    public void Exactly_half_of_the_active_assets_is_accepted()
    {
        var catalog = Catalog(Stock("AAAA3"), Stock("BBBB3"), Stock("CCCC3"), Stock("DDDD3"));

        var changes = AssetListReconciliation.Apply(catalog, [Stock("AAAA3"), Stock("BBBB3")], Day2);

        changes.IsRefused.ShouldBeFalse();
        changes.Deactivated.ShouldBe(2);
    }

    [Fact]
    public void The_sanity_check_counts_only_active_assets()
    {
        // 4 no catálogo, 3 já inativos: 1 ativo, e uma lista de 1 basta.
        var catalog = Catalog(Stock("AAAA3"), Stock("BBBB3"), Stock("CCCC3"), Stock("DDDD3"));
        AssetListReconciliation.Apply(catalog, [Stock("AAAA3"), Stock("BBBB3")], Day2);
        AssetListReconciliation.Apply(catalog, [Stock("AAAA3")], Day3);

        AssetListReconciliation.Apply(catalog, [Stock("AAAA3")], Day3.AddDays(1)).IsRefused.ShouldBeFalse();
    }

    [Fact]
    public void Repeated_symbol_in_the_list_counts_once()
    {
        var changes = AssetListReconciliation.Apply([], [Stock("BBAS3"), Listed("BBAS3", AssetKind.Unit)], Day1);

        changes.Added.Single().Kind.ShouldBe(AssetKind.Stock);
        changes.Listed.ShouldBe(1);
    }

    [Fact]
    public void Fractional_symbol_is_the_same_asset_as_the_standard_lot()
    {
        var changes = AssetListReconciliation.Apply([],
            [Listed("ITSA3F", AssetKind.Stock, "ITAUSA FRAC"), Listed("ITSA3", AssetKind.Stock, "ITAUSA S.A.")], Day1);

        var asset = changes.Added.Single();
        asset.Symbol.ShouldBe("ITSA3");
        asset.Name.ShouldBe("ITAUSA S.A."); // vale o lote padrão, mesmo vindo depois na lista
        changes.Listed.ShouldBe(1);
    }

    [Fact]
    public void Fractional_symbol_alone_enters_with_the_standard_lot_symbol()
    {
        var changes = AssetListReconciliation.Apply([], [Listed("BPAR3F", AssetKind.Stock), Listed("FSTU11F", AssetKind.Unit)], Day1);

        changes.Added.Select(a => a.Symbol).Order().ShouldBe(["BPAR3", "FSTU11"]);
    }

    [Fact]
    public void Fractional_symbol_keeps_the_standard_lot_active()
    {
        var catalog = Catalog(Stock("BPAR3"));

        var changes = AssetListReconciliation.Apply(catalog, [Stock("BPAR3F")], Day2);

        (changes.Added.Count, changes.Deactivated).ShouldBe((0, 0));
        catalog.Single().IsActive.ShouldBeTrue();
    }

    [Fact]
    public void Logo_address_follows_the_list()
    {
        var catalog = Catalog(Listed("BBAS3", AssetKind.Stock, logoUrl: "https://icons.example/BBAS3.svg"));

        var changed = AssetListReconciliation.Apply(catalog, [Listed("BBAS3", AssetKind.Stock, logoUrl: "https://icons.example/v2/BBAS3.svg")], Day2);
        var gone = AssetListReconciliation.Apply(catalog, [Listed("BBAS3", AssetKind.Stock)], Day3);

        changed.Updated.ShouldBe(1);
        gone.Updated.ShouldBe(1);
        catalog.Single().LogoUrl.ShouldBeNull();
    }

    [Fact]
    public void Fractional_symbol_alone_keeps_its_logo()
    {
        var changes = AssetListReconciliation.Apply([], [Listed("BPAR3F", AssetKind.Stock, logoUrl: "https://icons.example/BPAR3F.svg")], Day1);

        changes.Added.Single().LogoUrl.ShouldBe("https://icons.example/BPAR3F.svg");
    }

    [Fact]
    public void Requires_utc()
    {
        var local = new DateTime(2026, 10, 4, 4, 0, 0, DateTimeKind.Local);

        Should.Throw<ArgumentException>(() => AssetListReconciliation.Apply([], [Stock("BBAS3")], local));
    }

    // Invariante: aceita a lista, todo código listado está ativo, todo código fora dela está inativo, e nada
    // some do catálogo. Para quaisquer catálogo e lista.
    [Fact]
    public void Accepted_list_leaves_exactly_the_listed_symbols_active_and_never_removes()
    {
        var symbols = Gen.Int[0, 40].Select(n => $"AT{n:D2}3");
        var lists = symbols.HashSet[0, 30].Select(s => s.Order().ToList());

        Gen.Select(lists, lists).Sample((first, second) =>
        {
            var catalog = AssetListReconciliation.Apply([], first.Select(Stock).ToList(), Day1).Added.ToList();
            var changes = AssetListReconciliation.Apply(catalog, second.Select(Stock).ToList(), Day2);
            if (changes.IsRefused)
                return catalog.All(a => a.IsActive);

            var all = catalog.Concat(changes.Added).ToList();
            return all.Count == first.Union(second).Count()
                && all.Where(a => a.IsActive).Select(a => a.Symbol).Order().SequenceEqual(second.Order())
                && all.Select(a => a.Symbol).Distinct().Count() == all.Count;
        });
    }

    private static List<Asset> Catalog(params ListedAsset[] listed) =>
        AssetListReconciliation.Apply([], listed, Day1).Added.ToList();

    private static ListedAsset Stock(string symbol) => Listed(symbol, AssetKind.Stock);

    private static ListedAsset Fii(string symbol) => Listed(symbol, AssetKind.Fii, symbol, $"Fundo {symbol}");

    private static ListedAsset Listed(string symbol, AssetKind kind, string? name = null, string? longName = null, string? logoUrl = null)
    {
        ListedAsset.TryCreate(symbol, name ?? $"Empresa {symbol}", longName, kind, out var asset, logoUrl).ShouldBeTrue();
        return asset;
    }
}
