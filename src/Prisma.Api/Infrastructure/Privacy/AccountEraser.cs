using Microsoft.EntityFrameworkCore;
using Npgsql;
using Prisma.Domain;

namespace Prisma.Api.Infrastructure.Privacy;

// Apaga de vez uma conta e tudo o que é dela (etapa A.3): o direito de eliminação da LGPD, sem "Desfazer".
//
// Em SQL, numa transação só: o EF transformaria cada Remove em soft delete (CLAUDE.md, regra 7), e carregar tudo para
// apagar seria lento. A ordem segue as chaves estrangeiras (todas Restrict, de propósito); os vínculos internos (estorno
// → compra, subcategoria → categoria) são desfeitos antes. O usuário sai por último e leva junto o que tem cascata
// (dados de login do Identity, e-mails na fila). O id entra em deleted_accounts na mesma gravação.
//
// Tabela nova com user_id entra aqui: o teste AccountDeletionTests confere, pelo schema, que nada da pessoa sobra.
public sealed class AccountEraser(AppDbContext db, IClock clock)
{
    // Público para o teste que confere, pelo schema, que toda tabela com dono está aqui ou sai em cascata.
    public static readonly IReadOnlyList<string> Statements =
    [
        "DELETE FROM trades WHERE trade_note_id IN (SELECT id FROM trade_notes WHERE user_id = @user)",
        "DELETE FROM trade_notes WHERE user_id = @user",
        "DELETE FROM event_reviews WHERE user_id = @user",
        "DELETE FROM asset_requests WHERE user_id = @user",
        "DELETE FROM holdings WHERE user_id = @user",
        "DELETE FROM recurrence_pendings WHERE user_id = @user",
        "UPDATE transactions SET refunded_transaction_id = NULL WHERE user_id = @user AND refunded_transaction_id IS NOT NULL",
        "DELETE FROM transactions WHERE user_id = @user",
        "DELETE FROM installment_purchases WHERE user_id = @user",
        "DELETE FROM recurrences WHERE user_id = @user",
        "DELETE FROM statements WHERE user_id = @user",
        "UPDATE categories SET parent_category_id = NULL WHERE user_id = @user AND parent_category_id IS NOT NULL",
        "DELETE FROM categories WHERE user_id = @user",
        "DELETE FROM accounts WHERE user_id = @user",
    ];

    // Devolve false se a conta já não existia (pedido repetido, ou reaplicação de uma conta que não voltou).
    public async Task<bool> EraseAsync(Guid userId, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        foreach (var sql in Statements)
            await db.Database.ExecuteSqlRawAsync(sql, [new NpgsqlParameter("user", userId)], ct);

        var removed = await db.Database.ExecuteSqlRawAsync(
            "DELETE FROM users WHERE id = @user", [new NpgsqlParameter("user", userId)], ct);
        await db.Database.ExecuteSqlRawAsync(
            "INSERT INTO deleted_accounts (user_id, deleted_at) VALUES (@user, @now) ON CONFLICT (user_id) DO NOTHING",
            [new NpgsqlParameter("user", userId), new NpgsqlParameter("now", clock.UtcNow)], ct);
        await transaction.CommitAsync(ct);
        return removed > 0;
    }
}
