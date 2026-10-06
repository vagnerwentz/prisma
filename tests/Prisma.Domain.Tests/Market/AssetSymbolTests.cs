using Prisma.Domain.Market;
using Shouldly;

namespace Prisma.Domain.Tests.Market;

public sealed class AssetSymbolTests
{
    [Theory]
    [InlineData("BBAS3F", "BBAS3")]
    [InlineData("TAEE11F", "TAEE11")]
    [InlineData("AAPL34F", "AAPL34")]
    [InlineData("03BK11F", "03BK11")]
    [InlineData("B3SA3F", "B3SA3")]
    [InlineData("EQMA3BF", "EQMA3B")] // ação classe B
    public void Fractional_symbol_maps_to_the_standard_lot(string fractional, string standard)
    {
        AssetSymbol.IsFractional(fractional).ShouldBeTrue();
        AssetSymbol.BaseOf(fractional).ShouldBe(standard);
    }

    [Theory]
    [InlineData("BBAS3")]
    [InlineData("MXRF11")]
    [InlineData("BDOM")] // fundo sem número
    [InlineData("ABCDF")] // termina em F, mas sem dígito antes
    [InlineData("ABC3F")] // raiz curta demais
    [InlineData("ABCD123F")] // dígitos demais
    [InlineData("EQMA3B")] // classe B no lote padrão
    public void Other_symbols_stay_as_they_are(string symbol)
    {
        AssetSymbol.IsFractional(symbol).ShouldBeFalse();
        AssetSymbol.BaseOf(symbol).ShouldBe(symbol);
    }
}
