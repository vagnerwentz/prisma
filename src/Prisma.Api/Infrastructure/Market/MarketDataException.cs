namespace Prisma.Api.Infrastructure.Market;

// O fornecedor de dados de mercado falhou: fora do ar, lento demais, resposta em formato inesperado. É
// exceção, não Result: não é erro de negócio nem chega ao usuário, só ao log da tarefa que o chamou.
public sealed class MarketDataException(string message, Exception? innerException = null)
    : Exception(message, innerException);
