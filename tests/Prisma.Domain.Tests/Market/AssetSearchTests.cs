using Prisma.Domain.Market;
using Shouldly;

namespace Prisma.Domain.Tests.Market;

// docs/investimentos.md, seção 8, etapa 3: busca sem diferenciar maiúsculas nem acentos, e o nome que se mostra.
public sealed class AssetSearchTests
{
    private static readonly DateTime Day1 = new(2026, 10, 4, 7, 0, 0, DateTimeKind.Utc);

    [Theory]
    [InlineData("TRANSMISSORA ALIANÇA DE ENERGIA ELÉTRICA", "transmissora alianca de energia eletrica")]
    [InlineData("  Ação   Ordinária ", "acao ordinaria")]
    [InlineData("BBAS3", "bbas3")]
    [InlineData("São Paulo\tS.A.", "sao paulo s.a.")]
    [InlineData("", "")]
    [InlineData("   ", "")]
    [InlineData(null, "")]
    public void Normalize_drops_case_accents_and_extra_spaces(string? text, string expected) =>
        AssetSearch.Normalize(text).ShouldBe(expected);

    [Fact]
    public void Term_is_cut_at_the_limit()
    {
        var typed = new string('a', AssetSearch.MaxTermLength + 10);

        AssetSearch.Term(typed).Length.ShouldBe(AssetSearch.MaxTermLength);
    }

    [Fact]
    public void Search_text_has_symbol_and_both_names()
    {
        var asset = New("TAEE11", AssetKind.Unit, "TRANSMISSORA ALIANÇA S.A.", "Taesa Unit");

        asset.SearchText.ShouldBe("taee11 transmissora alianca s.a. taesa unit");
    }

    [Fact]
    public void Search_text_follows_a_new_name()
    {
        var catalog = new List<Asset> { New("BBAS3", AssetKind.Stock, "BCO BRASIL S.A.", null) };

        AssetListReconciliation.Apply(catalog, [Listed("BBAS3", AssetKind.Stock, "BANCO DO BRASIL", null)], Day1.AddDays(1));

        catalog.Single().SearchText.ShouldBe("bbas3 banco do brasil");
    }

    [Theory]
    [InlineData(AssetKind.Stock, "BCO BRASIL S.A.")]
    [InlineData(AssetKind.Unit, "BCO BRASIL S.A.")]
    [InlineData(AssetKind.Fii, "Nome longo")]
    [InlineData(AssetKind.Etf, "Nome longo")]
    [InlineData(AssetKind.Bdr, "Nome longo")]
    [InlineData(AssetKind.OtherFund, "Nome longo")]
    public void Shares_show_the_company_name_and_funds_the_long_name(AssetKind kind, string expected) =>
        New("XPTO11", kind, "BCO BRASIL S.A.", "Nome longo").DisplayName.ShouldBe(expected);

    [Fact]
    public void Fund_without_long_name_shows_the_short_one() =>
        New("MXRF11", AssetKind.Fii, "MXRF11", null).DisplayName.ShouldBe("MXRF11");

    private static Asset New(string symbol, AssetKind kind, string name, string? longName) =>
        AssetListReconciliation.Apply([], [Listed(symbol, kind, name, longName)], Day1).Added.Single();

    private static ListedAsset Listed(string symbol, AssetKind kind, string name, string? longName)
    {
        ListedAsset.TryCreate(symbol, name, longName, kind, out var asset).ShouldBeTrue();
        return asset;
    }
}
