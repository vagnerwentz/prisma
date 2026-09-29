using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Prisma.Api.Infrastructure;
using Prisma.Api.Infrastructure.Http;
using Prisma.Api.Infrastructure.Logging;
using Prisma.Domain;

namespace Prisma.Api.Features.Accounts;

public static class DeleteAccount
{
    public sealed class Handler(AppDbContext db, ILogger<Handler> logger)
    {
        // Remove vira soft delete no AppDbContext.
        public async Task<Result<Guid>> Execute(Guid id, CancellationToken ct)
        {
            var account = await db.Accounts.SingleOrDefaultAsync(a => a.Id == id, ct);
            if (account is null)
                return new Error(ErrorType.NotFound, "Conta não encontrada.");

            var activeTransactions = await db.Transactions.CountAsync(t => t.AccountId == id, ct);
            if (account.CheckCanDelete(activeTransactions) is { } error)
                return error;

            // Conta vazia não tem onde lançar: as séries dela encerram junto (a série que já terminou fica como
            // está) e as pendências dela saem, na mesma gravação (docs/fase-2.md, 2.14, decisão de 2026-09-29).
            // Sem isso, ficariam "ativas" sem conta, sem gerar nada. Encerrar, e não apagar: o que já foi
            // lançado é histórico.
            var series = await db.Recurrences.Where(r => r.AccountId == id).ToListAsync(ct);
            var ending = series.Where(r => r.NextOccurrence is not null).ToList();
            foreach (var recurrence in ending)
                recurrence.End();
            db.RecurrencePendings.RemoveRange(await db.RecurrencePendings.Where(p => p.AccountId == id).ToListAsync(ct));

            // Com as séries carregadas, o Remove tentaria a cascata na hora e a recusaria (a relação é Restrict).
            // Adiada para o SaveChanges, ela não acontece: antes, o AppDbContext transforma a exclusão em soft delete.
            db.ChangeTracker.CascadeDeleteTiming = CascadeTiming.OnSaveChanges;
            db.Accounts.Remove(account);
            await db.SaveChangesAsync(ct);
            foreach (var recurrence in ending)
                logger.RecurrenceEnded(recurrence.Id);
            return account.Id;
        }
    }

    public static void Map(IEndpointRouteBuilder app) =>
        app.MapDelete("/{id:guid}", async (Guid id, Handler handler, CancellationToken ct) =>
        {
            var result = await handler.Execute(id, ct);
            return result.IsSuccess ? Results.NoContent() : result.Error.ToProblem();
        })
            .Produces(204)
            .ProducesProblem(404)
            .ProducesProblem(409);
}
