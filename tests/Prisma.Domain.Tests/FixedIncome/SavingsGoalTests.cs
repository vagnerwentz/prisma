using Prisma.Domain.FixedIncome;
using Shouldly;

namespace Prisma.Domain.Tests.FixedIncome;

// O objetivo é uma caixa (docs/renda-fixa.md, 2.2): nome, meta opcional e o percentual do CDI que pré-preenche o formulário
// de aporte. Não guarda saldo nem rendimento: isso sai dos aportes.
public sealed class SavingsGoalTests
{
    private static readonly Guid User = Guid.NewGuid();

    private static SavingsGoal Goal(string name = "Casamento", string? description = null, long? target = null, long? percent = null) =>
        SavingsGoal.Create(User, name, description, target, percent).Value;

    [Fact]
    public void A_goal_with_only_a_name_is_valid()
    {
        var goal = Goal();

        goal.Name.ShouldBe("Casamento");
        goal.UserId.ShouldBe(User);
        (goal.Description, goal.TargetCents, goal.DefaultIndexPercentE4).ShouldBe((null, null, null));
    }

    [Fact]
    public void The_name_and_the_description_are_trimmed_and_an_empty_description_is_none()
    {
        var goal = Goal("  Reserva de emergência  ", "   ");

        goal.Name.ShouldBe("Reserva de emergência");
        goal.Description.ShouldBeNull();
        Goal("Viagem", "  duas semanas  ").Description.ShouldBe("duas semanas");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void A_name_is_required(string name) =>
        SavingsGoal.Create(User, name, null, null, null).Error!.Type.ShouldBe(ErrorType.Validation);

    [Fact]
    public void The_name_has_a_limit()
    {
        SavingsGoal.Create(User, new string('a', SavingsGoal.NameMaxLength), null, null, null).IsSuccess.ShouldBeTrue();
        SavingsGoal.Create(User, new string('a', SavingsGoal.NameMaxLength + 1), null, null, null).IsSuccess.ShouldBeFalse();
    }

    [Fact]
    public void The_description_has_a_limit()
    {
        SavingsGoal.Create(User, "Casamento", new string('a', SavingsGoal.DescriptionMaxLength), null, null).IsSuccess.ShouldBeTrue();
        SavingsGoal.Create(User, "Casamento", new string('a', SavingsGoal.DescriptionMaxLength + 1), null, null).IsSuccess.ShouldBeFalse();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void A_target_must_be_positive(long target) =>
        SavingsGoal.Create(User, "Casamento", null, target, null).IsSuccess.ShouldBeFalse();

    [Fact]
    public void A_target_of_one_cent_and_the_ceiling_are_accepted()
    {
        SavingsGoal.Create(User, "Casamento", null, 1, null).IsSuccess.ShouldBeTrue();
        SavingsGoal.Create(User, "Casamento", null, FixedIncomeEngine.MaxPrincipalCents, null).IsSuccess.ShouldBeTrue();
        SavingsGoal.Create(User, "Casamento", null, FixedIncomeEngine.MaxPrincipalCents + 1, null).IsSuccess.ShouldBeFalse();
    }

    [Theory]
    [InlineData(IndexPercent.Min - 1, false)]
    [InlineData(IndexPercent.Min, true)]
    [InlineData(1_000_000, true)]
    [InlineData(IndexPercent.Max, true)]
    [InlineData(IndexPercent.Max + 1, false)]
    [InlineData(0, false)]
    [InlineData(-1_000_000, false)]
    public void The_default_percent_follows_the_range_of_the_engine(long percent, bool valid) =>
        SavingsGoal.Create(User, "Casamento", null, null, percent).IsSuccess.ShouldBe(valid);

    [Fact]
    public void Update_replaces_everything_and_a_refused_update_changes_nothing()
    {
        var goal = Goal("Casamento", "festa", 5_000_000, 1_050_000);

        goal.Update("Viagem", null, null, null).ShouldBeNull();
        (goal.Name, goal.Description, goal.TargetCents, goal.DefaultIndexPercentE4).ShouldBe(("Viagem", null, null, null));

        goal.Update("", "x", 1, 1_000_000).ShouldNotBeNull();
        (goal.Name, goal.Description, goal.TargetCents, goal.DefaultIndexPercentE4).ShouldBe(("Viagem", null, null, null));
    }
}
