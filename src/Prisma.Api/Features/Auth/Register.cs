using FluentValidation;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using Prisma.Api.Infrastructure;
using Prisma.Api.Infrastructure.Auth;
using Prisma.Api.Infrastructure.Email;
using Prisma.Api.Infrastructure.Http;
using Prisma.Api.Infrastructure.Logging;
using Prisma.Api.Infrastructure.Privacy;
using Prisma.Domain;
using Prisma.Domain.Categories;

namespace Prisma.Api.Features.Auth;

// Cadastro com confirmação de e-mail (docs/email.md, 5.1). Não entra: a conta nasce sem o e-mail confirmado, e o link
// da confirmação é que dá a primeira sessão. A resposta é a mesma para e-mail novo e e-mail que já tem conta, para não
// contar a ninguém quem é cliente; quem já tem conta recebe um aviso (ou, sem ter confirmado, a confirmação de novo).
public static class Register
{
    // AcceptedTerms: a caixa "Li e aceito os termos, li a política e tenho 18 anos ou mais" (etapa A.4), sem ela não há
    // conta. Quem tem a tela de cadastro aberta de antes da A.4 não manda o campo e recebe o pedido para marcar.
    public sealed record Request(string Email, string Password, bool AcceptedTerms = false);

    public const string AcceptTermsMessage =
        "Para criar a conta, aceite os termos de uso e confirme que leu a política de privacidade e tem 18 anos ou mais. " +
        "Se a caixa não aparece, recarregue a página.";

    public sealed record Response(string Email);

    public sealed class Validator : AbstractValidator<Request>
    {
        public Validator()
        {
            RuleFor(x => x.Email)
                .NotEmpty().WithMessage("Informe o e-mail.")
                .EmailAddress().WithMessage("E-mail inválido.");
            RuleFor(x => x.Password)
                .NotEmpty().WithMessage("Informe a senha.")
                .MinimumLength(8).WithMessage("A senha deve ter pelo menos 8 caracteres.");
            RuleFor(x => x.AcceptedTerms).Equal(true).WithMessage(AcceptTermsMessage);
        }
    }

    public sealed class Handler(
        AppDbContext db,
        UserManager<AppUser> users,
        SignInManager<AppUser> signIn,
        EmailQueue emails,
        IHttpContextAccessor httpContextAccessor,
        IOptions<RegistrationOptions> registration,
        IClock clock,
        ILogger<Handler> logger)
    {
        public async Task<Result<Response>> Execute(Request req, CancellationToken ct)
        {
            var email = req.Email.Trim();
            if (!registration.Value.Allows(email))
            {
                logger.RegistrationRejected("NotAllowed");
                return new Error(ErrorType.Forbidden, "O cadastro está fechado por enquanto.");
            }

            if (await users.FindByEmailAsync(email) is { } existing)
                return await NotifyExisting(existing, email, ct);

            var user = new AppUser { UserName = email, Email = email, CategoryCatalogVersion = DefaultCategories.Version };
            LegalTerms.Accept(user, clock.UtcNow);

            // Usuário, categorias padrão e o e-mail de confirmação nascem juntos ou não nascem.
            await using var transaction = await db.Database.BeginTransactionAsync(ct);

            var created = await users.CreateAsync(user, req.Password);
            if (!created.Succeeded)
            {
                // Dois cadastros ao mesmo tempo com o mesmo e-mail: o segundo vira o caso de quem já tem conta.
                if (created.Errors.Any(e => e.Code is nameof(IdentityErrorDescriber.DuplicateEmail)
                        or nameof(IdentityErrorDescriber.DuplicateUserName)))
                {
                    await transaction.RollbackAsync(ct);
                    db.ChangeTracker.Clear();
                    return await NotifyExisting((await users.FindByEmailAsync(email))!, email, ct);
                }

                logger.RegistrationRejected(string.Join(",", created.Errors.Select(e => e.Code).Distinct()));
                return new Error(ErrorType.Validation, string.Join(" ", created.Errors.Select(e => e.Description).Distinct()));
            }

            // A partir daqui a requisição age como o novo usuário: o AppDbContext só aceita gravar
            // entidades do usuário autenticado.
            httpContextAccessor.HttpContext!.User = await signIn.CreateUserPrincipalAsync(user);

            db.Categories.AddRange(DefaultCategories.CreateFor(user.Id));
            await emails.EnqueueAsync(EmailKind.ConfirmEmail, user.Id, email, ct);
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);

            logger.UserRegistered(user.Id);
            return new Response(email);
        }

        private async Task<Result<Response>> NotifyExisting(AppUser existing, string typed, CancellationToken ct)
        {
            var kind = existing.EmailConfirmed ? EmailKind.AlreadyRegistered : EmailKind.ConfirmEmail;
            await emails.EnqueueAsync(kind, existing.Id, existing.Email!, ct);
            await db.SaveChangesAsync(ct);
            logger.RegistrationOfExistingAccount(existing.Id, existing.EmailConfirmed);
            return new Response(typed);
        }
    }

    public static void Map(IEndpointRouteBuilder app) =>
        app.MapPost("/register", async (Request request, Handler handler, CancellationToken ct) =>
            {
                var result = await handler.Execute(request, ct);
                return result.IsSuccess ? Results.Accepted(value: result.Value) : result.Error.ToProblem();
            })
            .AllowAnonymous()
            .RequireRateLimiting(AuthSetup.RateLimitPolicy)
            .AddEndpointFilter<ValidationFilter<Request>>()
            .Produces<Response>(202)
            .ProducesValidationProblem()
            .ProducesProblem(403)
            .ProducesProblem(429);
}
