namespace Prisma.Api.Infrastructure.Market.Brapi;

// Configuração da brapi (seção "Brapi"). O token vem dos User Secrets em desenvolvimento e de
// Brapi__Token em produção; nunca do repositório. A lista de ativos funciona sem ele.
public sealed class BrapiOptions
{
    public const string Section = "Brapi";

    public Uri BaseUrl { get; init; } = new("https://brapi.dev/");

    public string? Token { get; init; }

    // Uma requisição lenta não prende a tarefa: a próxima execução tenta de novo.
    public int TimeoutSeconds { get; init; } = 30;

    // 1.000 por página trazem a lista inteira (2.337 ativos em 2026-10) em 3 requisições. A brapi corta
    // em 2.000 sem avisar (pedir 3.000 devolve 2.000), por isso o teto da validação.
    public int PageSize { get; init; } = 1000;

    // Teto contra paginação que nunca acaba (hasNextPage sempre verdadeiro): 20 mil ativos.
    public int MaxPages { get; init; } = 20;

    public bool IsValid() =>
        BaseUrl.IsAbsoluteUri &&
        TimeoutSeconds is > 0 and <= 300 &&
        PageSize is > 0 and <= 2000 &&
        MaxPages > 0;
}
