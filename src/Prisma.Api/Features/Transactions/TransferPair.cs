using Microsoft.EntityFrameworkCore;
using Prisma.Api.Features.Statements;
using Prisma.Api.Infrastructure;
using Prisma.Domain;
using Prisma.Domain.Transactions;

namespace Prisma.Api.Features.Transactions;

// Excluir ou restaurar uma ponta de transferência trata as duas (docs/fase-1.md, 2.3), e a fatura
// paga por ela, se houver.
public static class TransferPair
{
    public static async Task<Result<Guid>> Remove(AppDbContext db, Transaction leg, CancellationToken ct)
    {
        var both = await db.Transactions.Where(t => t.TransferPairId == leg.TransferPairId).ToListAsync(ct);
        var legs = Legs(both);
        var statement = await PaidStatement(db, legs, ct);

        Transfer.Remove(legs, statement);
        db.Transactions.RemoveRange(both);
        await db.SaveChangesAsync(ct);
        return leg.Id;
    }

    public static async Task<Result<TransactionResponse>> Restore(AppDbContext db, Transaction leg, CancellationToken ct)
    {
        // Exceção da regra 6 do CLAUDE.md: ignora só o soft delete. As duas pontas saíram juntas.
        var both = await db.Transactions
            .IgnoreQueryFilters([AppDbContext.SoftDeleteFilter])
            .Where(t => t.TransferPairId == leg.TransferPairId && t.DeletedAt == leg.DeletedAt)
            .ToListAsync(ct);
        var legs = Legs(both);

        var accountIds = both.Select(t => t.AccountId).ToList();
        if (await db.Accounts.CountAsync(a => accountIds.Contains(a.Id), ct) != 2)
            return new Error(ErrorType.Conflict, "Uma das contas desta transferência foi excluída; não é possível restaurá-la.");

        var statement = await PaidStatement(db, legs, ct);
        var total = statement is null ? 0 : await StatementTotals.Of(db, statement.Id, ct);
        if (Transfer.Restore(legs, statement, total) is { } error)
            return error;

        await db.SaveChangesAsync(ct);
        return TransactionResponse.From(leg);
    }

    private static TransferLegs Legs(List<Transaction> both)
    {
        if (both.Count != 2)
            throw new InvalidOperationException("Transferência sem as duas pontas.");

        return new TransferLegs(
            both.Single(t => t.TransferDirection == TransferDirection.Out),
            both.Single(t => t.TransferDirection == TransferDirection.In));
    }

    private static Task<Domain.Statements.Statement?> PaidStatement(AppDbContext db, TransferLegs legs, CancellationToken ct) =>
        legs.In.StatementId is { } statementId
            ? db.Statements.SingleOrDefaultAsync(s => s.Id == statementId, ct)
            : Task.FromResult<Domain.Statements.Statement?>(null);
}
