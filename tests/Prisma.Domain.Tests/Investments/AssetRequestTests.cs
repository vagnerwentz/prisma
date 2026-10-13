using Prisma.Domain.Investments;
using Shouldly;

namespace Prisma.Domain.Tests.Investments;

// docs/investimentos.md, "I.7c3": o pedido é só o código, no formato da bolsa e como o catálogo o guarda.
public sealed class AssetRequestTests
{
    private static readonly Guid UserId = Guid.CreateVersion7();

    [Theory]
    [InlineData("CVBI11", "CVBI11")]
    [InlineData("cvbi11", "CVBI11")]
    [InlineData("  CVBI 11 ", "CVBI11")]
    [InlineData("CVBI11F", "CVBI11")]
    [InlineData("bbas3f", "BBAS3")]
    [InlineData("BBAS3", "BBAS3")]
    [InlineData("TAEE11", "TAEE11")]
    [InlineData("EQMA3B", "EQMA3B")]
    [InlineData("B3SA3", "B3SA3")]
    public void Code_is_kept_as_the_catalog_keeps_it(string typed, string expected)
    {
        var request = AssetRequest.Create(UserId, typed).Value;

        (request.Symbol, request.UserId).ShouldBe((expected, UserId));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("CVBI")]
    [InlineData("CVBI111")]
    [InlineData("VBI CRI")]
    [InlineData("CV11")]
    [InlineData("CVBI11AB")]
    [InlineData("CVBÍ11")]
    public void Anything_but_a_code_is_refused(string? typed)
    {
        var error = AssetRequest.Create(UserId, typed).Error!;

        (error.Type, error.Message).ShouldBe((ErrorType.Validation, "Digite o código do ativo, como CVBI11."));
    }

    [Fact]
    public void A_valid_code_fits_the_column() =>
        AssetRequest.Create(UserId, "EQMA11BF").Value.Symbol.Length.ShouldBeLessThanOrEqualTo(AssetRequest.SymbolMaxLength);
}
