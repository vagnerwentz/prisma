using System.Xml;
using System.Xml.Linq;

namespace Prisma.Api.Infrastructure.Market;

// Limpa o SVG de um logo antes de guardá-lo (docs/investimentos.md, etapa 4). SVG é documento, não só desenho:
// pode carregar script, buscar coisa de fora e embutir HTML. Fica só o desenho; o que não der para limpar é
// recusado (null) e o ativo segue com o código.
//
// A tela mostra o logo como <img> e a API o serve com uma CSP que não deixa nada executar: a limpeza é a primeira
// camada, não a única.
public static class SvgSanitizer
{
    public const int MaxBytes = 64 * 1024;

    private static readonly XNamespace Svg = "http://www.w3.org/2000/svg";
    private static readonly XNamespace XLink = "http://www.w3.org/1999/xlink";

    // Elementos que executam, embutem documento ou buscam algo de fora.
    private static readonly HashSet<string> Forbidden = new(StringComparer.OrdinalIgnoreCase)
    {
        "script", "foreignObject", "iframe", "object", "embed", "image", "audio", "video", "animate", "set",
        "animateMotion", "animateTransform", "handler", "listener",
    };

    public static string? Clean(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw) || System.Text.Encoding.UTF8.GetByteCount(raw) > MaxBytes)
            return null;

        XDocument document;
        try
        {
            // Sem DTD: nada de entidade (a "bomba de bilhões de risadas") nem arquivo externo.
            var settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null };
            using var reader = XmlReader.Create(new StringReader(raw), settings);
            document = XDocument.Load(reader);
        }
        catch (XmlException)
        {
            return null;
        }

        var root = document.Root;
        if (root is null || root.Name != Svg + "svg")
            return null;

        foreach (var element in root.DescendantsAndSelf().ToList())
        {
            if (element != root && (Forbidden.Contains(element.Name.LocalName) || element.Name.Namespace != Svg))
            {
                element.Remove();
                continue;
            }

            foreach (var attribute in element.Attributes().ToList())
                if (IsDangerous(attribute))
                    attribute.Remove();
        }

        // Sem declaração XML nem comentários: só o desenho.
        foreach (var node in document.DescendantNodes().OfType<XComment>().ToList())
            node.Remove();
        return root.ToString(SaveOptions.DisableFormatting);
    }

    private static bool IsDangerous(XAttribute attribute)
    {
        var name = attribute.Name.LocalName;
        var value = attribute.Value.Trim();

        // onload, onclick…
        if (name.StartsWith("on", StringComparison.OrdinalIgnoreCase))
            return true;

        // Link só para dentro do próprio desenho (#gradiente).
        if (name.Equals("href", StringComparison.OrdinalIgnoreCase) && (attribute.Name.Namespace == XLink || attribute.Name.Namespace == XNamespace.None))
            return !value.StartsWith('#');

        // url(...) só para dentro: url(#id).
        if (value.Contains("url(", StringComparison.OrdinalIgnoreCase) && !OnlyLocalUrls(value))
            return true;

        return value.Contains("javascript:", StringComparison.OrdinalIgnoreCase)
            || value.Contains("data:", StringComparison.OrdinalIgnoreCase)
            || name.Equals("style", StringComparison.OrdinalIgnoreCase) && value.Contains("@import", StringComparison.OrdinalIgnoreCase);
    }

    private static bool OnlyLocalUrls(string value)
    {
        var at = 0;
        while ((at = value.IndexOf("url(", at, StringComparison.OrdinalIgnoreCase)) >= 0)
        {
            var inside = value[(at + 4)..].TrimStart(' ', '\'', '"');
            if (!inside.StartsWith('#'))
                return false;
            at += 4;
        }

        return true;
    }
}
