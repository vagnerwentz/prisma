using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Prisma.Api.Infrastructure.Auth;
using Prisma.Domain.Accounts;

namespace Prisma.Api.Infrastructure.Configurations;

public sealed class AccountConfiguration : IEntityTypeConfiguration<Account>
{
    public void Configure(EntityTypeBuilder<Account> builder)
    {
        // As invariantes vivem no domínio; as constraints são a segunda linha de defesa.
        builder.ToTable("accounts", table =>
        {
            table.HasCheckConstraint("ck_accounts_card_fields",
                "(type = 'CreditCard' AND closing_day IS NOT NULL AND due_day IS NOT NULL) OR " +
                "(type <> 'CreditCard' AND closing_day IS NULL AND due_day IS NULL AND credit_limit_cents IS NULL)");
            table.HasCheckConstraint("ck_accounts_closing_day", "closing_day BETWEEN 1 AND 31");
            table.HasCheckConstraint("ck_accounts_due_day", "due_day BETWEEN 1 AND 31");
            table.HasCheckConstraint("ck_accounts_credit_limit_cents", "credit_limit_cents >= 0");
        });

        builder.Property(a => a.Name).HasMaxLength(Account.NameMaxLength);

        // Texto, não inteiro: legível no banco e imune a reordenação do enum.
        builder.Property(a => a.Type).HasConversion<string>().HasMaxLength(20);

        builder.HasOne<AppUser>().WithMany().HasForeignKey(a => a.UserId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(a => a.UserId);
    }
}
