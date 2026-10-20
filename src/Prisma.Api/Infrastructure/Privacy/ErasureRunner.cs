using Microsoft.EntityFrameworkCore;
using Npgsql;
using Prisma.Api.Infrastructure.Logging;
using Prisma.Domain;

namespace Prisma.Api.Infrastructure.Privacy;

public sealed record ErasureSummary(int PurgedRows, int AccountsReapplied, int AccountsForgotten);

// O que a política de privacidade promete sobre apagar (etapa A.3), numa passada:
// 1. o que a pessoa excluiu some do banco de dados depois de Grace (o "Desfazer" vale até lá; a política diz "em até
//    1 hora", e a tarefa roda a cada Interval);
// 2. conta apagada que voltou com um backup restaurado é apagada de novo (deleted_accounts);
// 3. o id da conta apagada é esquecido depois de 90 dias, quando os backups daquele dia já foram descartados.
//
// A limpeza dos excluídos passa por todos os usuários de uma vez, em SQL: não lê dado de ninguém, só apaga linha com
// deleted_at vencido (exceção à regra 6 do CLAUDE.md, como as migrations). Quem ainda é apontado por algo vivo perde só
// o vínculo: o estorno de uma compra apagada vira estorno avulso, e a série cuja categoria foi apagada fica sem
// categoria. A conta apagada leva as faturas e as séries dela (as séries são encerradas ao apagar a conta); os
// lançamentos da série em outra conta ficam, sem a série. Nenhuma ocorrência de série deixa marca: a série só gera
// datas depois de GeneratedThrough, que nunca volta, então a data apagada não reaparece.
public sealed class ErasureRunner(IServiceScopeFactory scopes, IClock clock, ILogger<ErasureRunner> logger)
{
    public static readonly TimeSpan Grace = TimeSpan.FromMinutes(30);
    public static readonly TimeSpan Interval = TimeSpan.FromMinutes(10);
    public static readonly TimeSpan DeletedAccountsRetention = TimeSpan.FromDays(90);

    // Tabelas com exclusão pelo "Desfazer", todas limpas aqui; faturas e séries nunca são excluídas sozinhas e saem
    // com a conta. O teste confere, pelo schema, que toda tabela com deleted_at está numa das duas listas.
    public static readonly IReadOnlySet<string> PurgedTables = new HashSet<string>
    {
        "transactions", "recurrence_pendings", "installment_purchases", "holdings", "asset_requests", "event_reviews",
        "trade_notes", "fixed_income_redemptions", "fixed_income_deposits", "savings_goals", "categories", "accounts",
    };

    public static readonly IReadOnlySet<string> PurgedWithAccount = new HashSet<string> { "statements", "recurrences" };

