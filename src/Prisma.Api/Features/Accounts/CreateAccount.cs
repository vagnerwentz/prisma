using FluentValidation;
using Prisma.Api.Infrastructure;
using Prisma.Api.Infrastructure.Auth;
using Prisma.Api.Infrastructure.Http;
using Prisma.Domain;
using Prisma.Domain.Accounts;

namespace Prisma.Api.Features.Accounts;

public static class CreateAccount
{
    public sealed record Request(
        string Name,
        AccountType Type,
        long InitialBalanceCents,
        int? ClosingDay,
        int? DueDay,
        long? CreditLimitCents);

    public sealed class Validator : AbstractValidator<Request>
    {
        public Validator() =>
            RuleFor(x => x.Type).IsInEnum().WithMessage("Tipo de conta inválido.");
    }

    public sealed class Handler(AppDbContext db, ICurrentUser currentUser)
    {
        public async Task<Result<AccountResponse>> Execute(Request req, CancellationToken ct)
        {
            var created = Account.Create(
                currentUser.UserId, req.Name, req.Type, req.InitialBalanceCents,
                req.ClosingDay, req.DueDay, req.CreditLimitCents);
            if (!created.IsSuccess)
                return created.Error;

            db.Accounts.Add(created.Value);
            await db.SaveChangesAsync(ct);
            return AccountResponse.From(created.Value);
        }
    }

    public static void Map(IEndpointRouteBuilder app) =>
        app.MapPost("/", async (Request request, Handler handler, CancellationToken ct) =>
            {
                var result = await handler.Execute(request, ct);
                return result.IsSuccess
                    ? Results.Created((string?)null, result.Value)
                    : result.Error.ToProblem();
            })
            .AddEndpointFilter<ValidationFilter<Request>>();
}
