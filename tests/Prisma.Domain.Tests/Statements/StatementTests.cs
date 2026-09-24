using Prisma.Domain.Statements;
using Shouldly;

namespace Prisma.Domain.Tests.Statements;

public sealed class StatementTests
{
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly Guid AccountId = Guid.NewGuid();
    private static readonly StatementDates March = new("2026-03", new DateOnly(2026, 3, 5), new DateOnly(2026, 3, 12));

    [Fact]
    public void Opens_with_the_calculated_dates()
    {
        var statement = Statement.Open(UserId, AccountId, March);

        statement.UserId.ShouldBe(UserId);
        statement.AccountId.ShouldBe(AccountId);
        statement.Dates.ShouldBe(March);
        statement.IsPaid.ShouldBeFalse();
        statement.DatesEditedManually.ShouldBeFalse();
    }

    [Fact]
    public void Editing_dates_overrides_them_and_marks_the_edit()
    {
        var statement = Statement.Open(UserId, AccountId, March);

        var result = statement.EditDates(new DateOnly(2026, 3, 7), new DateOnly(2026, 3, 14));

        result.IsSuccess.ShouldBeTrue();
        statement.Dates.ShouldBe(new StatementDates("2026-03", new DateOnly(2026, 3, 7), new DateOnly(2026, 3, 14)));
        statement.DatesEditedManually.ShouldBeTrue();
    }

    [Fact]
    public void Due_date_cannot_be_before_closing_date()
    {
        var statement = Statement.Open(UserId, AccountId, March);

        var result = statement.EditDates(new DateOnly(2026, 3, 12), new DateOnly(2026, 3, 11));

        result.IsSuccess.ShouldBeFalse();
        result.Error.Type.ShouldBe(ErrorType.Validation);
        result.Error.Message.ShouldBe("O vencimento não pode ser antes do fechamento.");
        statement.Dates.ShouldBe(March);
        statement.DatesEditedManually.ShouldBeFalse();
    }
}
