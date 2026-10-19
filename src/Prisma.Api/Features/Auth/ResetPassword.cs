using Microsoft.Extensions.Options;
using FluentValidation;
using Microsoft.AspNetCore.Identity;
using Prisma.Api.Infrastructure;
using Prisma.Api.Infrastructure.Auth;
using Prisma.Api.Infrastructure.Email;
using Prisma.Api.Infrastructure.Http;
using Prisma.Api.Infrastructure.Logging;
using Prisma.Domain;

namespace Prisma.Api.Features.Auth;

// O link de senha nova (docs/email.md, 5.2). A tela /nova-senha chama este POST com o link e a senha escolhida.
//
// Trocar a senha muda o SecurityStamp: o link deixa de valer (vale uma vez), os outros links de senha e de confirmação
// também, e as sessões abertas em outros aparelhos caem na próxima conferência do cookie (AuthSetup.SessionCheckInterval).
// O bloqueio por tentativas acaba (quem tem o e-mail provou ser a dona), o e-mail fica confirmado pelo mesmo motivo, e
// é isso que desfaz um pré-sequestro: a senha do intruso deixa de valer. Vai um aviso por e-mail, e a pessoa entra.
public static class ResetPassword
{
    public sealed record Request(Guid UserId, string Token, string Password);

    public static readonly Error InvalidLink =
        new(ErrorType.Validation, "Este link não vale mais. Peça outro abaixo.", "InvalidLink");

    public sealed class Validator : AbstractValidator<Request>
    {
        public Validator()
        {
            RuleFor(x => x.UserId).NotEmpty().WithMessage("Link incompleto.");
            RuleFor(x => x.Token).NotEmpty().WithMessage("Link incompleto.").MaximumLength(2000).WithMessage("Link inválido.");
            RuleFor(x => x.Password)
                .NotEmpty().WithMessage("Informe a senha.")
                .MinimumLength(8).WithMessage("A senha deve ter pelo menos 8 caracteres.");
        }
    }

    public sealed class Handler(
        AppDbContext db,
        UserManager<AppUser> users,
        SignInManager<AppUser> signIn,
        EmailQueue emails,
        IOptions<AdminOptions> admins,
        ILogger<Handler> logger)
    {
        public async Task<Result<SessionUser>> Execute(Request req, CancellationToken ct)
        {
            var user = await users.FindByIdAsync(req.UserId.ToString());
            var token = AuthEmails.DecodeToken(req.Token);
            if (user is null || token is null)
            {
                logger.PasswordResetRejected(user is null ? "UnknownUser" : "MalformedToken");
                return InvalidLink;
            }

            var reset = await users.ResetPasswordAsync(user, token, req.Password);
            if (!reset.Succeeded)
            {
                if (reset.Errors.Any(e => e.Code == nameof(IdentityErrorDescriber.InvalidToken)))
                {
                    logger.PasswordResetRejected("InvalidToken");
                    return InvalidLink;
                }

                logger.PasswordResetRejected(string.Join(",", reset.Errors.Select(e => e.Code).Distinct()));
                return new Error(ErrorType.Validation, string.Join(" ", reset.Errors.Select(e => e.Description).Distinct()));
            }

            await users.SetLockoutEndDateAsync(user, null);
            await users.ResetAccessFailedCountAsync(user);
            if (!user.EmailConfirmed)
            {
                user.EmailConfirmed = true;
                await users.UpdateAsync(user);
            }

            await emails.EnqueueAsync(EmailKind.PasswordChanged, user.Id, user.Email!, ct);
            await db.SaveChangesAsync(ct);

            await signIn.SignInAsync(user, isPersistent: true);
            logger.PasswordReset(user.Id);
            return SessionUser.Of(user, admins.Value);
        }
    }

    public static void Map(IEndpointRouteBuilder app) =>
        app.MapPost("/reset-password", async (Request request, Handler handler, CancellationToken ct) =>
            {
                var result = await handler.Execute(request, ct);
                return result.IsSuccess ? Results.Ok(result.Value) : result.Error.ToProblem();
            })
            .AllowAnonymous()
            .RequireRateLimiting(AuthSetup.RateLimitPolicy)
            .AddEndpointFilter<ValidationFilter<Request>>()
            .Produces<SessionUser>(200)
            .ProducesValidationProblem()
            .ProducesProblem(429);
}