    // Público para o teste que confere a lista de tabelas.
    public static readonly IReadOnlyList<string> PurgeStatements =
    [
        // Lançamentos. O estorno vivo de uma compra que sai fica avulso.
        """
        UPDATE transactions SET refunded_transaction_id = NULL
        WHERE refunded_transaction_id IN (SELECT id FROM transactions WHERE deleted_at < @cutoff)
        """,
        "DELETE FROM transactions WHERE deleted_at < @cutoff",
        "DELETE FROM recurrence_pendings WHERE deleted_at < @cutoff",
        """
        DELETE FROM installment_purchases p WHERE p.deleted_at < @cutoff
        AND NOT EXISTS (SELECT 1 FROM transactions t WHERE t.installment_purchase_id = p.id)
        """,

        // Carteira. As linhas da nota vivem dentro dela.
        "DELETE FROM holdings WHERE deleted_at < @cutoff",
        "DELETE FROM asset_requests WHERE deleted_at < @cutoff",
        "DELETE FROM event_reviews WHERE deleted_at < @cutoff",
        "DELETE FROM trades WHERE trade_note_id IN (SELECT id FROM trade_notes WHERE deleted_at < @cutoff)",
        "DELETE FROM trade_notes WHERE deleted_at < @cutoff",

        // Renda fixa: os aportes antes dos objetivos que os guardavam. Excluir o objetivo deixa os aportes soltos ou os leva junto,
        // então um objetivo vencido nunca é apontado por aporte vivo; o NOT EXISTS é a segunda trava.
        // Os resgates excluídos à parte, e os do aporte que sai de vez vão com ele (a chave estrangeira é em cascata).
        "DELETE FROM fixed_income_redemptions WHERE deleted_at < @cutoff",
        "DELETE FROM fixed_income_deposits WHERE deleted_at < @cutoff",
        """
        DELETE FROM savings_goals g WHERE g.deleted_at < @cutoff
        AND NOT EXISTS (SELECT 1 FROM fixed_income_deposits d WHERE d.goal_id = g.id)
        """,

        // Categorias: a série que usava a categoria fica sem ela; subcategorias antes das categorias de cima.
        "UPDATE recurrences SET category_id = NULL WHERE category_id IN (SELECT id FROM categories WHERE deleted_at < @cutoff)",
        """
        DELETE FROM categories c WHERE c.deleted_at < @cutoff AND c.parent_category_id IS NOT NULL
        AND NOT EXISTS (SELECT 1 FROM transactions t WHERE t.category_id = c.id)
        """,
        """
        DELETE FROM categories c WHERE c.deleted_at < @cutoff
        AND NOT EXISTS (SELECT 1 FROM transactions t WHERE t.category_id = c.id)
        AND NOT EXISTS (SELECT 1 FROM categories s WHERE s.parent_category_id = c.id)
        """,

        // Contas: só a que nada vivo aponta mais (a exclusão da conta já exige que esteja vazia; isto é a segunda trava).
        "CREATE TEMP TABLE purged_accounts (id uuid PRIMARY KEY) ON COMMIT DROP",
        """
        INSERT INTO purged_accounts
        SELECT a.id FROM accounts a WHERE a.deleted_at < @cutoff
        AND NOT EXISTS (SELECT 1 FROM transactions t WHERE t.account_id = a.id)
        AND NOT EXISTS (SELECT 1 FROM transactions t JOIN statements s ON s.id = t.statement_id WHERE s.account_id = a.id)
        AND NOT EXISTS (SELECT 1 FROM installment_purchases p WHERE p.account_id = a.id)
        """,
        """
        DELETE FROM recurrence_pendings
        WHERE account_id IN (SELECT id FROM purged_accounts)
        OR recurrence_id IN (SELECT r.id FROM recurrences r JOIN purged_accounts a ON a.id = r.account_id)
        """,
        """
        UPDATE transactions SET recurrence_id = NULL, occurrence_date = NULL, amount_estimated = FALSE
        WHERE recurrence_id IN (SELECT r.id FROM recurrences r JOIN purged_accounts a ON a.id = r.account_id)
        """,
        "DELETE FROM recurrences WHERE account_id IN (SELECT id FROM purged_accounts)",
        "DELETE FROM statements WHERE account_id IN (SELECT id FROM purged_accounts)",
        "DELETE FROM accounts WHERE id IN (SELECT id FROM purged_accounts)",
    ];

    public async Task<ErasureSummary> RunAsync(CancellationToken ct)
    {
        var purged = await PurgeDeletedAsync(ct);
        var reapplied = await ReapplyAccountDeletionsAsync(ct);
        var forgotten = await ForgetOldDeletionsAsync(ct);
        return new ErasureSummary(purged, reapplied, forgotten);
    }

    private async Task<int> PurgeDeletedAsync(CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var cutoff = clock.UtcNow - Grace;

        var removed = 0;
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        foreach (var sql in PurgeStatements)
        {
            var rows = await db.Database.ExecuteSqlRawAsync(sql, [new NpgsqlParameter("cutoff", cutoff)], ct);
            if (sql.StartsWith("DELETE", StringComparison.Ordinal))
                removed += rows;
        }

        await transaction.CommitAsync(ct);
        if (removed > 0)
            logger.DeletedItemsPurged(removed);
        return removed;
    }

    // Só acha alguém depois de restaurar um backup: a conta apagada não volta de outro jeito.
    private async Task<int> ReapplyAccountDeletionsAsync(CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var returned = await db.DeletedAccounts
            .Where(d => db.Users.Any(u => u.Id == d.UserId))
            .Select(d => d.UserId)
            .ToListAsync(ct);

        var eraser = scope.ServiceProvider.GetRequiredService<AccountEraser>();
        foreach (var userId in returned)
        {
            await eraser.EraseAsync(userId, ct);
            logger.AccountDeletionReapplied(userId);
        }

        return returned.Count;
    }

    private async Task<int> ForgetOldDeletionsAsync(CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var before = clock.UtcNow - DeletedAccountsRetention;
        var forgotten = await db.DeletedAccounts.Where(d => d.DeletedAt < before).ExecuteDeleteAsync(ct);
        if (forgotten > 0)
            logger.DeletedAccountsForgotten(forgotten);
        return forgotten;
    }
}
