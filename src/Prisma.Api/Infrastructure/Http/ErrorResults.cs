using Prisma.Domain;

namespace Prisma.Api.Infrastructure.Http;

public static class ErrorResults
{
    public static IResult ToProblem(this Error error)
    {
        var (status, title) = error.Type switch
        {
            ErrorType.Validation => (StatusCodes.Status400BadRequest, "Dados inválidos."),
            ErrorType.Unauthorized => (StatusCodes.Status401Unauthorized, "Não autorizado."),
            ErrorType.NotFound => (StatusCodes.Status404NotFound, "Não encontrado."),
            ErrorType.Conflict => (StatusCodes.Status409Conflict, "Conflito."),
            ErrorType.TooManyAttempts => (StatusCodes.Status429TooManyRequests, "Muitas tentativas."),
            _ => throw new ArgumentOutOfRangeException(nameof(error), error.Type, null),
        };

        return Results.Problem(detail: error.Message, statusCode: status, title: title);
    }
}
