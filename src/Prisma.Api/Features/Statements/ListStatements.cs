using Microsoft.EntityFrameworkCore;
using Prisma.Api.Infrastructure;
using Prisma.Api.Infrastructure.Http;
using Prisma.Domain;

namespace Prisma.Api.Features.Statements;

public static class ListStatements
{
    // TotalCents soma as transações ativas da fatura (o filtro global ignora as excluídas).
    public sealed record StatementResponse(
        Guid Id,
        string Reference,
        DateOnly ClosingDate,
        DateOnly DueDate,
        bool IsPaid,
        bool DatesEditedManually,
        long TotalCents);

    public sealed class Handler(AppDbContext db)
    {
        public async Task<Result<IReadOnlyList<StatementResponse>>> Execute(Guid accountId, CancellationToken ct)
        {
            if (!await db.Accounts.AnyAsync(a => a.Id == accountId, ct))
                return new Error(ErrorType.NotFound, "Conta não encontrada.");

            return await db.Statements
                .AsNoTracking()
                .Where(s => s.AccountId == accountId)
                .OrderByDescending(s => s.DueDate)
                .Select(s => new StatementResponse(
                    s.Id, s.Reference, s.ClosingDate, s.DueDate, s.IsPaid, s.DatesEditedManually,
                    db.Transactions.Where(t => t.StatementId == s.Id).Sum(t => t.AmountCents)))
                .ToListAsync(ct);
        }
    }

    public static void Map(IEndpointRouteBuilder app) =>
        app.MapGet("/accounts/{accountId:guid}/statements", async (Guid accountId, Handler handler, CancellationToken ct) =>
        {
            var result = await handler.Execute(accountId, ct);
            return result.IsSuccess ? Results.Ok(result.Value) : result.Error.ToProblem();
        });
}
