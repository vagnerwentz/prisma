using Prisma.Domain.Accounts;
using Prisma.Domain.Categories;
using Prisma.Domain.Transactions;
using Shouldly;

namespace Prisma.Domain.Tests.Transactions;

// Etapa 1.14: "Desfazer" a exclusão de uma compra parcelada devolve a compra com as parcelas
// excluídas junto com ela, e só se a soma delas continuar igual ao total (CLAUDE.md, regra 2).
public sealed class CardPurchaseRestoreTests
{
    private static readonly Guid UserId = Guid.NewGuid();

    private static readonly Account Card =
        Account.Create(UserId, "Visa", AccountType.CreditCard, 0, 5, 12, null).Value;

    private static readonly Category Electronics =
        Category.Create(UserId, "Eletrônicos", TransactionType.Expense, null, null, null).Value;

    private const string MismatchMessage =
        "As parcelas desta compra não fecham com o total; não é possível restaurá-la.";

    private static CardPurchaseResult Buy(long total = 10000, int count = 3) =>
        CardPurchase.Create(UserId, Card, TransactionType.Expense, PaymentMethod.Credit, total, count,
            new DateOnly(2026, 3, 10), Electronics, "Notebook", []).Value;

    private static HashSet<Guid> CategoryExists => [Electronics.Id];

    [Fact]
    public void Restoring_with_every_installment_succeeds_and_keeps_the_category()
    {
        var bought = Buy();

        var result = CardPurchase.Restore(bought.Purchase!, bought.Installments, CategoryExists);

        result.IsSuccess.ShouldBeTrue();
        bought.Installments.ShouldAllBe(t => t.CategoryId == Electronics.Id);
    }

    [Fact]
    public void A_missing_installment_blocks_the_restore()
    {
        var bought = Buy();

        var result = CardPurchase.Restore(bought.Purchase!, bought.Installments.Take(2).ToList(), CategoryExists);

        result.Error!.Type.ShouldBe(ErrorType.Conflict);
        result.Error!.Message.ShouldBe(MismatchMessage);
    }

    [Fact]
    public void An_extra_installment_with_the_same_number_blocks_the_restore()
    {
        // Ex.: a parcela 3 removida por uma edição e a parcela 3 atual, se viessem juntas.
        var bought = Buy();
        var other = Buy();
        var mixed = bought.Installments.Append(other.Installments[2]).ToList();

        var result = CardPurchase.Restore(bought.Purchase!, mixed, CategoryExists);

        result.Error!.Message.ShouldBe(MismatchMessage);
    }

    [Fact]
    public void Installments_that_do_not_sum_to_the_total_block_the_restore()
    {
        // Três parcelas numeradas 1..3, mas de outra compra com total diferente.
        var bought = Buy(total: 10000);
        var other = Buy(total: 9000);

        var result = CardPurchase.Restore(bought.Purchase!, other.Installments, CategoryExists);

        result.Error!.Message.ShouldBe(MismatchMessage);
    }

    [Fact]
    public void A_blocked_restore_changes_nothing()
    {
        var bought = Buy();

        CardPurchase.Restore(bought.Purchase!, bought.Installments.Take(2).ToList(), new HashSet<Guid>());

        bought.Installments.ShouldAllBe(t => t.CategoryId == Electronics.Id);
    }

    [Fact]
    public void Installments_whose_category_was_deleted_come_back_without_category()
    {
        var bought = Buy();

        var result = CardPurchase.Restore(bought.Purchase!, bought.Installments, new HashSet<Guid>());

        result.IsSuccess.ShouldBeTrue();
        bought.Installments.ShouldAllBe(t => t.CategoryId == null);
    }
}
