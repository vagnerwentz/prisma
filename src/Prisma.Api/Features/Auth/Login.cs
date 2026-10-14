using FluentValidation;
using Microsoft.AspNetCore.Identity;
using Prisma.Api.Infrastructure.Auth;
using Prisma.Api.Infrastructure.Http;
using Prisma.Api.Infrastructure.Logging;
using Prisma.Domain;

namespace Prisma.Api.Features.Auth;

public static class Login
{
    public sealed record Request(string Email, string Password);

    public sealed record Response(Guid Id, string Email);

    public sealed class Validator : AbstractValidator<Request>
    {
        public Validator()
        {
            RuleFor(x => x.Email).NotEmpty().WithMessage("Informe o e-mail.");
            RuleFor(x => x.Password).NotEmpty().WithMessage("Informe a senha.");
        }
    }

    public sealed class Handler(UserManager<AppUser> users, SignInManager<AppUser> signIn, ILogger<Handler> logger)
    {
        // Mesma mensagem para e-mail inexistente e senha errada: não revela quem tem conta.
        private static readonly Error InvalidCredentials =
            new(ErrorType.Unauthorized, "E-mail ou senha inválidos.");

        public static readonly Error EmailNotConfirmed = new(ErrorType.Forbidden,
            "Falta confirmar seu e-mail. Abra o link que enviamos ou peça outro.", "EmailNotConfirmed");

        private Error LockedOut(AppUser user)
        {
            logger.LoginLockedOut(user.Id);
            return new Error(ErrorType.TooManyAttempts,
                "Conta bloqueada temporariamente por excesso de tentativas. Tente novamente em alguns minutos.");
        }

        public async Task<Result<Response>> Execute(Request req, CancellationToken ct)
        {
            var user = await users.FindByEmailAsync(req.Email.Trim());
            if (user is null)
            {
                logger.LoginUnknownEmail();
                return InvalidCredentials;
            }

            // A senha vem antes de tudo: o SignInManager responderia "e-mail não confirmado" sem conferir a senha, e o
            // estado da conta apareceria para qualquer um. O bloqueio por tentativas vale igual para quem não confirmou.
            if (await users.IsLockedOutAsync(user))
                return LockedOut(user);

            if (!await users.CheckPasswordAsync(user, req.Password))
            {
                await users.AccessFailedAsync(user);
                if (await users.IsLockedOutAsync(user))
                    return LockedOut(user);
                logger.LoginFailed(user.Id, "WrongPassword");
                return InvalidCredentials;
            }

            await users.ResetAccessFailedCountAsync(user);
            if (!user.EmailConfirmed)
            {
                logger.LoginFailed(user.Id, "EmailNotConfirmed");
                return EmailNotConfirmed;
            }

            await signIn.SignInAsync(user, isPersistent: true);
            logger.LoginSucceeded(user.Id);
            return new Response(user.Id, user.Email!);
        }
    }

    public static void Map(IEndpointRouteBuilder app) =>
        app.MapPost("/login", async (Request request, Handler handler, CancellationToken ct) =>
            {
                var result = await handler.Execute(request, ct);
                return result.IsSuccess ? Results.Ok(result.Value) : result.Error.ToProblem();
            })
            .AllowAnonymous()
            .RequireRateLimiting(AuthSetup.RateLimitPolicy)
            .AddEndpointFilter<ValidationFilter<Request>>()
            .Produces<Response>(200)
            .ProducesValidationProblem()
            .ProducesProblem(401)
            .ProducesProblem(403)
            .ProducesProblem(429);
}
