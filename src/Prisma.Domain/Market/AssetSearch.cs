using System.Globalization;
using System.Text;

namespace Prisma.Domain.Market;

// Como se procura um ativo (docs/investimentos.md, seção 8, etapa 3): por pedaço do código ou do nome, sem
// diferenciar maiúsculas nem acentos. Os nomes vêm do fornecedor em maiúsculas e com acento ("TRANSMISSORA
// ALIANÇA DE ENERGIA ELÉTRICA"), e quem digita "alianca" precisa achar.
//
// O texto de busca é guardado pronto no ativo (Asset.SearchText) e o termo é normalizado igual: o banco só
// compara, sem extensão do Postgres (unaccent) nem função por linha.
public static class AssetSearch
{
    public const int MaxTermLength = 50;

    // Minúsculas, sem acento, com um espaço só entre palavras. "  Aliança  ELÉTRICA " → "alianca eletrica".
    public static string Normalize(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return "";

        var builder = new StringBuilder(text.Length);
        var pendingSpace = false;
        foreach (var c in text.Normalize(NormalizationForm.FormD))
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark)
                continue;
            if (char.IsWhiteSpace(c))
            {
                pendingSpace = builder.Length > 0;
                continue;
            }

            if (pendingSpace)
                builder.Append(' ');
            pendingSpace = false;
            builder.Append(char.ToLowerInvariant(c));
        }

        return builder.ToString().Normalize(NormalizationForm.FormC);
    }

    // O termo digitado, pronto para comparar com o SearchText. Termo longo demais é cortado: ninguém digita 50
    // letras para achar um ativo, e a consulta não precisa receber um texto de qualquer tamanho.
    public static string Term(string? typed)
    {
        var term = Normalize(typed);
        return term.Length > MaxTermLength ? term[..MaxTermLength] : term;
    }

    internal static string TextOf(string symbol, string name, string? longName) =>
        Normalize($"{symbol} {name} {longName}");
}
