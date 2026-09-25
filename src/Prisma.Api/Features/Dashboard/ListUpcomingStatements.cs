using Microsoft.EntityFrameworkCore;
using Prisma.Api.Infrastructure;
using Prisma.Domain;
using Prisma.Domain.Accounts;
using Prisma.Domain.Dashboard;
using Prisma.Domain.Transactions;

namespace Prisma.Api.Features.Dashboard;

// Próximas faturas (docs/fase-2.md, 2.4): a próxima não paga de cada cartão ativo, olhando para
// hoje, não para o mês do Resumo. O banco traz as faturas que ainda vão vencer com o total de
// cada uma (o mesmo da tela do cartão); o domínio escolhe a próxima de cada cartão.
public static class ListUpcomingStatements
{
    public sealed record Item(Guid AccountId, string CardName, Guid StatementId, DateOnly DueDate, long TotalCents);

    public sealed record Response(long TotalCents, IReadOnlyList<Item> Statements);

    public sealed class Handler(AppDbContext db, IClock clock)
    {
        public async Task<Response> Execute(CancellationToken ct)
        {
            var today = clock.Today;

            var statements = await db.Statements
                .AsNoTracking()
                .Where(s => s.DueDate >= today)
                .Join(db.Accounts.Where(a => a.Type == AccountType.CreditCard && a.IsActive),
                    s => s.AccountId, a => a.Id, (s, a) => new
                    {
                        s.AccountId, CardName = a.Name, s.Id, s.DueDate, s.IsPaid,
                        TotalCents = db.Transactions.Where(t => t.StatementId == s.Id && t.Type != TransactionType.Transfer)
                            .Sum(t => t.Type == TransactionType.Refund ? -t.AmountCents : t.AmountCents),
                    })
                .ToListAsync(ct);

            var upcoming = UpcomingStatements.Of(today,
                statements.Select(s => new CardStatement(s.AccountId, s.CardName, s.Id, s.DueDate, s.IsPaid, s.TotalCents)));

            return new Response(
                upcoming.TotalCents,
                upcoming.Statements.Select(s => new Item(s.AccountId, s.CardName, s.StatementId, s.DueDate, s.TotalCents)).ToList());
        }
    }

    public static void Map(IEndpointRouteBuilder app) =>
        app.MapGet("/upcoming-statements", async (Handler handler, CancellationToken ct) =>
            Results.Ok(await handler.Execute(ct)))
            .Produces<Response>(200);
}
