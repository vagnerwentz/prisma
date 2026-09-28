using Prisma.Api.Infrastructure;
using Prisma.Api.Infrastructure.Http;
using Prisma.Domain;
using Prisma.Domain.Statements;
using Prisma.Domain.Transactions;

namespace Prisma.Api.Features.Statements;

public static class PreviewStatementDates
{
    // Prévia do ajuste de datas (docs/fase-2.md, 2.10): as compras que mudariam de fatura com estas datas,
    // pela mesma peça do domínio que o PATCH usa, sobre dados lidos sem rastreamento. Nada é gravado.
    public sealed record Request(DateOnly ClosingDate, DateOnly DueDate);

    // Uma linha por compra: a parcelada aparece uma vez, com o total e a fatura da primeira parcela.
    public sealed record MovedPurchase(
        Guid TransactionId,
        TransactionType Type,
        string Description,
        DateOnly PurchaseDate,
        long AmountCents,
        int InstallmentCount,
        string FromReference,
        string ToReference);

    public sealed record Response(IReadOnlyList<MovedPurchase> MovedPurchases);

    public sealed class Handler(AppDbContext db)
    {
        public async Task<Result<Response>> Execute(Guid id, Request req, CancellationToken ct)
        {
            var loaded = await CardLedger.Load(db, id, tracking: false, ct);
            if (!loaded.IsSuccess)
                return loaded.Error;

            var ledger = loaded.Value;
            var before = ledger.Transactions.ToDictionary(t => t.Id, t => t.StatementId);
            var edited = StatementEditing.EditDates(
                ledger.Card, ledger.Statement, ledger.Statements, ledger.Transactions, req.ClosingDate, req.DueDate);
            if (!edited.IsSuccess)
                return edited.Error;

            var references = ledger.Statements.Concat(edited.Value.Opened).ToDictionary(s => s.Id, s => s.Reference);
            var byPurchase = ledger.Transactions.ToLookup(t => t.InstallmentPurchaseId ?? t.Id);
            var moved = edited.Value.Moved
                .GroupBy(t => t.InstallmentPurchaseId ?? t.Id)
                .Select(group =>
                {
                    var purchase = byPurchase[group.Key].OrderBy(t => t.InstallmentNumber ?? 1).ToList();
                    var first = purchase[0];
                    return new MovedPurchase(
                        first.Id, first.Type, first.Description, first.PurchaseDate, purchase.Sum(t => t.AmountCents),
                        purchase.Count, references[before[first.Id]!.Value], references[first.StatementId!.Value]);
                })
                .OrderBy(p => p.PurchaseDate)
                .ToList();

            return new Response(moved);
        }
    }

    public static void Map(IEndpointRouteBuilder app) =>
        app.MapPost("/statements/{id:guid}/date-preview", async (Guid id, Request request, Handler handler, CancellationToken ct) =>
            {
                var result = await handler.Execute(id, request, ct);
                return result.IsSuccess ? Results.Ok(result.Value) : result.Error.ToProblem();
            })
            .Produces<Response>(200)
            .ProducesProblem(400)
            .ProducesProblem(404);
}
