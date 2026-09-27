using FluentValidation;
using Prisma.Api.Infrastructure.Auth;
using Prisma.Api.Infrastructure.Http;
using Prisma.Api.Infrastructure.Logging;

namespace Prisma.Api.Features.Diagnostics;

// Erro do navegador (tela quebrada ou erro silencioso) registrado no log do servidor (etapa H.3b), com o
// userId da sessão quando há uma. Só mensagem, pilha, tela (sem consulta), tipo e versão: nada do que
// estava na tela. Aberto sem login (a tela de entrar também quebra), com rate limit próprio.
public static class ReportClientError
{
    public const int MaxMessageLength = 2000;
    public const int MaxStackLength = 8000;

    public sealed record Request(string Message, string? Stack, string Screen, string Kind, string? AppVersion);

    public sealed class Validator : AbstractValidator<Request>
    {
        public Validator()
        {
            RuleFor(x => x.Message).NotEmpty().MaximumLength(MaxMessageLength).WithMessage("Mensagem inválida.");
            RuleFor(x => x.Stack).MaximumLength(MaxStackLength).WithMessage("Pilha grande demais.");
            RuleFor(x => x.Kind).Must(kind => kind is "crash" or "silent").WithMessage("Tipo de erro inválido.");
            RuleFor(x => x.Screen)
                .NotEmpty().MaximumLength(300)
                .Must(screen => screen.StartsWith('/') && !screen.StartsWith("//", StringComparison.Ordinal))
                .WithMessage("Tela inválida.");
            RuleFor(x => x.AppVersion).MaximumLength(40).WithMessage("Versão inválida.");
        }
    }

    public sealed class Handler(ILogger<Handler> logger)
    {
        public void Execute(Request req) =>
            logger.ClientError(
                new BrowserError(req.Message, req.Stack), ScreenOf(req.Screen), req.Kind, req.AppVersion ?? "unknown", req.Message);

        // Só o caminho: a consulta (?estorno=…) e o fragmento podem levar dados.
        private static string ScreenOf(string screen) => screen.Split('?', '#')[0];
    }

    public static void Map(IEndpointRouteBuilder app) =>
        app.MapPost("/client-errors", (Request request, Handler handler) =>
            {
                handler.Execute(request);
                return Results.NoContent();
            })
            .AllowAnonymous()
            .RequireRateLimiting(AuthSetup.ClientErrorsRateLimitPolicy)
            .AddEndpointFilter<ValidationFilter<Request>>()
            .Produces(204)
            .ProducesValidationProblem()
            .ProducesProblem(429);
}
