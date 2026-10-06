using System.Security.Cryptography;
using System.Text;

namespace Prisma.Domain.Market;

// O logo de um ativo, já limpo, guardado no Prisma (docs/investimentos.md, etapa 4): a tela não depende do
// fornecedor estar no ar, e o fornecedor não fica sabendo o que a pessoa olha. Global, como o catálogo.
public sealed class AssetLogo
{
    private AssetLogo() { }

    public Guid AssetId { get; private init; }

    public string Svg { get; private set; } = null!;

    // Identifica o conteúdo: não regrava o que não mudou, e serve de ETag.
    public byte[] Sha256 { get; private set; } = null!;

    // De onde veio: se o fornecedor mudar o endereço, o logo é baixado de novo.
    public string SourceUrl { get; private set; } = null!;

    public DateTime FetchedAt { get; private set; }

    public static AssetLogo Create(Guid assetId, string svg, string sourceUrl, DateTime utcNow)
    {
        var logo = new AssetLogo { AssetId = assetId };
        logo.Replace(svg, sourceUrl, utcNow);
        return logo;
    }

    // Devolve se o desenho mudou. O endereço e a data sempre se atualizam: o logo foi conferido agora.
    public bool Replace(string svg, string sourceUrl, DateTime utcNow)
    {
        var hash = HashOf(svg);
        var changed = Sha256 is null || !hash.AsSpan().SequenceEqual(Sha256);
        Svg = svg;
        Sha256 = hash;
        SourceUrl = sourceUrl;
        FetchedAt = utcNow;
        return changed;
    }

    public static byte[] HashOf(string svg) => SHA256.HashData(Encoding.UTF8.GetBytes(svg));
}
