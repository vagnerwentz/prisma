using Prisma.Api.Infrastructure;
using Prisma.Api.Infrastructure.Http;
using Prisma.Domain;
using Prisma.Domain.Statements;

namespace Prisma.Api.Features.Statements;

public static class UpdateStatement
{
    public sealed record Request(DateOnly ClosingDate, DateOnly DueDate);

    // A fatura editada, como na lista, e quantas compras mudaram de fatura (a parcelada conta uma).
    public sealed record Response(
        Guid Id,
        string Reference,
        DateOnly ClosingDate,
        DateOnly DueDate,
        bool IsPaid,
        bool DatesEditedManually,
        long TotalCents,
        int MovedPurchases);

    public sealed class Handler(AppDbContext db)
    {
        // Editar as datas move as compras cujo ciclo mudou e não atravessa as faturas vizinhas
        // (docs/fase-2.md, 2.9, regras 4 a 6). O recálculo precisa do cartão inteiro (CardLedger).
        public Task<Result<Response>> Execute(Guid id, Request req, CancellationToken ct) =>
            ConcurrentStatementOpening.Retry(db, () => Update(id, req, ct));

        private async Task<Result<Response>> Update(Guid id, Request req, CancellationToken ct)
        {
            var loaded = await CardLedger.Load(db, id, tracking: true, ct);
            if (!loaded.IsSuccess)
                return loaded.Error;

            var (statement, card, statements, transactions) = loaded.Value;
            var before = transactions.ToDictionary(t => t.Id, t => t.StatementId);

            var edited = StatementEditing.EditDates(card, statement, statements, transactions, req.ClosingDate, req.DueDate);
            if (!edited.IsSuccess)
                return edited.Error;

            db.Statements.AddRange(edited.Value.Opened);
            var touched = edited.Value.Moved.SelectMany(t => new[] { before[t.Id], t.StatementId }).ToHashSet();
            StatementTouch.Touch(db, statements.Where(s => touched.Contains(s.Id)));
            await db.SaveChangesAsync(ct);

            var total = await StatementTotals.Of(db, statement.Id, ct);
            return new Response(
                statement.Id, statement.Reference, statement.ClosingDate, statement.DueDate, statement.IsPaid,
                statement.DatesEditedManually, total, edited.Value.MovedPurchases);
        }
    }

    public static void Map(IEndpointRouteBuilder app) =>
        app.MapPatch("/statements/{id:guid}", async (Guid id, Request request, Handler handler, CancellationToken ct) =>
            {
                var result = await handler.Execute(id, request, ct);
                return result.IsSuccess ? Results.Ok(result.Value) : result.Error.ToProblem();
            })
            .Produces<Response>(200)
            .ProducesProblem(400)
            .ProducesProblem(404)
            .ProducesProblem(409);
}
