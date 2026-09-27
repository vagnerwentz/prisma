namespace Prisma.Api.Infrastructure.Logging;

// Erro relatado pelo navegador, com a pilha que ele mandou: vai ao log como exceção, e o formatador o
// escreve no campo `exception`, como os erros do servidor. Nunca é lançado.
public sealed class BrowserError(string message, string? stack) : Exception(message)
{
    public override string? StackTrace => stack;

    public override string ToString() => string.IsNullOrEmpty(stack) ? Message : stack;
}
