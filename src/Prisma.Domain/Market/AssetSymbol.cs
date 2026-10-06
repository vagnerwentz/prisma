using System.Text.RegularExpressions;

namespace Prisma.Domain.Market;

// Regras do código de negociação da B3 (docs/investimentos.md, seção 3).
public static partial class AssetSymbol
{
    // Mercado fracionário: quem compra menos que o lote padrão compra "BBAS3F", que é a mesma ação que BBAS3
    // (mesmo papel, mesmos proventos). Raiz de 4 caracteres, 1 ou 2 dígitos, a letra da classe quando houver
    // ("EQMA3B", ação classe B) e o "F" no fim.
    [GeneratedRegex("^([A-Z0-9]{4}[0-9]{1,2}[A-Z]?)F$")]
    private static partial Regex Fractional();

    public static bool IsFractional(string symbol) => Fractional().IsMatch(symbol);

    // O código do lote padrão: "BBAS3F" → "BBAS3"; qualquer outro fica como está.
    public static string BaseOf(string symbol)
    {
        var match = Fractional().Match(symbol);
        return match.Success ? match.Groups[1].Value : symbol;
    }
}
