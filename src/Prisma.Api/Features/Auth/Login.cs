using FluentValidation;
using Microsoft.AspNetCore.Identity;
using Prisma.Api.Infrastructure.Auth;
using Prisma.Api.Infrastructure.Http;
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

    public sealed class Handler(UserManager<AppUser> users, SignInManager<AppUser> signIn)
    {
        // Mesma mensagem para e-mail inexistente e senha errada: não revela quem tem conta.
        private static readonly Error InvalidCredentials =
            new(ErrorType.Unauthorized, "E-mail ou senha inválidos.");

        public async Task<Result<Response>> Execute(Request req, CancellationToken ct)
        {
            var user = await users.FindByEmailAsync(req.Email.Trim());
            if (user is null)
                return InvalidCredentials;

            var signedIn = await signIn.PasswordSignInAsync(user, req.Password, isPersistent: true, lockoutOnFailure: true);

            if (signedIn.IsLockedOut)
                return new Error(ErrorType.TooManyAttempts,
                    "Conta bloqueada temporariamente por excesso de tentativas. Tente novamente em alguns minutos.");

            if (!signedIn.Succeeded)
                return InvalidCredentials;

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
            .AddEndpointFilter<ValidationFilter<Request>>();
}
