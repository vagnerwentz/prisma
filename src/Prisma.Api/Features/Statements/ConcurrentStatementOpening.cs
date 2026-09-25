using Microsoft.EntityFrameworkCore;
using Prisma.Api.Infrastructure;
using Prisma.Api.Infrastructure.Configurations;

namespace Prisma.Api.Features.Statements;

// Duas operações ao mesmo tempo no mesmo cartão (toque duplo, dois aparelhos) podem ler que a
// fatura do mês ainda não existe e tentar criá-la. O índice único deixa passar só uma; a outra
// descarta o que tinha em memória e refaz a operação inteira, que agora encontra a fatura criada.
// Quando as duas criam várias faturas e parcelas, os bloqueios podem se cruzar e o Postgres aborta
// uma delas por impasse (deadlock): também se refaz.
// A operação precisa carregar tudo de novo a cada tentativa (o ChangeTracker é limpo).
public static class ConcurrentStatementOpening
{
    private const int Attempts = 3;

    public static async Task<T> Retry<T>(AppDbContext db, Func<Task<T>> operation)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                return await operation();
            }
            catch (Exception ex) when (attempt < Attempts && IsLostRace(ex))
            {
                db.ChangeTracker.Clear();
            }
        }
    }

    // O EF trata o impasse como falha transitória e o entrega embrulhado num InvalidOperationException.
    private static bool IsLostRace(Exception ex) =>
        (ex as DbUpdateException ?? ex.InnerException as DbUpdateException) is { } update
        && (update.IsUniqueViolation(StatementConfiguration.AccountReferenceIndex) || update.IsDeadlock());
}
