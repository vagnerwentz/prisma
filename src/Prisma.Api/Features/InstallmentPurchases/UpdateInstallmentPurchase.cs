using Microsoft.EntityFrameworkCore;
using Prisma.Api.Features.Transactions;
using Prisma.Api.Infrastructure;
using Prisma.Api.Infrastructure.Http;
using Prisma.Domain;
using Prisma.Domain.Categories;
using Prisma.Domain.Transactions;

namespace Prisma.Api.Features.InstallmentPurchases;

public static class UpdateInstallmentPurchase
{
    // Todos os campos editáveis juntos, como nos demais PATCH. A data da compra não muda.
    public sealed record Request(long TotalAmountCents, int InstallmentCount, Guid? CategoryId, string? Description);

    public sealed record Response(
        Guid Id,
        Guid AccountId,
        string Description,
        long TotalAmountCents,
        int InstallmentCount,
        DateOnly PurchaseDate,
        IReadOnlyList<TransactionResponse> Installments);

    public sealed class Handler(AppDbContext db)
    {
        public async Task<Result<Response>> Execute(Guid id, Request req, CancellationToken ct)
        {
            var purchase = await db.InstallmentPurchases.SingleOrDefaultAsync(p => p.Id == id, ct);
            if (purchase is null)
                return new Error(ErrorType.NotFound, "Compra parcelada não encontrada.");

            var card = await db.Accounts.SingleOrDefaultAsync(a => a.Id == purchase.AccountId, ct);
            if (card is null)
                return new Error(ErrorType.Conflict, "A conta desta compra foi excluída.");

            Category? category = null;
            if (req.CategoryId is { } categoryId)
            {
                category = await db.Categories.SingleOrDefaultAsync(c => c.Id == categoryId, ct);
                if (category is null)
                    return new Error(ErrorType.Validation, "Categoria não encontrada.");
            }

            var installments = await db.Transactions.Where(t => t.InstallmentPurchaseId == id).ToListAsync(ct);

            // As faturas das parcelas (para saber quais estão pagas) e as que podem receber
            // parcelas novas: o cálculo parte de um ciclo antes do mês da compra.
            var statementIds = installments.Select(t => t.StatementId).ToList();
            var from = purchase.PurchaseDate.AddMonths(-2);
            var statements = await db.Statements
                .Where(s => s.AccountId == card.Id && (s.ClosingDate >= from || statementIds.Contains(s.Id)))
                .ToListAsync(ct);

            var edited = CardPurchase.Edit(
                purchase, card, installments, statements, req.TotalAmountCents, req.InstallmentCount,
                category, req.Description);
            if (!edited.IsSuccess)
                return edited.Error;

            db.Statements.AddRange(edited.Value.OpenedStatements);
            db.Transactions.AddRange(edited.Value.Added);
            db.Transactions.RemoveRange(edited.Value.Removed);
            await db.SaveChangesAsync(ct);

            var current = installments.Except(edited.Value.Removed).Concat(edited.Value.Added)
                .OrderBy(t => t.InstallmentNumber)
                .Select(TransactionResponse.From)
                .ToList();

            return new Response(
                purchase.Id, purchase.AccountId, purchase.Description, purchase.TotalAmountCents,
                purchase.InstallmentCount, purchase.PurchaseDate, current);
        }
    }

    public static void Map(IEndpointRouteBuilder app) =>
        app.MapPatch("/installment-purchases/{id:guid}", async (Guid id, Request request, Handler handler, CancellationToken ct) =>
            {
                var result = await handler.Execute(id, request, ct);
                return result.IsSuccess ? Results.Ok(result.Value) : result.Error.ToProblem();
            })
            .Produces<Response>(200)
            .ProducesProblem(400)
            .ProducesProblem(404)
            .ProducesProblem(409);
}
