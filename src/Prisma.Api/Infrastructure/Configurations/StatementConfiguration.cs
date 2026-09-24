using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Prisma.Api.Infrastructure.Auth;
using Prisma.Domain.Accounts;
using Prisma.Domain.Statements;

namespace Prisma.Api.Infrastructure.Configurations;

public sealed class StatementConfiguration : IEntityTypeConfiguration<Statement>
{
    public const string AccountReferenceIndex = "ux_statements_account_reference";

    public void Configure(EntityTypeBuilder<Statement> builder)
    {
        builder.ToTable("statements", table =>
            table.HasCheckConstraint("ck_statements_due_after_closing", "due_date >= closing_date"));

        builder.Ignore(s => s.Dates);
        builder.Property(s => s.Reference).HasMaxLength(Statement.ReferenceLength).IsFixedLength();

        builder.HasOne<AppUser>().WithMany().HasForeignKey(s => s.UserId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Account>().WithMany().HasForeignKey(s => s.AccountId).OnDelete(DeleteBehavior.Restrict);

        // Uma fatura por ciclo do cartão (docs/fase-1.md).
        builder.HasIndex(s => new { s.AccountId, s.Reference })
            .IsUnique()
            .HasDatabaseName(AccountReferenceIndex);
    }
}
