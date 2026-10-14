using FluentValidation;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using Prisma.Api.Infrastructure.Auth;
using Prisma.Api.Infrastructure.Http;
using Prisma.Api.Infrastructure.Logging;
using Prisma.Domain;

namespace Prisma.Api.Features.Auth;

// O link do e-mail de confirmação, com a senha do cadastro (docs/email.md, 5.1). A tela /confirmar-email chama este
// POST: um GET seria aberto pelos antivírus de e-mail antes da pessoa e gastaria o link.
//
// Por que a senha: o link prova que a pessoa tem o e-mail, não que foi ela quem escolheu a senha. Sem ela, alguém
// poderia se cadastrar antes com o e-mail de outra pessoa e uma senha sua, esperar a dona do e-mail confirmar e entrar
// na conta dela depois ("pré-sequestro"). Com a senha, só confirma quem tem o e-mail e sabe a senha; a dona do e-mail
// que não sabe cria uma senha nova pelo e-mail (H.4c), e a do intruso deixa de valer.
//
// A ordem importa. O link vem antes: link inválido não testa senha nem conta tentativa. A senha errada conta para o
// bloqueio, como no login. Deu certo: o e-mail fica confirmado, o SecurityStamp muda (este e qualquer outro link de
// confirmação já enviado deixam de valer) e a pessoa entra.
public static class ConfirmEmail
{
    public sealed record Request(Guid UserId, string Token, string Password);

    public sealed record Response(Guid Id, string Email);

    public static readonly Error InvalidLink =
        new(ErrorType.Validation, "Este link não vale mais. Peça outro abaixo ou entre, se já confirmou.", "InvalidLink");

    public static readonly Error WrongPassword =
        new(ErrorType.Unauthorized, "A senha não confere com a do cadastro.", "WrongPassword");

    public sealed class Validator : AbstractValidator<Request>
    {
        public Validator()
        {
            RuleFor(x => x.UserId).NotEmpty().WithMessage("Link incompleto.");
            RuleFor(x => x.Token).NotEmpty().WithMessage("Link incompleto.").MaximumLength(2000).WithMessage("Link inválido.");
            RuleFor(x => x.Password).NotEmpty().WithMessage("Informe a senha.");
        }
    }

    public sealed class Handler(
        UserManager<AppUser> users, SignInManager<AppUser> signIn, IOptions<IdentityOptions> identity, ILogger<Handler> logger)
    {
        public async Task<Result<Response>> Execute(Request req, CancellationToken ct)
        {
            var user = await users.FindByIdAsync(req.UserId.ToString());
            var token = AuthEmails.DecodeToken(req.Token);
            if (user is null || token is null)
            {
                logger.EmailConfirmationRejected(user is null ? "UnknownUser" : "MalformedToken");
                return InvalidLink;
            }

            // Confere o link sem gastá-lo: a senha ainda pode estar errada.
            var linkIsValid = await users.VerifyUserTokenAsync(user,
                identity.Value.Tokens.EmailConfirmationTokenProvider, UserManager<AppUser>.ConfirmEmailTokenPurpose, token);
            if (!linkIsValid)
            {
                logger.EmailConfirmationRejected("InvalidToken");
                return InvalidLink;
            }

            if (await users.IsLockedOutAsync(user))
                return LockedOut(user);
            if (!await users.CheckPasswordAsync(user, req.Password))
            {
                await users.AccessFailedAsync(user);
                if (await users.IsLockedOutAsync(user))
                    return LockedOut(user);
                logger.EmailConfirmationRejected("WrongPassword");
                return WrongPassword;
            }

            await users.ResetAccessFailedCountAsync(user);
            var confirmed = await users.ConfirmEmailAsync(user, token);
            if (!confirmed.Succeeded)
            {
                logger.EmailConfirmationRejected("InvalidToken");
                return InvalidLink;
            }

            await users.UpdateSecurityStampAsync(user);
            await signIn.SignInAsync(user, isPersistent: true);
            logger.EmailConfirmed(user.Id);
            return new Response(user.Id, user.Email!);
        }

        private Error LockedOut(AppUser user)
        {
            logger.LoginLockedOut(user.Id);
            return new Error(ErrorType.TooManyAttempts,
                "Conta bloqueada temporariamente por excesso de tentativas. Tente novamente em alguns minutos.");
        }
    }

    public static void Map(IEndpointRouteBuilder app) =>
        app.MapPost("/confirm-email", async (Request request, Handler handler, CancellationToken ct) =>
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
            .ProducesProblem(429);
}
