using Microsoft.EntityFrameworkCore;
using Prisma.Api.Infrastructure;
using Prisma.Domain;
using Prisma.Domain.Accounts;
using Prisma.Domain.Investments.Trades;
using Prisma.Domain.Transactions;

namespace Prisma.Api.Features.Investments;

// A liquidação da nota no caixa da conta (docs/investimentos.md, "I.8"): gravada, trocada, restaurada e excluída junto
// com a nota, no mesmo SaveChanges. O domínio decide o que deve existir (TradeSettlement.Settle); aqui se carrega a
// conta e a linha que já havia, inclusive a excluída (uma nota tem uma liquidação só, que volta quando a nota volta).
internal static class NoteSettlement
{
    // A conta pedida: do usuário (o filtro de dono faz a de outro parecer inexistente), que não seja cartão e, ao
    // trocar de conta, que esteja ativa. Sem id, sem vínculo.
    public static async Task<Result<Account?>> Account(AppDbContext db, Guid? accountId, Guid? currentAccountId, CancellationToken ct)
    {
        if (accountId is null)
            return Result<Account?>.Success(null);

        var account = await db.Accounts.SingleOrDefaultAsync(a => a.Id == accountId, ct);
        if (account is null)
            return new Error(ErrorType.NotFound, "Conta não encontrada.");

        if (TradeSettlement.CheckAccount(account) is { } error)
            return error;

        if (!account.IsActive && accountId != currentAccountId)
            return new Error(ErrorType.Validation, "A conta está inativa.");

        return Result<Account?>.Success(account);
    }

    // Põe a liquidação no estado que a nota pede: cria, atualiza (e restaura, se estava excluída) ou tira.
    public static async Task Sync(AppDbContext db, TradeNote note, Account? account, CancellationToken ct)
    {
        var existing = await Existing(db, note.Id, ct);
        var settlement = TradeSettlement.Settle(note, account, existing);

        if (settlement is null)
        {
            if (existing is { DeletedAt: null })
                db.Transactions.Remove(existing);
            return;
        }

        if (existing is null)
            db.Transactions.Add(settlement);
        else if (existing.DeletedAt is not null)
            existing.Restore(categoryStillExists: true);
    }

    // A nota foi excluída: a liquidação sai com ela.
    public static async Task Remove(AppDbContext db, Guid noteId, CancellationToken ct)
    {
        if (await Existing(db, noteId, ct) is { DeletedAt: null } existing)
            db.Transactions.Remove(existing);
    }

    // Regra 6 do CLAUDE.md: ignora só o soft delete; o filtro de dono continua.
    private static Task<Transaction?> Existing(AppDbContext db, Guid noteId, CancellationToken ct) =>
        db.Transactions.IgnoreQueryFilters([AppDbContext.SoftDeleteFilter]).SingleOrDefaultAsync(t => t.TradeNoteId == noteId, ct);
}
