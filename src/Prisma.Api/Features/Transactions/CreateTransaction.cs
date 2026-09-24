using FluentValidation;
using Prisma.Api.Infrastructure;
using Prisma.Api.Infrastructure.Auth;
using Prisma.Api.Infrastructure.Http;
using Prisma.Domain;
using Prisma.Domain.Transactions;

namespace Prisma.Api.Features.Transactions;

public static class CreateTransaction
{
    public sealed record Request(
        Guid AccountId,
        TransactionType Type,
        long AmountCents,
        DateOnly PurchaseDate,
        Guid? CategoryId,
        PaymentMethod Method,
        string? Description);

    public sealed class Validator : AbstractValidator<Request>
    {
        public Validator()
        {
            RuleFor(x => x.Type).IsInEnum().WithMessage("Tipo de transação inválido.");
            RuleFor(x => x.Method).IsInEnum().WithMessage("Meio de pagamento inválido.");
        }
    }

    public sealed class Handler(AppDbContext db, ICurrentUser currentUser)
    {
        public async Task<Result<TransactionResponse>> Execute(Request req, CancellationToken ct)
        {
            var references = await TransactionReferences.Load(db, req.AccountId, req.CategoryId, ct);
            if (!references.IsSuccess)
                return references.Error;

            var (account, category) = references.Value;
            var created = Transaction.CreateSimple(
                currentUser.UserId, account, req.Type, req.AmountCents, req.PurchaseDate,
                category, req.Method, req.Description);
            if (!created.IsSuccess)
                return created.Error;

            db.Transactions.Add(created.Value);
            await db.SaveChangesAsync(ct);
            return TransactionResponse.From(created.Value);
        }
    }

    public static void Map(IEndpointRouteBuilder app) =>
        app.MapPost("/", async (Request request, Handler handler, CancellationToken ct) =>
            {
                var result = await handler.Execute(request, ct);
                return result.IsSuccess
                    ? Results.Created($"/transactions/{result.Value.Id}", result.Value)
                    : result.Error.ToProblem();
            })
            .AddEndpointFilter<ValidationFilter<Request>>();
}
