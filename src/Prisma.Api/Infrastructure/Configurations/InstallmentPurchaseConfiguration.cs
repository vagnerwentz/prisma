using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Prisma.Api.Infrastructure.Auth;
using Prisma.Domain.Accounts;
using Prisma.Domain.Transactions;

namespace Prisma.Api.Infrastructure.Configurations;

public sealed class InstallmentPurchaseConfiguration : IEntityTypeConfiguration<InstallmentPurchase>
{
    public void Configure(EntityTypeBuilder<InstallmentPurchase> builder)
    {
        builder.ToTable("installment_purchases", table =>
        {
            table.HasCheckConstraint("ck_installment_purchases_installment_count",
                $"installment_count BETWEEN 2 AND {InstallmentPurchase.MaxInstallments}");
            table.HasCheckConstraint("ck_installment_purchases_cent_per_installment",
                "total_amount_cents >= installment_count");
        });

        builder.Property(p => p.Description).HasMaxLength(Transaction.DescriptionMaxLength);

        builder.HasOne<AppUser>().WithMany().HasForeignKey(p => p.UserId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Account>().WithMany().HasForeignKey(p => p.AccountId).OnDelete(DeleteBehavior.Restrict);
    }
}
