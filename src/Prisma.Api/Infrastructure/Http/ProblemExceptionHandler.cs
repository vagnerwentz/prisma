using Microsoft.AspNetCore.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Prisma.Api.Infrastructure.Logging;

namespace Prisma.Api.Infrastructure.Http;

// Exceção que escapou do handler vira ProblemDetails em pt-BR, no mesmo formato dos erros
// esperados. Corpo que não dá para ler (JSON malformado, número enviado como texto, enum
// desconhecido) é 400; conflito de concorrência (outro pedido mudou o registro antes) é 409; o
// resto, 500.
// O log registra só o tipo e a pilha da exceção: as mensagens do domínio não levam valores.
public sealed class ProblemExceptionHandler(IProblemDetailsService problemDetails, ILogger<ProblemExceptionHandler> logger)
    : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext context, Exception exception, CancellationToken ct)
    {
        var (status, title, detail) = exception switch
        {
            BadHttpRequestException bad => (
                bad.StatusCode, "Dados inválidos.", "Os dados enviados estão em um formato que a API não entende."),
            DbUpdateConcurrencyException => (
                StatusCodes.Status409Conflict, "Conflito.",
                "Estes dados mudaram enquanto você salvava. Atualize a tela e tente de novo."),
            _ => (
                StatusCodes.Status500InternalServerError, "Erro inesperado.",
                "Algo deu errado do nosso lado. Tente novamente em instantes."),
        };

        if (status == StatusCodes.Status500InternalServerError)
            logger.UnhandledException(exception, context.Request.Method, AppLog.RouteOf(context));
        else if (status == StatusCodes.Status409Conflict)
            logger.ConcurrencyConflict(context.Request.Method, AppLog.RouteOf(context));

        context.Response.StatusCode = status;
        return await problemDetails.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = context,
            Exception = exception,
            ProblemDetails = { Status = status, Title = title, Detail = detail },
        });
    }
}
