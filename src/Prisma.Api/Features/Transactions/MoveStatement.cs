using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Prisma.Api.Features.Statements;
using Prisma.Api.Infrastructure;
using Prisma.Api.Infrastructure.Http;
using Prisma.Api.Infrastructure.Logging;
using Prisma.Domain;
using Prisma.Domain.Statements;
using Prisma.Domain.Transactions;

namespace Prisma.Api.Features.Transactions;

public static class MoveStatement
{
    // Muda a compra no cartão para a fatura seguinte ou anterior, sem mudar a data dela (docs/fase-2.md,
    // 2.9, regras 2, 3 e 6). A partir de qualquer parcela, a compra parcelada anda inteira.
    public sealed record Request(StatementShift Direction);

    public sealed class Validator : AbstractValidator<Request>
    {
        public Validator() =>
            RuleFor(x => x.Direction).IsInEnum().WithMessage("Direção inválida: use Next ou Previous.");
    }

    public sealed class Handler(AppDbContext db, ILogger<Handler> logger)
    {
        public Task<Result<IReadOnlyList<TransactionResponse>>> Execute(Guid id, Request req, CancellationToken ct) =>
            ConcurrentStatementOpening.Retry(db, () => Move(id, req, ct));

        private async Task<Result<IReadOnlyList<TransactionResponse>>> Move(Guid id, Request req, CancellationToken ct)
        {
            var transaction = await db.Transactions.SingleOrDefaultAsync(t => t.Id == id, ct);
            if (transaction is null)
                return new Error(ErrorType.NotFound, "Transação não encontrada.");

            var card = await db.Accounts.SingleOrDefaultAsync(a => a.Id == transaction.AccountId, ct);
            if (card is null)
                return new Error(ErrorType.Conflict, "A conta desta compra foi excluída.");

            var purchase = transaction.InstallmentPurchaseId is { } purchaseId
                ? await db.Transactions.Where(t => t.InstallmentPurchaseId == purchaseId).ToListAsync(ct)
                : [transaction];
            var statements = await db.Statements.Where(s => s.AccountId == card.Id).ToListAsync(ct);
            List<Statement> Holding() => statements.Where(s => purchase.Any(t => t.StatementId == s.Id)).ToList();
            var sources = Holding();

            var moved = CardPurchase.MoveStatement(purchase, card, statements, req.Direction);
            if (!moved.IsSuccess)
                return moved.Error;

            db.Statements.AddRange(moved.Value.Opened);
            StatementTouch.Touch(db, sources.Concat(Holding()));
            await db.SaveChangesAsync(ct);

            logger.PurchaseMovedToStatement(transaction.InstallmentPurchaseId ?? transaction.Id, req.Direction, moved.Value.Pinned);
            return moved.Value.Moved.OrderBy(t => t.InstallmentNumber).Select(TransactionResponse.From).ToList();
        }
    }

    public static void Map(IEndpointRouteBuilder app) =>
        app.MapPost("/{id:guid}/move-statement", async (Guid id, Request request, Handler handler, CancellationToken ct) =>
            {
                var result = await handler.Execute(id, request, ct);
                return result.IsSuccess ? Results.Ok(result.Value) : result.Error.ToProblem();
            })
            .AddEndpointFilter<ValidationFilter<Request>>()
            .Produces<IReadOnlyList<TransactionResponse>>(200)
            .ProducesValidationProblem()
            .ProducesProblem(404)
            .ProducesProblem(409);
}
