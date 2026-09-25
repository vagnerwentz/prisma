using FluentValidation;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using Prisma.Api.Infrastructure;
using Prisma.Api.Infrastructure.Auth;
using Prisma.Api.Infrastructure.Http;
using Prisma.Domain;
using Prisma.Domain.Categories;

namespace Prisma.Api.Features.Auth;

public static class Register
{
    public sealed record Request(string Email, string Password);

    public sealed record Response(Guid Id, string Email);

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
        }
    }

    public sealed class Handler(
        AppDbContext db,
        UserManager<AppUser> users,
        SignInManager<AppUser> signIn,
        IHttpContextAccessor httpContextAccessor,
        IOptions<RegistrationOptions> registration)
    {
        public async Task<Result<Response>> Execute(Request req, CancellationToken ct)
        {
            var email = req.Email.Trim();
            if (!registration.Value.Allows(email))
                return new Error(ErrorType.Forbidden, "O cadastro está fechado por enquanto.");

            var user = new AppUser { UserName = email, Email = email };

            // Usuário e categorias padrão nascem juntos ou não nascem.
            await using var transaction = await db.Database.BeginTransactionAsync(ct);

            var created = await users.CreateAsync(user, req.Password);
            if (!created.Succeeded)
            {
                var isDuplicate = created.Errors.Any(e =>
                    e.Code is nameof(IdentityErrorDescriber.DuplicateEmail)
                        or nameof(IdentityErrorDescriber.DuplicateUserName));
                var message = string.Join(" ", created.Errors.Select(e => e.Description).Distinct());
                return new Error(isDuplicate ? ErrorType.Conflict : ErrorType.Validation, message);
            }

            // A partir daqui a requisição age como o novo usuário: o AppDbContext só aceita gravar
            // entidades do usuário autenticado.
            httpContextAccessor.HttpContext!.User = await signIn.CreateUserPrincipalAsync(user);

            db.Categories.AddRange(DefaultCategories.CreateFor(user.Id));
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);

            // Cadastro já entra logado: sem confirmação de e-mail por enquanto (ver PLAN.md).
            await signIn.SignInAsync(user, isPersistent: true);
            return new Response(user.Id, email);
        }
    }

    public static void Map(IEndpointRouteBuilder app) =>
        app.MapPost("/register", async (Request request, Handler handler, CancellationToken ct) =>
            {
                var result = await handler.Execute(request, ct);
                return result.IsSuccess
                    ? Results.Created("/auth/me", result.Value)
                    : result.Error.ToProblem();
            })
            .AllowAnonymous()
            .RequireRateLimiting(AuthSetup.RateLimitPolicy)
            .AddEndpointFilter<ValidationFilter<Request>>()
            .Produces<Response>(201)
            .ProducesValidationProblem()
            .ProducesProblem(403)
            .ProducesProblem(409)
            .ProducesProblem(429);
}
