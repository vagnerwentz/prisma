using Microsoft.EntityFrameworkCore;
using Prisma.Api.Infrastructure;
using Prisma.Domain;
using Prisma.Domain.Transactions;

namespace Prisma.Api.Features.Transactions;

// Vocabulário do autocompletar da descrição (docs/fase-2.md, 2.8). Uma requisição por sessão: o filtro
// por tecla roda no aparelho. O banco traz os lançamentos dos últimos 12 meses (a parcela 1 com o
// total da compra); o domínio agrupa e ordena.
public static class ListDescriptions
{
    // Teto de linhas lidas: mais que um ano de uso intenso, e a consulta segue pelo índice
    // (user_id, purchase_date).
    private const int MaxRows = 5000;

    public sealed record Item(
        string Description, TransactionType Type, Guid? CategoryId, Guid AccountId, PaymentMethod Method,
        long LastAmountCents, int Count, DateOnly LastUsedOn);

    public sealed class Handler(AppDbContext db, IClock clock)
    {
        public async Task<IReadOnlyList<Item>> Execute(CancellationToken ct)
        {
            var today = clock.Today;
            var since = DescriptionHistory.Since(today);

            var uses = await db.Transactions
                .AsNoTracking()
                .Where(t => (t.Type == TransactionType.Expense || t.Type == TransactionType.Income)
                            && t.PurchaseDate >= since
                            && t.Description != ""
                            && (t.InstallmentNumber == null || t.InstallmentNumber == 1))
                .OrderByDescending(t => t.PurchaseDate)
                .Take(MaxRows)
                .Select(t => new DescriptionUse(
                    t.Type, t.Description, t.CategoryId, t.AccountId, t.Method, t.AmountCents, t.PurchaseDate,
                    t.InstallmentNumber,
                    db.InstallmentPurchases
                        .Where(p => p.Id == t.InstallmentPurchaseId)
                        .Select(p => (long?)p.TotalAmountCents)
                        .FirstOrDefault(),
                    t.CreatedAt))
                .ToListAsync(ct);

            return DescriptionHistory.Of(uses, today)
                .Select(s => new Item(s.Description, s.Type, s.CategoryId, s.AccountId, s.Method, s.LastAmountCents, s.Count, s.LastUsedOn))
                .ToList();
        }
    }

    public static void Map(IEndpointRouteBuilder app) =>
        app.MapGet("/descriptions", async (Handler handler, CancellationToken ct) => Results.Ok(await handler.Execute(ct)))
            .Produces<IReadOnlyList<Item>>(200);
}
