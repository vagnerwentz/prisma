using Microsoft.EntityFrameworkCore;
using Prisma.Api.Infrastructure;
using Prisma.Domain.Statements;

namespace Prisma.Api.Features.Statements;

// Pôr ou tirar compra de uma fatura conta como mudança nela (docs/fase-2.md, 2.9, regra 6): o
// UpdatedAt vai para o UPDATE, que confere o xmin. Se a fatura for paga ao mesmo tempo, um dos dois
// recebe 409, e a fatura paga nunca muda de valor. Fatura recém-aberta já é um INSERT.
public static class StatementTouch
{
    public static void Touch(AppDbContext db, IEnumerable<Statement> statements)
    {
        foreach (var statement in statements.Distinct())
        {
            var entry = db.Entry(statement);
            if (entry.State == EntityState.Unchanged)
                entry.Property(s => s.UpdatedAt).IsModified = true;
        }
    }
}
