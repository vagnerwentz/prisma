using CsCheck;
using Prisma.Domain.Accounts;
using Prisma.Domain.Categories;
using Prisma.Domain.Statements;
using Prisma.Domain.Transactions;
using Shouldly;

namespace Prisma.Domain.Tests.Transactions;

// Valores esperados de docs/fase-1.md, 2.1 e 2.2, com cartão que fecha dia 5 e vence dia 12.
public sealed class CardPurchaseTests
{
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly DateOnly March10 = new(2026, 3, 10);

    private static readonly Account Card =
        Account.Create(UserId, "Visa", AccountType.CreditCard, 0, 5, 12, null).Value;

    private static readonly Account Checking =
        Account.Create(UserId, "Itaú", AccountType.Checking, 0, null, null, null).Value;

    private static readonly Category Electronics =
        Category.Create(UserId, "Eletrônicos", TransactionType.Expense, null, null, null).Value;

    private static readonly Category Salary =
        Category.Create(UserId, "Salário", TransactionType.Income, null, null, null).Value;

    private static Result<CardPurchaseResult> Purchase(
        long total = 100000, int installments = 10, Account? account = null,
        TransactionType type = TransactionType.Expense, PaymentMethod method = PaymentMethod.Credit,
        Category? category = null, string? description = "Notebook", DateOnly? date = null,
        IReadOnlyCollection<Statement>? existing = null) =>
        CardPurchase.Create(UserId, account ?? Card, type, method, total, installments, date ?? March10,
            category ?? Electronics, description, existing ?? []);

    private static void ShouldFailWith(Result<CardPurchaseResult> result, string message)
    {
        result.IsSuccess.ShouldBeFalse();
        result.Error.Type.ShouldBe(ErrorType.Validation);
        result.Error.Message.ShouldBe(message);
    }

    [Fact]
    public void Ten_installments_settle_on_the_due_date_of_ten_consecutive_statements()
    {
        var result = Purchase().Value;

        // Compra em 10/03, depois do fechamento de 05/03: a 1ª parcela vai para a fatura de abril.
        result.Installments.Select(t => t.SettlementDate).ShouldBe([
            new DateOnly(2026, 4, 12), new DateOnly(2026, 5, 12), new DateOnly(2026, 6, 12),
            new DateOnly(2026, 7, 12), new DateOnly(2026, 8, 12), new DateOnly(2026, 9, 12),
            new DateOnly(2026, 10, 12), new DateOnly(2026, 11, 12), new DateOnly(2026, 12, 12),
            new DateOnly(2027, 1, 12),
        ]);
        result.OpenedStatements.Select(s => s.Reference).ShouldBe([
            "2026-04", "2026-05", "2026-06", "2026-07", "2026-08",
            "2026-09", "2026-10", "2026-11", "2026-12", "2027-01",
        ]);
    }

    [Fact]
    public void Each_installment_points_to_its_statement_and_settles_on_its_due_date()
    {
        var result = Purchase().Value;

        foreach (var (installment, statement) in result.Installments.Zip(result.OpenedStatements))
        {
            installment.StatementId.ShouldBe(statement.Id);
            installment.SettlementDate.ShouldBe(statement.DueDate);
            statement.AccountId.ShouldBe(Card.Id);
            statement.UserId.ShouldBe(UserId);
        }
    }

    [Fact]
    public void Installments_share_purchase_date_purchase_and_are_numbered()
    {
        var result = Purchase().Value;

        result.Purchase.ShouldNotBeNull();
        result.Purchase.TotalAmountCents.ShouldBe(100000);
        result.Purchase.InstallmentCount.ShouldBe(10);
        result.Purchase.PurchaseDate.ShouldBe(March10);
        result.Purchase.AccountId.ShouldBe(Card.Id);
        result.Purchase.Description.ShouldBe("Notebook");

        result.Installments.ShouldAllBe(t => t.PurchaseDate == March10);
        result.Installments.ShouldAllBe(t => t.InstallmentPurchaseId == result.Purchase.Id);
        result.Installments.Select(t => t.InstallmentNumber).ShouldBe([1, 2, 3, 4, 5, 6, 7, 8, 9, 10]);
        result.Installments.ShouldAllBe(t =>
            t.Type == TransactionType.Expense && t.Method == PaymentMethod.Credit
            && t.AccountId == Card.Id && t.CategoryId == Electronics.Id && t.Description == "Notebook"
            && t.Source == TransactionSource.Manual && t.UserId == UserId);
    }

    [Fact]
    public void Remainder_goes_to_the_first_installments() =>
        Purchase(total: 10000, installments: 3).Value.Installments.Select(t => t.AmountCents)
            .ShouldBe([3334, 3333, 3333]);

    [Fact]
    public void Single_payment_has_no_installment_purchase()
    {
        var result = Purchase(total: 4590, installments: 1).Value;

        result.Purchase.ShouldBeNull();
        var transaction = result.Installments.ShouldHaveSingleItem();
        transaction.AmountCents.ShouldBe(4590);
        transaction.InstallmentNumber.ShouldBeNull();
        transaction.InstallmentPurchaseId.ShouldBeNull();
        transaction.SettlementDate.ShouldBe(new DateOnly(2026, 4, 12));
    }

