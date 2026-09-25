using Prisma.Domain.Dashboard;
using Shouldly;

namespace Prisma.Domain.Tests.Dashboard;

// Etapa 2.8 (docs/fase-2.md, 2.6), com o exemplo da regra: outubro de 2026 no Visa.
public sealed class InheritedInstallmentsTests
{
    private static readonly Guid Visa = Guid.NewGuid();

    private static InstallmentCharge Charge(string description, int number, int count, long cents, string purchaseDate) =>
        new(Guid.NewGuid(), Guid.NewGuid(), description, null, Visa, number, count, cents, DateOnly.Parse(purchaseDate));

    // As parcelas de compras parceladas que vencem em outubro, inclusive a primeira do Tênis.
    private static readonly InstallmentCharge Tv = Charge("TV", 4, 10, 40000, "2026-06-10");
    private static readonly InstallmentCharge Ticket = Charge("Passagem", 2, 6, 60000, "2026-08-20");
    private static readonly InstallmentCharge Groceries = Charge("Mercado", 2, 3, 10000, "2026-08-02");
    private static readonly InstallmentCharge Sneakers = Charge("Tênis", 1, 3, 20000, "2026-09-15");

    [Fact]
    public void Spec_example_inherited_is_installment_two_or_later_largest_first()
    {
        var inherited = InheritedInstallments.Of(monthExpenseCents: 155000, [Tv, Sneakers, Groceries, Ticket]);

        inherited.InheritedCents.ShouldBe(110000);
        inherited.DecidedInMonthCents.ShouldBe(45000);
        inherited.Charges.Select(c => c.Description).ShouldBe(["Passagem", "TV", "Mercado"]);
    }

    [Fact]
    public void The_first_installment_is_a_purchase_of_the_month()
    {
        var inherited = InheritedInstallments.Of(20000, [Sneakers]);

        inherited.InheritedCents.ShouldBe(0);
        inherited.Charges.ShouldBeEmpty();
        inherited.DecidedInMonthCents.ShouldBe(20000);
    }

    [Fact]
    public void Ties_go_to_the_oldest_purchase_then_to_the_description()
    {
        var older = Charge("Sofá", 3, 5, 10000, "2026-05-01");
        var newerB = Charge("Bicicleta", 2, 4, 10000, "2026-07-01");
        var newerA = Charge("Armário", 2, 4, 10000, "2026-07-01");

        InheritedInstallments.Of(30000, [newerB, newerA, older]).Charges.Select(c => c.Description)
            .ShouldBe(["Sofá", "Armário", "Bicicleta"]);
    }

    [Fact]
    public void Without_enough_spending_to_cover_the_inherited_there_is_no_split()
    {
        // Estornos grandes: o "Saiu" (R$ 800,00) fica abaixo do herdado (R$ 1.000,00).
        var inherited = InheritedInstallments.Of(80000, [Tv, Ticket]);

        inherited.InheritedCents.ShouldBe(100000);
        inherited.DecidedInMonthCents.ShouldBeNull();
    }
}
