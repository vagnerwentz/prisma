using Microsoft.EntityFrameworkCore;
using Prisma.Api.Infrastructure;
using Prisma.Domain;
using Prisma.Domain.Accounts;
using Prisma.Domain.Categories;

namespace Prisma.Api.Features.Transactions;

// Carrega a conta e a categoria citadas no corpo da requisição. Os filtros globais garantem
// que são do usuário: id de outro usuário resulta em "não encontrada".
public static class TransactionReferences
{
    public static async Task<Result<(Account Account, Category? Category)>> Load(
        AppDbContext db, Guid accountId, Guid? categoryId, CancellationToken ct)
    {
        var account = await db.Accounts.SingleOrDefaultAsync(a => a.Id == accountId, ct);
        if (account is null)
            return new Error(ErrorType.Validation, "Conta não encontrada.");

        if (categoryId is null)
            return (account, null);

        var category = await db.Categories.SingleOrDefaultAsync(c => c.Id == categoryId, ct);
        if (category is null)
            return new Error(ErrorType.Validation, "Categoria não encontrada.");

        return (account, category);
    }
}
