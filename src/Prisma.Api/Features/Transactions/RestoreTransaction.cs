using Microsoft.EntityFrameworkCore;
using Prisma.Api.Infrastructure;
using Prisma.Api.Infrastructure.Http;
using Prisma.Domain;

namespace Prisma.Api.Features.Transactions;

public static class RestoreTransaction
{
    public sealed class Handler(AppDbContext db)
    {
        public async Task<Result<TransactionResponse>> Execute(Guid id, CancellationToken ct)
        {
            // Única exceção da regra 6 do CLAUDE.md: ignora só o soft delete. O filtro de dono
            // continua valendo, então transação de outro usuário segue invisível.
            var transaction = await db.Transactions
                .IgnoreQueryFilters([AppDbContext.SoftDeleteFilter])
                .SingleOrDefaultAsync(t => t.Id == id && t.DeletedAt != null, ct);
            if (transaction is null)
                return new Error(ErrorType.NotFound, "Transação excluída não encontrada.");

            if (!await db.Accounts.AnyAsync(a => a.Id == transaction.AccountId, ct))
                return new Error(ErrorType.Conflict,
                    "A conta desta transação foi excluída; não é possível restaurá-la.");

            var categoryStillExists = transaction.CategoryId is null
                || await db.Categories.AnyAsync(c => c.Id == transaction.CategoryId, ct);

            transaction.Restore(categoryStillExists);
            await db.SaveChangesAsync(ct);
            return TransactionResponse.From(transaction);
        }
    }

    public static void Map(IEndpointRouteBuilder app) =>
        app.MapPost("/{id:guid}/restore", async (Guid id, Handler handler, CancellationToken ct) =>
        {
            var result = await handler.Execute(id, ct);
            return result.IsSuccess ? Results.Ok(result.Value) : result.Error.ToProblem();
        });
}
