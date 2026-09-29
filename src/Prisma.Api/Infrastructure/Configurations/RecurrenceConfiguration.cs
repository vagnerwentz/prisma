using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Prisma.Api.Infrastructure.Auth;
using Prisma.Domain.Accounts;
using Prisma.Domain.Categories;
using Prisma.Domain.Recurrences;
using Prisma.Domain.Statements;
using Prisma.Domain.Transactions;

namespace Prisma.Api.Infrastructure.Configurations;

// Lançamentos que se repetem (docs/fase-2.md, 2.14).
public sealed class RecurrenceConfiguration : IEntityTypeConfiguration<Recurrence>
{
    public void Configure(EntityTypeBuilder<Recurrence> builder)
    {
        builder.ToTable("recurrences", table =>
        {
            table.HasCheckConstraint("ck_recurrences_amount_cents_positive", "amount_cents > 0");
            table.HasCheckConstraint("ck_recurrences_type", "type IN ('Expense', 'Income')");
        });

        builder.Ignore(r => r.NextOccurrence);
        builder.Property(r => r.Type).HasConversion<string>().HasMaxLength(20);
        builder.Property(r => r.Method).HasConversion<string>().HasMaxLength(20);
        builder.Property(r => r.Frequency).HasConversion<string>().HasMaxLength(20);
        builder.Property(r => r.Description).HasMaxLength(Transaction.DescriptionMaxLength);

        builder.HasOne<AppUser>().WithMany().HasForeignKey(r => r.UserId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Account>().WithMany().HasForeignKey(r => r.AccountId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Category>().WithMany().HasForeignKey(r => r.CategoryId).OnDelete(DeleteBehavior.Restrict);

        // A geração e a edição da série ao mesmo tempo: uma delas recebe o conflito (regra 10). É também a
        // segunda trava contra gerar duas vezes: duas execuções leem a mesma versão, e só uma grava.
        builder.Property<uint>("Version").IsRowVersion();
    }
}

public sealed class RecurrencePendingConfiguration : IEntityTypeConfiguration<RecurrencePending>
{
    public const string OccurrenceIndex = "ux_recurrence_pendings_occurrence";

    public void Configure(EntityTypeBuilder<RecurrencePending> builder)
    {
        builder.ToTable("recurrence_pendings", table =>
            table.HasCheckConstraint("ck_recurrence_pendings_amount_cents_positive", "amount_cents > 0"));
        builder.Property(p => p.StatementReference).HasMaxLength(Statement.ReferenceLength).IsFixedLength();

        builder.HasOne<AppUser>().WithMany().HasForeignKey(p => p.UserId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Recurrence>().WithMany().HasForeignKey(p => p.RecurrenceId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Account>().WithMany().HasForeignKey(p => p.AccountId).OnDelete(DeleteBehavior.Restrict);

        // Uma pendência por ocorrência, contando as já resolvidas.
        builder.HasIndex(p => new { p.RecurrenceId, p.OccurrenceDate }).IsUnique().HasDatabaseName(OccurrenceIndex);
    }
}
