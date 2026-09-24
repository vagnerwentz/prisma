using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Prisma.Api.Infrastructure.Auth;
using Prisma.Domain.Accounts;
using Prisma.Domain.Categories;
using Prisma.Domain.Statements;
using Prisma.Domain.Transactions;

namespace Prisma.Api.Infrastructure.Configurations;

public sealed class TransactionConfiguration : IEntityTypeConfiguration<Transaction>
{
    public void Configure(EntityTypeBuilder<Transaction> builder)
    {
        builder.ToTable("transactions", table =>
        {
            table.HasCheckConstraint("ck_transactions_amount_cents_positive", "amount_cents > 0");
            table.HasCheckConstraint("ck_transactions_installment_number", "installment_number >= 1");
            // Transferência tem par e direção; nada mais tem (docs/fase-1.md, 2.3).
            table.HasCheckConstraint("ck_transactions_transfer_legs",
                "(type = 'Transfer') = (transfer_pair_id IS NOT NULL AND transfer_direction IS NOT NULL)");
        });

        builder.Property(t => t.Type).HasConversion<string>().HasMaxLength(20);
        builder.Property(t => t.Method).HasConversion<string>().HasMaxLength(20);
        builder.Property(t => t.Source).HasConversion<string>().HasMaxLength(20);
        builder.Property(t => t.TransferDirection).HasConversion<string>().HasMaxLength(10);
        builder.Property(t => t.Description).HasMaxLength(Transaction.DescriptionMaxLength);

        builder.HasOne<AppUser>().WithMany().HasForeignKey(t => t.UserId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Account>().WithMany().HasForeignKey(t => t.AccountId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Category>().WithMany().HasForeignKey(t => t.CategoryId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Statement>().WithMany().HasForeignKey(t => t.StatementId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<InstallmentPurchase>().WithMany().HasForeignKey(t => t.InstallmentPurchaseId)
            .OnDelete(DeleteBehavior.Restrict);

        // Índices de docs/fase-1.md, mais PurchaseDate, que é o período da listagem.
        builder.HasIndex(t => new { t.UserId, t.SettlementDate });
        builder.HasIndex(t => new { t.UserId, t.AccountId, t.SettlementDate });
        builder.HasIndex(t => new { t.UserId, t.CategoryId });
        builder.HasIndex(t => new { t.UserId, t.PurchaseDate });
        builder.HasIndex(t => t.TransferPairId);
    }
}
