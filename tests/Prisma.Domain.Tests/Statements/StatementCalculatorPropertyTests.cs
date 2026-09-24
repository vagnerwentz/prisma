using CsCheck;
using Prisma.Domain.Statements;

namespace Prisma.Domain.Tests.Statements;

// Invariantes da seção 2.1 verificadas para quaisquer datas e dias de fechamento/vencimento.
public sealed class StatementCalculatorPropertyTests
{
    private static readonly StatementDates[] None = [];

    private static readonly Gen<(DateOnly Purchase, int ClosingDay, int DueDay)> Card =
        Gen.Select(
            Gen.Int[0, 365 * 30].Select(offset => new DateOnly(2000, 1, 1).AddDays(offset)),
            Gen.Int[1, 31],
            Gen.Int[1, 31]);

    [Fact]
    public void Purchase_enters_the_first_statement_closing_on_or_after_it() =>
        Card.Sample((purchase, closingDay, dueDay) =>
        {
            var statement = StatementCalculator.ForPurchase(purchase, closingDay, dueDay, None);
            return statement.ClosingDate >= purchase
                && statement.ClosingDate <= purchase.AddMonths(1);
        });

    [Fact]
    public void Due_date_is_never_before_closing_date() =>
        Card.Sample((purchase, closingDay, dueDay) =>
        {
            var statement = StatementCalculator.ForPurchase(purchase, closingDay, dueDay, None);
            return statement.DueDate >= statement.ClosingDate;
        });

    [Fact]
    public void Consecutive_installments_fall_in_strictly_later_statements() =>
        Gen.Select(Card, Gen.Int[1, 23]).Sample((card, installment) =>
        {
            var (purchase, closingDay, dueDay) = card;
            var current = StatementCalculator.ForInstallment(purchase, installment, closingDay, dueDay, None);
            var next = StatementCalculator.ForInstallment(purchase, installment + 1, closingDay, dueDay, None);
            return next.ClosingDate > current.ClosingDate
                && next.DueDate > current.DueDate
                && string.CompareOrdinal(next.Reference, current.Reference) > 0;
        });

    [Fact]
    public void Reference_is_the_due_month() =>
        Card.Sample((purchase, closingDay, dueDay) =>
        {
            var statement = StatementCalculator.ForPurchase(purchase, closingDay, dueDay, None);
            return statement.Reference == statement.DueDate.ToString("yyyy-MM");
        });
}
