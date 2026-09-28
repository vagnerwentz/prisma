using Microsoft.EntityFrameworkCore;
using Prisma.Api.Infrastructure;
using Prisma.Domain;
using Prisma.Domain.Accounts;
using Prisma.Domain.Statements;
using Prisma.Domain.Transactions;

namespace Prisma.Api.Features.Statements;

// Tudo o que o recálculo de faturas precisa (docs/fase-2.md, 2.9): a fatura editada, o cartão, todas as
// faturas e todas as transações dele, para nenhuma parcela de compra com fatura paga se mover. O PATCH
// lê rastreando (para gravar); a prévia (2.10) lê sem rastrear, e assim não tem como gravar nada.
public sealed record CardLedger(Statement Statement, Account Card, List<Statement> Statements, List<Transaction> Transactions)
{
    public static async Task<Result<CardLedger>> Load(AppDbContext db, Guid statementId, bool tracking, CancellationToken ct)
    {
        var statements = tracking ? db.Statements : db.Statements.AsNoTracking();
        var statement = await statements.SingleOrDefaultAsync(s => s.Id == statementId, ct);
        if (statement is null)
            return new Error(ErrorType.NotFound, "Fatura não encontrada.");

        var card = await db.Accounts.AsNoTracking().SingleOrDefaultAsync(a => a.Id == statement.AccountId, ct);
        if (card is null)
            return new Error(ErrorType.Conflict, "O cartão desta fatura foi excluído.");

        var all = await statements.Where(s => s.AccountId == card.Id && s.Id != statement.Id).ToListAsync(ct);
        all.Add(statement);
        var transactions = await (tracking ? db.Transactions : db.Transactions.AsNoTracking())
            .Where(t => t.AccountId == card.Id)
            .ToListAsync(ct);
        return new CardLedger(statement, card, all, transactions);
    }
}
