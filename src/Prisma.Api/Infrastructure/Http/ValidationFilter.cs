using FluentValidation;

namespace Prisma.Api.Infrastructure.Http;

// Valida o request do endpoint antes do handler e devolve 400 com as mensagens em pt-BR.
public sealed class ValidationFilter<TRequest>(IValidator<TRequest> validator) : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var request = context.Arguments.OfType<TRequest>().Single();
        var result = await validator.ValidateAsync(request, context.HttpContext.RequestAborted);

        if (result.IsValid)
            return await next(context);

        return Results.ValidationProblem(result.ToDictionary(), title: "Dados inválidos.");
    }
}
