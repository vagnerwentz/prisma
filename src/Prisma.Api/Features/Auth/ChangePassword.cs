using FluentValidation;
using Microsoft.AspNetCore.Identity;
using Prisma.Api.Infrastructure;
using Prisma.Api.Infrastructure.Auth;
using Prisma.Api.Infrastructure.Email;
using Prisma.Api.Infrastructure.Http;
using Prisma.Api.Infrastructure.Logging;
using Prisma.Domain;

namespace Prisma.Api.Features.Auth;

// "Trocar senha" com a sessão aberta (docs/email.md, 5.3; etapa H.4d). Pede a senha atual, e a errada conta para o
// bloqueio, como no login: senão uma sessão esquecida aberta viraria um jeito de adivinhar a senha sem limite.
//
// Errar a senha responde 400 (com o código WrongPassword), e não 401: na tela, o 401 leva ao login e derrubaria a sessão
// de quem só errou a senha. Trocar muda o SecurityStamp: esta sessão ganha um cookie novo e continua, as dos outros
// aparelhos caem na próxima conferência do cookie (AuthSetup.SessionCheckInterval), e vai o aviso de senha trocada.
public static class ChangePassword
{
    public sealed record Request(string CurrentPassword, string NewPassword);

    public static readonly Error WrongPassword = new(ErrorType.Validation, "Senha atual incorreta.", "WrongPassword");

    public sealed class Validator : AbstractValidator<Request>
    {
        public Validator()
        {
            RuleFor(x => x.CurrentPassword).NotEmpty().WithMessage("Informe a senha atual.");
            RuleFor(x => x.NewPassword)
                .NotEmpty().WithMessage("Informe a senha nova.")
                .MinimumLength(8).WithMessage("A senha deve ter pelo menos 8 caracteres.");
        }
    }

    public sealed class Handler(
        AppDbContext db,
        UserManager<AppUser> users,
        SignInManager<AppUser> signIn,
        ICurrentUser currentUser,
        EmailQueue emails,
        ILogger<Handler> logger)
    {
        private Error LockedOut(Guid userId)
        {
            logger.PasswordChangeRejected(userId, "LockedOut");
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

            if (!await users.CheckPasswordAsync(user, req.CurrentPassword))
            {
                await users.AccessFailedAsync(user);
                if (await users.IsLockedOutAsync(user))
                    return LockedOut(user.Id);

                logger.PasswordChangeRejected(user.Id, "WrongPassword");
                return WrongPassword;
            }

            await users.ResetAccessFailedCountAsync(user);
            var change = await users.ChangePasswordAsync(user, req.CurrentPassword, req.NewPassword);
            if (!change.Succeeded)
            {
                logger.PasswordChangeRejected(user.Id, string.Join(",", change.Errors.Select(e => e.Code).Distinct()));
                return new Error(ErrorType.Validation, string.Join(" ", change.Errors.Select(e => e.Description).Distinct()));
            }

            await emails.EnqueueAsync(EmailKind.PasswordChanged, user.Id, user.Email!, ct);
            await db.SaveChangesAsync(ct);

            // O cookie desta tela leva o selo antigo: sem o novo, ela cairia na próxima conferência como as outras.
            await signIn.RefreshSignInAsync(user);
            logger.PasswordChanged(user.Id);
            return true;
        }
    }

    public static void Map(IEndpointRouteBuilder app) =>
        app.MapPost("/change-password", async (Request request, Handler handler, CancellationToken ct) =>
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
