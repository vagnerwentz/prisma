namespace Prisma.Api.Infrastructure.Market;

// O fornecedor de dados de mercado falhou: fora do ar, lento demais, resposta em formato inesperado. É
// exceção, não Result: não é erro de negócio nem chega ao usuário, só ao log da tarefa que o chamou.
//
// `SourceErrorCode` é o número que a página de rejeição do Banco Central mostra ("O código de erro é: <número>"), quando a
// resposta trouxe um: só dígitos, é o que o Banco pede se alguém abrir chamado. Nada mais do corpo da resposta é guardado.
public sealed class MarketDataException(string message, Exception? innerException = null, string? sourceErrorCode = null)
    : Exception(message, innerException)
{
    public string? SourceErrorCode { get; } = sourceErrorCode;
}