    [Fact]
    public void Existing_statements_are_reused_instead_of_opened_again()
    {
        var april = Statement.Open(UserId, Card.Id, new StatementDates("2026-04", new DateOnly(2026, 4, 5), new DateOnly(2026, 4, 12)));

        var result = Purchase(installments: 3, existing: [april]).Value;

        result.Installments[0].StatementId.ShouldBe(april.Id);
        result.OpenedStatements.Select(s => s.Reference).ShouldBe(["2026-05", "2026-06"]);
    }

    [Fact]
    public void Edited_statement_dates_decide_the_statement_and_the_settlement()
    {
        // Fechamento de março adiado para 11/03: a compra de 10/03 ainda entra em março.
        var march = Statement.Open(UserId, Card.Id, new StatementDates("2026-03", new DateOnly(2026, 3, 5), new DateOnly(2026, 3, 12)));
        march.EditDates(new DateOnly(2026, 3, 11), new DateOnly(2026, 3, 18));

        var result = Purchase(installments: 1, existing: [march]).Value;

        result.Installments[0].StatementId.ShouldBe(march.Id);
        result.Installments[0].SettlementDate.ShouldBe(new DateOnly(2026, 3, 18));
        result.OpenedStatements.ShouldBeEmpty();
    }

    [Fact]
    public void Requires_a_credit_card_account() =>
        ShouldFailWith(Purchase(account: Checking), "Compra no cartão exige uma conta de cartão de crédito.");

    [Theory]
    [InlineData(0)]
    [InlineData(25)]
    public void Installments_go_from_1_to_24(int installments) =>
        ShouldFailWith(Purchase(installments: installments), "O número de parcelas deve estar entre 1 e 24.");

    [Fact]
    public void Accepts_24_installments() =>
        Purchase(installments: 24).Value.Installments.Count.ShouldBe(24);

    [Fact]
    public void Amount_must_be_positive() =>
        ShouldFailWith(Purchase(total: 0, installments: 1), "O valor deve ser maior que zero.");

    [Fact]
    public void Every_installment_needs_at_least_one_cent() =>
        ShouldFailWith(Purchase(total: 9, installments: 10), "O valor total deve ter ao menos 1 centavo por parcela.");

    [Fact]
    public void Card_accepts_only_expenses_for_now() =>
        ShouldFailWith(Purchase(type: TransactionType.Income, category: Salary), "O cartão aceita apenas despesas.");

    [Fact]
    public void Card_purchase_uses_the_credit_method() =>
        ShouldFailWith(Purchase(method: PaymentMethod.Pix), "Compra no cartão usa o meio de pagamento crédito.");

    [Fact]
    public void Category_must_be_an_expense_category() =>
        ShouldFailWith(Purchase(category: Salary),
            "A categoria deve ser do mesmo tipo da transação (receita ou despesa).");

    [Fact]
    public void Description_has_at_most_200_characters() =>
        ShouldFailWith(Purchase(description: new string('d', 201)), "A descrição deve ter no máximo 200 caracteres.");

    [Fact]
    public void Installment_of_a_purchase_cannot_be_deleted_or_restored_alone()
    {
        var error = Purchase().Value.Installments[2].CheckCanChangeIndividually();

        error.ShouldNotBeNull();
        error.Type.ShouldBe(ErrorType.Conflict);
        error.Message.ShouldBe("Esta parcela faz parte de uma compra parcelada. Exclua a compra inteira.");
    }

    // Editar compra no cartão é a etapa 1.9b. Até lá, a edição simples não pode transformar uma
    // parcela em lançamento de conta corrente, deixando-a presa à fatura e à compra.
    [Fact]
    public void Card_installment_cannot_be_edited_as_a_simple_transaction()
    {
        var installment = Purchase().Value.Installments[0];

        var result = installment.UpdateSimple(Checking, TransactionType.Expense, 100, March10, null, PaymentMethod.Pix, null);

        result.IsSuccess.ShouldBeFalse();
        result.Error.Message.ShouldBe("Lançamento em cartão de crédito usa a compra no cartão.");
        installment.AccountId.ShouldBe(Card.Id);
    }

    [Fact]
    public void Single_payment_can_be_deleted_alone() =>
        Purchase(installments: 1).Value.Installments[0].CheckCanChangeIndividually().ShouldBeNull();

    // CLAUDE.md, regra 2 aplicada ao parcelamento: nenhuma combinação perde centavo nem gera
    // parcela zerada, e cada parcela cai numa fatura posterior à anterior.
    [Fact]
    public void Installments_always_add_up_to_the_total() =>
        Gen.Select(Gen.Int[1, InstallmentPurchase.MaxInstallments], Gen.Long[0, 10_000_000], Gen.Int[0, 3650])
            .Sample((installments, extra, days) =>
            {
                var total = installments + extra;
                var result = Purchase(total: total, installments: installments, date: new DateOnly(2024, 1, 1).AddDays(days)).Value;
                var dueDates = result.Installments.Select(t => t.SettlementDate).ToList();
                return result.Installments.Sum(t => t.AmountCents) == total
                    && result.Installments.All(t => t.AmountCents > 0)
                    && result.Installments.Count == installments
                    && dueDates.Zip(dueDates.Skip(1)).All(pair => pair.Second > pair.First);
            });
}
