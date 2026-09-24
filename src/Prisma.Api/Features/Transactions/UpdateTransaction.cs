using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Prisma.Api.Infrastructure;
using Prisma.Api.Infrastructure.Http;
using Prisma.Domain;
using Prisma.Domain.Transactions;

namespace Prisma.Api.Features.Transactions;

public static class UpdateTransaction
{
    // Todos os campos editáveis juntos, como nos demais PATCH.
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

    public sealed class Handler(AppDbContext db)
    {
        public async Task<Result<TransactionResponse>> Execute(Guid id, Request req, CancellationToken ct)
        {
            var transaction = await db.Transactions.SingleOrDefaultAsync(t => t.Id == id, ct);
            if (transaction is null)
                return new Error(ErrorType.NotFound, "Transação não encontrada.");

            var references = await TransactionReferences.Load(db, req.AccountId, req.CategoryId, ct);
            if (!references.IsSuccess)
                return references.Error;

            var (account, category) = references.Value;
            var updated = transaction.UpdateSimple(
                account, req.Type, req.AmountCents, req.PurchaseDate, category, req.Method, req.Description);
            if (!updated.IsSuccess)
                return updated.Error;

            await db.SaveChangesAsync(ct);
            return TransactionResponse.From(transaction);
        }
    }

    public static void Map(IEndpointRouteBuilder app) =>
        app.MapPatch("/{id:guid}", async (Guid id, Request request, Handler handler, CancellationToken ct) =>
            {
                var result = await handler.Execute(id, request, ct);
                return result.IsSuccess ? Results.Ok(result.Value) : result.Error.ToProblem();
            })
            .AddEndpointFilter<ValidationFilter<Request>>();
}
