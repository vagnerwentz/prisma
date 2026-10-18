using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Prisma.Api.Infrastructure.Auth;
using Prisma.Domain.FixedIncome;

namespace Prisma.Api.Infrastructure.Configurations;

// Aportes em renda fixa (docs/renda-fixa.md, 2.3). Da pessoa: dono, filtro e soft delete. O objetivo é opcional: o aporte
// pode ficar solto e mudar de caixa. Não há coluna de rendimento, saldo ou imposto: tudo se calcula.
public sealed class FixedIncomeDepositConfiguration : IEntityTypeConfiguration<FixedIncomeDeposit>
{
    public const string RequestIndex = "ux_fixed_income_deposits_user_request";

    public void Configure(EntityTypeBuilder<FixedIncomeDeposit> builder)
    {
        var products = string.Join(", ", Enum.GetNames<FixedIncomeProductKind>().Select(k => $"'{k}'"));
        var indexes = string.Join(", ", Enum.GetNames<FixedIncomeIndexKind>().Select(k => $"'{k}'"));
        var liquidities = string.Join(", ", Enum.GetNames<DepositLiquidity>().Select(k => $"'{k}'"));
        builder.ToTable("fixed_income_deposits", table =>
        {
            table.HasCheckConstraint("ck_fixed_income_deposits_amount",
                $"amount_cents >= 1 AND amount_cents <= {FixedIncomeEngine.MaxPrincipalCents}");
            table.HasCheckConstraint("ck_fixed_income_deposits_percent", "index_percent_e4 BETWEEN 100 AND 5000000");
            table.HasCheckConstraint("ck_fixed_income_deposits_maturity", "maturity_date IS NULL OR maturity_date > deposit_date");
            table.HasCheckConstraint("ck_fixed_income_deposits_product_kind", $"product_kind IN ({products})");
            table.HasCheckConstraint("ck_fixed_income_deposits_index_kind", $"index_kind IN ({indexes})");
            table.HasCheckConstraint("ck_fixed_income_deposits_liquidity", $"liquidity IN ({liquidities})");
        });

        // Texto, não inteiro: legível no banco e imune a reordenação do enum.
        builder.Property(d => d.ProductKind).HasConversion<string>().HasMaxLength(20);
        builder.Property(d => d.IndexKind).HasConversion<string>().HasMaxLength(20);
        builder.Property(d => d.Liquidity).HasConversion<string>().HasMaxLength(20);
        builder.Property(d => d.Note).HasMaxLength(FixedIncomeDeposit.NoteMaxLength);

        // Editar enquanto outro pedido edita, move ou exclui o mesmo aporte dá 409 (CLAUDE.md, seção 6).
        builder.Property<uint>("Version").IsRowVersion();

        builder.HasOne<AppUser>().WithMany().HasForeignKey(d => d.UserId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<SavingsGoal>().WithMany().HasForeignKey(d => d.GoalId).OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(d => new { d.UserId, d.GoalId, d.DepositDate });

        // O par da transferência que financiou o aporte. Sem chave estrangeira: o par são duas linhas de transação.
        builder.HasIndex(d => new { d.UserId, d.TransferPairId });

        // Contando também o excluído: o mesmo pedido repetido nunca cria um segundo aporte, nem depois de o primeiro sumir.
        builder.HasIndex(d => new { d.UserId, d.ClientRequestId }).IsUnique().HasDatabaseName(RequestIndex);
    }
}
