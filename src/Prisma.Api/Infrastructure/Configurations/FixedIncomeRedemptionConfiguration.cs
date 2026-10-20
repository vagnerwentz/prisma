using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Prisma.Api.Infrastructure.Auth;
using Prisma.Domain.FixedIncome;

namespace Prisma.Api.Infrastructure.Configurations;

// Resgates de aportes em renda fixa (docs/renda-fixa-resgate.md, 2). Da pessoa: dono, filtro e soft delete. Cada linha é um fato
// realizado e congelado (principal, rendimento e imposto), então as contas viram constraints: o banco recusa o que o domínio nunca grava.
public sealed class FixedIncomeRedemptionConfiguration : IEntityTypeConfiguration<FixedIncomeRedemption>
{
    public const string RequestIndex = "ux_fixed_income_redemptions_user_request";

    public void Configure(EntityTypeBuilder<FixedIncomeRedemption> builder)
    {
        var sources = string.Join(", ", Enum.GetNames<RedemptionTaxSource>().Select(k => $"'{k}'"));
        builder.ToTable("fixed_income_redemptions", table =>
        {
            table.HasCheckConstraint("ck_fixed_income_redemptions_gross",
                $"gross_cents >= 1 AND gross_cents <= {FixedIncomeEngine.MaxPrincipalCents * 1_000L}");
            table.HasCheckConstraint("ck_fixed_income_redemptions_split",
                "principal_cents >= 0 AND yield_cents >= 0 AND principal_cents + yield_cents = gross_cents");
            // Imposto nunca passa do rendimento (o IOF é limitado a ele e o IR incide sobre o que sobra) e o líquido é o bruto menos ele.
            table.HasCheckConstraint("ck_fixed_income_redemptions_tax",
                "tax_cents >= 0 AND tax_cents <= yield_cents AND net_cents = gross_cents - tax_cents");
            table.HasCheckConstraint("ck_fixed_income_redemptions_tax_source", $"tax_source IN ({sources})");
            // Calculado: IOF, IR e a regra vêm juntos e somam o total. Informado: só o total, sem separar.
            table.HasCheckConstraint("ck_fixed_income_redemptions_tax_detail",
                "(tax_source = 'Estimated' AND iof_cents >= 0 AND ir_cents >= 0 AND iof_cents + ir_cents = tax_cents AND tax_rule_version IS NOT NULL) " +
                "OR (tax_source = 'Informed' AND iof_cents IS NULL AND ir_cents IS NULL AND tax_rule_version IS NULL)");
        });

        builder.Property(r => r.TaxSource).HasConversion<string>().HasMaxLength(20);
        builder.Property(r => r.TaxRuleVersion).HasMaxLength(60);

        builder.HasOne<AppUser>().WithMany().HasForeignKey(r => r.UserId).OnDelete(DeleteBehavior.Restrict);

        // O resgate é parte do aporte: quando o aporte sai de vez do banco (limpeza dos excluídos, A.3), os resgates vão com ele.
        builder.HasOne<FixedIncomeDeposit>().WithMany().HasForeignKey(r => r.DepositId).OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(r => new { r.DepositId, r.RedemptionDate });

        // O par da transferência do principal e a receita do rendimento. Sem chave estrangeira: são linhas de transação, e podem ser
        // excluídas à parte (como o financiamento do aporte).
        builder.HasIndex(r => new { r.UserId, r.TransferPairId });

        // Contando também o excluído: o mesmo pedido repetido nunca cria um segundo resgate, nem depois de o primeiro sumir.
        builder.HasIndex(r => new { r.UserId, r.ClientRequestId }).IsUnique().HasDatabaseName(RequestIndex);
    }
}
