using Microsoft.EntityFrameworkCore;
using Prisma.Api.Features.Recurrences;
using Prisma.Api.Infrastructure;
using Prisma.Api.Infrastructure.Http;
using Prisma.Domain;
using Prisma.Domain.Recurrences;
using Prisma.Domain.Transactions;

namespace Prisma.Api.Features.Statements;

public static class ListStatements
{
    // TotalCents: compras ativas menos estornos da fatura (o filtro global ignora os excluídos); a
    // entrada do pagamento, Transfer, não conta (docs/fase-1.md, 2.3). Negativo é saldo a favor.
    public sealed record StatementResponse(
        Guid Id,
        string Reference,
        DateOnly ClosingDate,
        DateOnly DueDate,
        bool IsPaid,
        bool DatesEditedManually,
        long TotalCents,
        // As cobranças das séries que ainda vão cair nesta fatura (docs/fase-2.md, 2.14, regra 11). Não entra
        // no total. Zero na fatura paga.
        long ProjectedCents);

    public sealed class Handler(AppDbContext db)
    {
        public async Task<Result<IReadOnlyList<StatementResponse>>> Execute(Guid accountId, CancellationToken ct)
        {
            if (!await db.Accounts.AnyAsync(a => a.Id == accountId, ct))
                return new Error(ErrorType.NotFound, "Conta não encontrada.");

            var statements = await db.Statements
                .AsNoTracking()
                .Where(s => s.AccountId == accountId)
                .OrderByDescending(s => s.DueDate)
                .Select(s => new StatementResponse(
                    s.Id, s.Reference, s.ClosingDate, s.DueDate, s.IsPaid, s.DatesEditedManually,
                    db.Transactions.Where(t => t.StatementId == s.Id && t.Type != TransactionType.Transfer)
                        .Sum(t => t.Type == TransactionType.Refund ? -t.AmountCents : t.AmountCents),
                    0))
                .ToListAsync(ct);

            // Até o fechamento da última fatura em aberto: o que cairia depois dela ainda não tem fatura.
            var lastOpen = statements.Where(s => !s.IsPaid).Select(s => (DateOnly?)s.ClosingDate).Max();
            if (lastOpen is not { } until)
                return statements;

            var projected = await RecurrenceForecast.Load(db, until, ct, accountId);
            return statements
                .Select(s => s.IsPaid ? s : s with { ProjectedCents = RecurrenceProjection.ForStatement(projected, accountId, s.Reference) })
                .ToList();
        }
    }

    public static void Map(IEndpointRouteBuilder app) =>
        app.MapGet("/accounts/{accountId:guid}/statements", async (Guid accountId, Handler handler, CancellationToken ct) =>
        {
            var result = await handler.Execute(accountId, ct);
            return result.IsSuccess ? Results.Ok(result.Value) : result.Error.ToProblem();
        })
            .Produces<IReadOnlyList<StatementResponse>>(200)
            .ProducesProblem(404);
}
