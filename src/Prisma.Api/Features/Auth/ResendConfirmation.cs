using FluentValidation;
using Microsoft.AspNetCore.Identity;
using Prisma.Api.Infrastructure;
using Prisma.Api.Infrastructure.Auth;
using Prisma.Api.Infrastructure.Email;
using Prisma.Api.Infrastructure.Http;

namespace Prisma.Api.Features.Auth;

// "Reenviar o e-mail de confirmação" (docs/email.md, 5.1). Responde sempre igual, exista a conta ou não, e já
// confirmada ou não: não conta a ninguém quem tem conta. O limite por pessoa fica na fila (EmailQueue).
public static class ResendConfirmation
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

    public sealed class Handler(AppDbContext db, UserManager<AppUser> users, EmailQueue emails)
    {
        public async Task Execute(Request req, CancellationToken ct)
        {
            var user = await users.FindByEmailAsync(req.Email.Trim());
            if (user is null || user.EmailConfirmed)
                return;

            await emails.EnqueueAsync(EmailKind.ConfirmEmail, user.Id, user.Email!, ct);
            await db.SaveChangesAsync(ct);
        }
    }

    public static void Map(IEndpointRouteBuilder app) =>
        app.MapPost("/resend-confirmation", async (Request request, Handler handler, CancellationToken ct) =>
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
