using System.Text.RegularExpressions;
using Prisma.Domain.Market;

namespace Prisma.Domain.Investments;

// "Não encontrou? Avise a gente" (docs/investimentos.md, I.7c3): a pessoa avisa que um código falta no catálogo. É
// só o código, sem texto livre; quem traz o ativo é a curadoria (ou a brapi). Se o ativo chegou não é gravado: é
// calculado na leitura, pelo catálogo.
//
// Um por código por pessoa, contando os cancelados: avisar de novo devolve o mesmo registro (o "Desfazer"), como a
// carteira (Holding).
public sealed partial class AssetRequest : Entity
{
    public const int MaxWaiting = 20;

    public const int SymbolMaxLength = 12;

    // Raiz de 4 caracteres, 1 ou 2 dígitos e a letra da classe quando houver (AssetSymbol), já sem o "F" do
    // fracionário.
    [GeneratedRegex("^[A-Z0-9]{4}[0-9]{1,2}[A-Z]?$")]
    private static partial Regex SymbolFormat();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Spaces();

    private AssetRequest() { }

    public string Symbol { get; private init; } = null!;

    // O código como o catálogo o guarda: sem espaços, em maiúsculas, sem o "F" do fracionário ("cvbi11f" → CVBI11).
    public static Result<string> Normalize(string? typed)
    {
        var symbol = AssetSymbol.BaseOf(Spaces().Replace(typed ?? "", "").ToUpperInvariant());
        return SymbolFormat().IsMatch(symbol)
            ? symbol
            : new Error(ErrorType.Validation, "Digite o código do ativo, como CVBI11.");
    }

    public static Result<AssetRequest> Create(Guid userId, string? typed)
    {
        var symbol = Normalize(typed);
        if (!symbol.IsSuccess)
            return symbol.Error;
        return new AssetRequest { UserId = userId, Symbol = symbol.Value };
    }

    public void Restore() => ClearDeletion();
}
