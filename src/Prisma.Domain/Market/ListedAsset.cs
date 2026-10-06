using System.Diagnostics.CodeAnalysis;

namespace Prisma.Domain.Market;

// Um ativo como a lista da bolsa o descreve, antes de virar registro nosso (docs/investimentos.md, seção 3).
// Não depende do fornecedor: o adaptador da brapi traduz para cá, e um fornecedor futuro também.
public sealed record ListedAsset
{
    // Tamanho da coluna `symbol`. Os códigos da B3 têm de 5 a 6 caracteres; a folga cobre BDR e mudanças.
    public const int MaxSymbolLength = 12;

    private ListedAsset(string symbol, string name, string? longName, AssetKind kind, string? logoUrl)
    {
        Symbol = symbol;
        Name = name;
        LongName = longName;
        Kind = kind;
        LogoUrl = logoUrl;
    }

    // Código de negociação, em maiúsculas: "BBAS3", "MXRF11", "03BK11". Só letras e dígitos.
    public string Symbol { get; }

    public string Name { get; }

    // Nome completo. Nos fundos é o nome de verdade: o Name de um FII costuma ser o próprio código.
    public string? LongName { get; }

    public AssetKind Kind { get; }

    // Onde o fornecedor publica o logo (só https). Nulo quando não há logo: o ativo aparece com o código.
    public string? LogoUrl { get; }

    // O mesmo ativo com outro código: o fracionário vira o do lote padrão (AssetSymbol.BaseOf).
    internal ListedAsset WithSymbol(string symbol) => new(symbol, Name, LongName, Kind, LogoUrl);

    // Item que não serve (código vazio ou com caractere estranho, sem nome) fica de fora, sem derrubar a
    // lista: quem chama conta e registra.
    public static bool TryCreate(
        string? symbol, string? name, string? longName, AssetKind kind, [NotNullWhen(true)] out ListedAsset? asset,
        string? logoUrl = null)
    {
        asset = null;
        var normalizedSymbol = symbol?.Trim().ToUpperInvariant();
        var normalizedName = name?.Trim();
        if (!IsValidSymbol(normalizedSymbol) || string.IsNullOrEmpty(normalizedName))
            return false;

        var normalizedLongName = string.IsNullOrWhiteSpace(longName) ? null : longName.Trim();
        asset = new ListedAsset(normalizedSymbol, normalizedName, normalizedLongName, kind, SafeLogoUrl(logoUrl));
        return true;
    }

    // Endereço de logo que não seja https absoluto vira "sem logo": o ativo não é recusado por causa dele.
    private static string? SafeLogoUrl(string? url) =>
        Uri.TryCreate(url?.Trim(), UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps ? uri.AbsoluteUri : null;

    private static bool IsValidSymbol([NotNullWhen(true)] string? symbol) =>
        !string.IsNullOrEmpty(symbol) &&
        symbol.Length <= MaxSymbolLength &&
        symbol.All(char.IsAsciiLetterOrDigit);
}
