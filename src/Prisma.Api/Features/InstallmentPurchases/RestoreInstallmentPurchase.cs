using Microsoft.EntityFrameworkCore;
using Prisma.Api.Features.Transactions;
using Prisma.Api.Infrastructure;
using Prisma.Api.Infrastructure.Http;
using Prisma.Domain;
using Prisma.Domain.Transactions;

namespace Prisma.Api.Features.InstallmentPurchases;

public static class RestoreInstallmentPurchase
{
    public sealed class Handler(AppDbContext db)
    {
        // "Desfazer" do DELETE /installment-purchases/{id}: volta a compra com as parcelas
        // excluídas junto com ela.
        public async Task<Result<UpdateInstallmentPurchase.Response>> Execute(Guid id, CancellationToken ct)
        {
            // Exceção da regra 6 do CLAUDE.md: ignora só o soft delete; o filtro de dono continua.
            var purchase = await db.InstallmentPurchases
                .IgnoreQueryFilters([AppDbContext.SoftDeleteFilter])
                .SingleOrDefaultAsync(p => p.Id == id && p.DeletedAt != null, ct);
            if (purchase is null)
                return new Error(ErrorType.NotFound, "Compra parcelada excluída não encontrada.");

            if (!await db.Accounts.AnyAsync(a => a.Id == purchase.AccountId, ct))
                return new Error(ErrorType.Conflict,
                    "A conta desta compra foi excluída; não é possível restaurá-la.");

            // Excluídas no mesmo SaveChanges da compra, portanto com o mesmo DeletedAt. Parcelas
            // removidas antes, por uma edição que reduziu o número de parcelas, ficam de fora.
            var installments = await db.Transactions
                .IgnoreQueryFilters([AppDbContext.SoftDeleteFilter])
                .Where(t => t.InstallmentPurchaseId == id && t.DeletedAt == purchase.DeletedAt)
                .ToListAsync(ct);

            var categoryIds = installments.Select(t => t.CategoryId).OfType<Guid>().Distinct().ToList();
            var existingCategoryIds = (await db.Categories
                .Where(c => categoryIds.Contains(c.Id))
                .Select(c => c.Id)
                .ToListAsync(ct)).ToHashSet();

            var restored = CardPurchase.Restore(purchase, installments, existingCategoryIds);
            if (!restored.IsSuccess)
                return restored.Error;

            await db.SaveChangesAsync(ct);

            return new UpdateInstallmentPurchase.Response(
                purchase.Id, purchase.AccountId, purchase.Description, purchase.TotalAmountCents,
                purchase.InstallmentCount, purchase.PurchaseDate,
                installments.OrderBy(t => t.InstallmentNumber).Select(TransactionResponse.From).ToList());
        }
    }

    public static void Map(IEndpointRouteBuilder app) =>
        app.MapPost("/installment-purchases/{id:guid}/restore", async (Guid id, Handler handler, CancellationToken ct) =>
        {
            var result = await handler.Execute(id, ct);
            return result.IsSuccess ? Results.Ok(result.Value) : result.Error.ToProblem();
        })
            .Produces<UpdateInstallmentPurchase.Response>(200)
            .ProducesProblem(404)
            .ProducesProblem(409);
}
