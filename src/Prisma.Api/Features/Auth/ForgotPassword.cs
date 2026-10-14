using FluentValidation;
using Microsoft.AspNetCore.Identity;
using Prisma.Api.Infrastructure;
using Prisma.Api.Infrastructure.Auth;
using Prisma.Api.Infrastructure.Email;
using Prisma.Api.Infrastructure.Http;
using Prisma.Api.Infrastructure.Logging;

namespace Prisma.Api.Features.Auth;

// "Esqueci a senha" (docs/email.md, 5.2): pede o e-mail com o link de senha nova. Responde sempre igual, exista a conta
// ou não: não conta a ninguém quem tem conta. O limite por pessoa fica na fila (EmailQueue), e o por IP no rate limit.
// Conta sem o e-mail confirmado também recebe: o link prova o e-mail, e criar a senha confirma.
public static class ForgotPassword
{
    public sealed record Request(string Email);

    public sealed class Validator : AbstractValidator<Request>
    {
        public Validator()
        {
            RuleFor(x => x.Email)
                .NotEmpty().WithMessage("Informe o e-mail.")
                .EmailAddress().WithMessage("E-mail inválido.");
        }
    }

    public sealed class Handler(AppDbContext db, UserManager<AppUser> users, EmailQueue emails, ILogger<Handler> logger)
    {
        public async Task Execute(Request req, CancellationToken ct)
        {
            var user = await users.FindByEmailAsync(req.Email.Trim());
            if (user is null)
            {
                logger.PasswordResetRequestedForUnknownEmail();
                return;
            }

            if (await emails.EnqueueAsync(EmailKind.ResetPassword, user.Id, user.Email!, ct))
            {
                await db.SaveChangesAsync(ct);
                logger.PasswordResetRequested(user.Id);
            }
        }
    }

    public static void Map(IEndpointRouteBuilder app) =>
        app.MapPost("/forgot-password", async (Request request, Handler handler, CancellationToken ct) =>
            {
                await handler.Execute(request, ct);
                return Results.Accepted();
            })
            .AllowAnonymous()
            .RequireRateLimiting(AuthSetup.RateLimitPolicy)
            .AddEndpointFilter<ValidationFilter<Request>>()
            .Produces(202)
            .ProducesValidationProblem()
            .ProducesProblem(429);
}
