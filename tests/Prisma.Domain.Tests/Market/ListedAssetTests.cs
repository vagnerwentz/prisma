using Prisma.Domain.Market;
using Shouldly;

namespace Prisma.Domain.Tests.Market;

public sealed class ListedAssetTests
{
    [Fact]
    public void Normalizes_symbol_and_trims_names()
    {
        ListedAsset.TryCreate(" bbas3 ", " BCO BRASIL S.A. ", " Banco do Brasil SA ", AssetKind.Stock, out var asset)
            .ShouldBeTrue();

        asset.Symbol.ShouldBe("BBAS3");
        asset.Name.ShouldBe("BCO BRASIL S.A.");
        asset.LongName.ShouldBe("Banco do Brasil SA");
        asset.Kind.ShouldBe(AssetKind.Stock);
    }

    [Theory]
    [InlineData("MXRF11")]
    [InlineData("03BK11")] // ETF cujo código começa com dígito
    [InlineData("AAPL34")]
    [InlineData("ABCDEFGHIJKL")] // 12, o limite
    public void Accepts_letters_and_digits_up_to_the_limit(string symbol) =>
        ListedAsset.TryCreate(symbol, "Nome", null, AssetKind.Fii, out _).ShouldBeTrue();

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("BBAS3.SA")]
    [InlineData("BB AS3")]
    [InlineData("^BVSP")]
    [InlineData("ABCDEFGHIJKLM")] // 13
    public void Rejects_symbol_that_is_not_a_b3_code(string? symbol)
    {
        ListedAsset.TryCreate(symbol, "Nome", null, AssetKind.Stock, out var asset).ShouldBeFalse();
        asset.ShouldBeNull();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void Rejects_missing_name(string? name) =>
        ListedAsset.TryCreate("BBAS3", name, "Banco do Brasil SA", AssetKind.Stock, out _).ShouldBeFalse();

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Blank_long_name_becomes_null(string? longName)
    {
        ListedAsset.TryCreate("MXRF11", "MXRF11", longName, AssetKind.Fii, out var asset).ShouldBeTrue();
        asset.LongName.ShouldBeNull();
    }

    [Theory]
    [InlineData("https://icons.brapi.dev/icons/BBAS3.svg", "https://icons.brapi.dev/icons/BBAS3.svg")]
    [InlineData(" https://icons.brapi.dev/icons/BBAS3.svg ", "https://icons.brapi.dev/icons/BBAS3.svg")]
    [InlineData("http://icons.brapi.dev/icons/BBAS3.svg", null)] // só https
    [InlineData("/icons/BBAS3.svg", null)]
    [InlineData("javascript:alert(1)", null)]
    [InlineData("", null)]
    [InlineData(null, null)]
    public void Logo_address_is_kept_only_when_it_is_https(string? logoUrl, string? expected)
    {
        ListedAsset.TryCreate("BBAS3", "BCO BRASIL S.A.", null, AssetKind.Stock, out var asset, logoUrl).ShouldBeTrue();

        asset.LogoUrl.ShouldBe(expected);
    }
}
