using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Prisma.Api.Infrastructure.Auth;
using Prisma.Domain.Accounts;
using Prisma.Domain.Categories;
using Prisma.Domain.Recurrences;
using Prisma.Domain.Statements;
using Prisma.Domain.Transactions;

namespace Prisma.Api.Infrastructure.Configurations;

public sealed class TransactionConfiguration : IEntityTypeConfiguration<Transaction>
{
    public const string RecurrenceOccurrenceIndex = "ux_transactions_recurrence_occurrence";

    public void Configure(EntityTypeBuilder<Transaction> builder)
    {
        builder.ToTable("transactions", table =>
        {
            table.HasCheckConstraint("ck_transactions_amount_cents_positive", "amount_cents > 0");
            table.HasCheckConstraint("ck_transactions_installment_number", "installment_number >= 1");
            // Transferência tem par e direção; nada mais tem (docs/fase-1.md, 2.3).
            table.HasCheckConstraint("ck_transactions_transfer_legs",
                "(type = 'Transfer') = (transfer_pair_id IS NOT NULL AND transfer_direction IS NOT NULL)");
            table.HasCheckConstraint("ck_transactions_refund_link",
                "refunded_transaction_id IS NULL OR type = 'Refund'");
            // Só compra no cartão fica presa a uma fatura (docs/fase-2.md, 2.9, regra 2).
            table.HasCheckConstraint("ck_transactions_statement_pinned",
                "NOT statement_pinned OR (statement_id IS NOT NULL AND type = 'Expense')");
            // Lançamento de série tem a série e a data da ocorrência; nada mais tem (docs/fase-2.md, 2.14).
            table.HasCheckConstraint("ck_transactions_recurrence_occurrence",
                "(recurrence_id IS NULL) = (occurrence_date IS NULL)");
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
        builder.HasOne<Recurrence>().WithMany().HasForeignKey(t => t.RecurrenceId).OnDelete(DeleteBehavior.Restrict);
        // Ligar um lançamento a uma série confere que ele ainda não tinha série: dois toques em "se repete"
        // ao mesmo tempo criariam duas séries e o lançamento em dobro. O segundo recebe 409.
        builder.Property(t => t.RecurrenceId).IsConcurrencyToken();

        // Uma ocorrência gera um lançamento só, para sempre: o índice conta também os excluídos, para o que
        // a pessoa excluiu nunca voltar (docs/fase-2.md, 2.14, A3 e regra 5).
        builder.HasIndex(t => new { t.RecurrenceId, t.OccurrenceDate })
            .IsUnique()
            .HasFilter("recurrence_id IS NOT NULL")
            .HasDatabaseName(RecurrenceOccurrenceIndex);

        // Estorno ligado à compra que devolve (docs/fase-2.md, 2.5, regra 7). Só estorno tem vínculo.
        builder.HasOne<Transaction>().WithMany().HasForeignKey(t => t.RefundedTransactionId)
            .OnDelete(DeleteBehavior.Restrict);

        // Índices de docs/fase-1.md, mais PurchaseDate, que é o período da listagem.
        builder.HasIndex(t => new { t.UserId, t.SettlementDate });
        builder.HasIndex(t => new { t.UserId, t.AccountId, t.SettlementDate });
        builder.HasIndex(t => new { t.UserId, t.CategoryId });
        builder.HasIndex(t => new { t.UserId, t.PurchaseDate });
        builder.HasIndex(t => t.TransferPairId);
    }
}
