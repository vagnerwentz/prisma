using FluentValidation;
using Microsoft.AspNetCore.Identity;
using Prisma.Api.Infrastructure.Auth;
using Prisma.Api.Infrastructure.Http;
using Prisma.Api.Infrastructure.Logging;
using Prisma.Api.Infrastructure.Privacy;
using Prisma.Domain;

namespace Prisma.Api.Features.Auth;

// "Apagar conta" (etapa A.3): a pessoa apaga a própria conta e tudo o que é dela, de vez, sem "Desfazer". Pede a senha:
// uma sessão esquecida aberta não basta para apagar tudo. A senha errada conta para o bloqueio, como no login.
//
// Errar a senha responde 400 (com o código WrongPassword), e não 401: na tela, o 401 leva ao login e derrubaria a
// sessão de quem só errou a senha. Depois de apagar, a sessão daqui sai; a de outro aparelho cai na próxima conferência
// do cookie (AuthSetup.SessionCheckInterval), porque o usuário não existe mais.
public static class DeleteMyAccount
{
    public sealed record Request(string Password);

    public static readonly Error WrongPassword = new(ErrorType.Validation, "Senha incorreta.", "WrongPassword");

    public sealed class Validator : AbstractValidator<Request>
    {
        public Validator() => RuleFor(x => x.Password).NotEmpty().WithMessage("Informe a senha.");
    }

    public sealed class Handler(
        UserManager<AppUser> users,
        SignInManager<AppUser> signIn,
        ICurrentUser currentUser,
        AccountEraser eraser,
        ILogger<Handler> logger)
    {
        private Error LockedOut(Guid userId)
        {
            logger.AccountDeletionRejected(userId, "LockedOut");
            return new Error(ErrorType.TooManyAttempts,
                "Muitas tentativas com a senha errada. Tente novamente em alguns minutos.");
        }

        public async Task<Result<bool>> Execute(Request req, CancellationToken ct)
        {
            var user = await users.FindByIdAsync(currentUser.UserId.ToString());
            if (user is null)
                return new Error(ErrorType.Unauthorized, "Sessão expirada. Entre de novo.");

            if (await users.IsLockedOutAsync(user))
                return LockedOut(user.Id);

            if (!await users.CheckPasswordAsync(user, req.Password))
            {
                await users.AccessFailedAsync(user);
                if (await users.IsLockedOutAsync(user))
                {
                    // A sessão segue aberta, mas o bloqueio também vale para o login: avisa como o login avisaria.
                    return LockedOut(user.Id);
                }

                logger.AccountDeletionRejected(user.Id, "WrongPassword");
                return WrongPassword;
            }

            await eraser.EraseAsync(user.Id, ct);
            await signIn.SignOutAsync();
            logger.AccountDeleted(user.Id);
            return true;
        }
    }

    public static void Map(IEndpointRouteBuilder app) =>
        app.MapPost("/delete-account", async (Request request, Handler handler, CancellationToken ct) =>
            {
                var result = await handler.Execute(request, ct);
                return result.IsSuccess ? Results.NoContent() : result.Error.ToProblem();
            })
            .RequireRateLimiting(AuthSetup.RateLimitPolicy)
            .AddEndpointFilter<ValidationFilter<Request>>()
            .Produces(204)
            .ProducesValidationProblem()
            .ProducesProblem(401)
            .ProducesProblem(429);
}
