namespace Prisma.Api.Infrastructure.Market.Bcb;

// Configuração da API de dados abertos do Banco Central (seção "Bcb"). Pública e sem token.
public sealed class BcbOptions
{
    public const string Section = "Bcb";

    public Uri BaseUrl { get; init; } = new("https://api.bcb.gov.br/");

    // Uma requisição lenta não prende a tarefa: a próxima execução tenta de novo.
    public int TimeoutSeconds { get; init; } = 30;

    public bool IsValid() => BaseUrl.IsAbsoluteUri && TimeoutSeconds is > 0 and <= 300;
}
