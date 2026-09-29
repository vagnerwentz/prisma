using Microsoft.EntityFrameworkCore;
using Prisma.Api.Features.Statements;
using Prisma.Api.Infrastructure;
using Prisma.Api.Infrastructure.Auth;
using Prisma.Api.Infrastructure.Configurations;
using Prisma.Api.Infrastructure.Logging;
using Prisma.Domain;
using Prisma.Domain.Accounts;
using Prisma.Domain.Categories;
using Prisma.Domain.Recurrences;
using Prisma.Domain.Statements;

namespace Prisma.Api.Features.Recurrences;

public sealed record RecurrenceRunSummary(int Users, int Created, int Pending, int Failed)
{
    public static RecurrenceRunSummary operator +(RecurrenceRunSummary a, RecurrenceRunSummary b) =>
        new(a.Users + b.Users, a.Created + b.Created, a.Pending + b.Pending, a.Failed + b.Failed);
}

// Gera as ocorrências vencidas dos lançamentos que se repetem (docs/fase-2.md, 2.14, A3 a A6). Não
// importa quando nem quantas vezes roda: cada série gera o que venceu depois de GeneratedThrough, e
// gravar o lançamento e o avanço juntos, com o índice único da ocorrência e o xmin da série, impede
// que duas execuções (ou uma repetida) gerem o mesmo lançamento.
//
// Cada usuário é processado no seu escopo de DI, com o usuário fixo (ScopedUser): o filtro de dono e a
// trava de gravação valem como numa requisição. Cada série grava sozinha: a que falha não trava as
// outras, e a próxima execução tenta de novo, porque GeneratedThrough não andou.
public sealed class RecurrenceRunner(IServiceScopeFactory scopes, IClock clock, ILogger<RecurrenceRunner> logger)
{
    public async Task<RecurrenceRunSummary> RunAsync(CancellationToken ct)
    {
        List<Guid> users;
        await using (var scope = scopes.CreateAsyncScope())
        {
            // A tabela de usuários não é do domínio: não tem filtro de dono.
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            users = await db.Users.Select(u => u.Id).ToListAsync(ct);
        }

        var summary = new RecurrenceRunSummary(0, 0, 0, 0);
        foreach (var userId in users)
            summary += await RunForUserAsync(userId, ct);

        logger.RecurrenceRunFinished(summary.Users, summary.Created, summary.Pending, summary.Failed);
        return summary;
    }

    // Também chamado logo depois de criar ou editar uma série, para gerar sem esperar a próxima hora.
    public async Task<RecurrenceRunSummary> RunForUserAsync(Guid userId, CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<ScopedUser>().UserId = userId;
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var today = clock.Today;

        var due = await db.Recurrences
            .Where(r => r.GeneratedThrough < today && (r.EndDate == null || r.EndDate > r.GeneratedThrough))
            .Select(r => r.Id)
            .ToListAsync(ct);

        var summary = new RecurrenceRunSummary(due.Count > 0 ? 1 : 0, 0, 0, 0);
        foreach (var id in due)
        {
            try
            {
                var (created, pending) = await ConcurrentStatementOpening.Retry(db, () => Generate(db, id, today, ct));
                summary += new RecurrenceRunSummary(0, created, pending, 0);
            }
            catch (DbUpdateException ex) when (
                ex.IsUniqueViolation(TransactionConfiguration.RecurrenceOccurrenceIndex) ||
                ex.IsUniqueViolation(RecurrencePendingConfiguration.OccurrenceIndex))
            {
                db.ChangeTracker.Clear();
                logger.RecurrenceSkipped(id, "occurrence already generated");
            }
            catch (DbUpdateConcurrencyException)
            {
                // Outra execução gerou a série, a pessoa a editou, ou a fatura foi paga ao mesmo tempo.
                db.ChangeTracker.Clear();
                logger.RecurrenceSkipped(id, "changed while generating");
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                db.ChangeTracker.Clear();
                logger.RecurrenceFailed(ex, id);
                summary += new RecurrenceRunSummary(0, 0, 0, 1);
            }
        }

        return summary;
    }

    // Carrega tudo a cada tentativa: o ConcurrentStatementOpening limpa o ChangeTracker e refaz.
    private async Task<(int Created, int Pending)> Generate(AppDbContext db, Guid id, DateOnly today, CancellationToken ct)
    {
        var recurrence = await db.Recurrences.SingleOrDefaultAsync(r => r.Id == id, ct);
        var account = recurrence is null ? null : await db.Accounts.SingleOrDefaultAsync(a => a.Id == recurrence.AccountId, ct);
        if (recurrence is null || account is null)
            return (0, 0);

        // Categoria excluída: o filtro não a traz, e o lançamento sai sem categoria (regra 8).
        Category? category = recurrence.CategoryId is { } categoryId
            ? await db.Categories.SingleOrDefaultAsync(c => c.Id == categoryId, ct)
            : null;
        List<Statement> statements = account.Type == AccountType.CreditCard
            ? await db.Statements.Where(s => s.AccountId == account.Id).ToListAsync(ct)
            : [];

        var result = recurrence.Generate(today, account, category, statements);

        db.Statements.AddRange(result.Opened);
        db.Transactions.AddRange(result.Created);
        db.RecurrencePendings.AddRange(result.Pending.Select(p => RecurrencePending.For(recurrence, p)));
        // A cobrança que entra numa fatura existente conta como mudança nela (regra 10; 2.9, regra 6).
        StatementTouch.Touch(db, statements.Where(s => result.Created.Any(t => t.StatementId == s.Id)));
        await db.SaveChangesAsync(ct);

        if (result.Created.Count + result.Pending.Count > 0)
            logger.RecurrenceGenerated(recurrence.Id, result.Created.Count, result.Pending.Count);
        return (result.Created.Count, result.Pending.Count);
    }
}
