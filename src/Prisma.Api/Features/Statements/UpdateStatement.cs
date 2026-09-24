using Microsoft.EntityFrameworkCore;
using Prisma.Api.Infrastructure;
using Prisma.Api.Infrastructure.Http;
using Prisma.Domain;
using Prisma.Domain.Statements;

namespace Prisma.Api.Features.Statements;

public static class UpdateStatement
{
    public sealed record Request(DateOnly ClosingDate, DateOnly DueDate);

    public sealed class Handler(AppDbContext db)
    {
        // Editar as datas recalcula o SettlementDate das transações da fatura (docs/fase-1.md).
        public async Task<Result<ListStatements.StatementResponse>> Execute(Guid id, Request req, CancellationToken ct)
        {
            var statement = await db.Statements.SingleOrDefaultAsync(s => s.Id == id, ct);
            if (statement is null)
                return new Error(ErrorType.NotFound, "Fatura não encontrada.");

            var transactions = await db.Transactions.Where(t => t.StatementId == id).ToListAsync(ct);

            var edited = StatementEditing.EditDates(statement, transactions, req.ClosingDate, req.DueDate);
            if (!edited.IsSuccess)
                return edited.Error;

            await db.SaveChangesAsync(ct);
            return new ListStatements.StatementResponse(
                statement.Id, statement.Reference, statement.ClosingDate, statement.DueDate, statement.IsPaid,
                statement.DatesEditedManually, transactions.Sum(t => t.AmountCents));
        }
    }

    public static void Map(IEndpointRouteBuilder app) =>
        app.MapPatch("/statements/{id:guid}", async (Guid id, Request request, Handler handler, CancellationToken ct) =>
            {
                var result = await handler.Execute(id, request, ct);
                return result.IsSuccess ? Results.Ok(result.Value) : result.Error.ToProblem();
            })
            .Produces<ListStatements.StatementResponse>(200)
            .ProducesProblem(400)
            .ProducesProblem(404);
}
