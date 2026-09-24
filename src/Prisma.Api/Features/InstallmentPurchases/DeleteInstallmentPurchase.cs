using Microsoft.EntityFrameworkCore;
using Prisma.Api.Infrastructure;
using Prisma.Api.Infrastructure.Http;
using Prisma.Domain;

namespace Prisma.Api.Features.InstallmentPurchases;

public static class DeleteInstallmentPurchase
{
    public sealed class Handler(AppDbContext db)
    {
        // Soft delete da compra e de todas as parcelas (docs/fase-1.md, 2.2).
        public async Task<Result<Guid>> Execute(Guid id, CancellationToken ct)
        {
            var purchase = await db.InstallmentPurchases.SingleOrDefaultAsync(p => p.Id == id, ct);
            if (purchase is null)
                return new Error(ErrorType.NotFound, "Compra parcelada não encontrada.");

            var installments = await db.Transactions.Where(t => t.InstallmentPurchaseId == id).ToListAsync(ct);

            db.Transactions.RemoveRange(installments);
            db.InstallmentPurchases.Remove(purchase);
            await db.SaveChangesAsync(ct);
            return purchase.Id;
        }
    }

    public static void Map(IEndpointRouteBuilder app) =>
        app.MapDelete("/installment-purchases/{id:guid}", async (Guid id, Handler handler, CancellationToken ct) =>
        {
            var result = await handler.Execute(id, ct);
            return result.IsSuccess ? Results.NoContent() : result.Error.ToProblem();
        })
            .Produces(204)
            .ProducesProblem(404);
}
