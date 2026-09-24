using Prisma.Domain.Categories;
using Prisma.Domain.Transactions;
using Shouldly;

namespace Prisma.Domain.Tests.Categories;

public sealed class CategoryTests
{
    private static readonly Guid UserId = Guid.NewGuid();

    private static Category Root(TransactionType type = TransactionType.Expense) =>
        Category.Create(UserId, "Alimentação", type, null, null, null).Value;

    private static void ShouldFailWith<T>(Result<T> result, string message)
    {
        result.IsSuccess.ShouldBeFalse();
        result.Error.Type.ShouldBe(ErrorType.Validation);
        result.Error.Message.ShouldBe(message);
    }

    [Fact]
    public void Creates_a_root_category()
    {
        var category = Category.Create(UserId, "  Alimentação ", TransactionType.Expense, null, "utensils", "#FF8800").Value;

        category.UserId.ShouldBe(UserId);
        category.Name.ShouldBe("Alimentação");
        category.Type.ShouldBe(TransactionType.Expense);
        category.ParentCategoryId.ShouldBeNull();
        category.Icon.ShouldBe("utensils");
        category.Color.ShouldBe("#FF8800");
    }

    [Fact]
    public void Creates_a_subcategory_under_a_root()
    {
        var parent = Root();

        var sub = Category.Create(UserId, "Mercado", TransactionType.Expense, parent, null, null).Value;

        sub.ParentCategoryId.ShouldBe(parent.Id);
        sub.Type.ShouldBe(TransactionType.Expense);
    }

    [Fact]
    public void Transfer_is_not_a_category_type() =>
        ShouldFailWith(Category.Create(UserId, "Transferências", TransactionType.Transfer, null, null, null),
            "A categoria deve ser de receita ou de despesa.");

    [Fact]
    public void Subcategory_must_have_the_parent_type() =>
        ShouldFailWith(Category.Create(UserId, "Bônus", TransactionType.Income, Root(TransactionType.Expense), null, null),
            "A subcategoria deve ter o mesmo tipo da categoria pai.");

    [Fact]
    public void Only_two_levels_are_allowed()
    {
        var sub = Category.Create(UserId, "Mercado", TransactionType.Expense, Root(), null, null).Value;

        ShouldFailWith(Category.Create(UserId, "Hortifruti", TransactionType.Expense, sub, null, null),
            "Subcategoria não pode ter subcategorias.");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void Name_is_required(string? name) =>
        ShouldFailWith(Category.Create(UserId, name!, TransactionType.Expense, null, null, null),
            "Informe o nome da categoria.");

    [Fact]
    public void Name_has_at_most_50_characters()
    {
        Category.Create(UserId, new string('a', 50), TransactionType.Expense, null, null, null).IsSuccess.ShouldBeTrue();
        ShouldFailWith(Category.Create(UserId, new string('a', 51), TransactionType.Expense, null, null, null),
            "O nome da categoria deve ter no máximo 50 caracteres.");
    }

    [Theory]
    [InlineData("#ff8800")]
    [InlineData("#00AAFF")]
    public void Accepts_hex_colors(string color) =>
        Category.Create(UserId, "Lazer", TransactionType.Expense, null, null, color).Value.Color.ShouldBe(color);

    [Theory]
    [InlineData("vermelho")]
    [InlineData("#FFF")]
    [InlineData("FF8800")]
    [InlineData("#GG8800")]
    public void Rejects_colors_that_are_not_RRGGBB(string color) =>
        ShouldFailWith(Category.Create(UserId, "Lazer", TransactionType.Expense, null, null, color),
            "A cor deve estar no formato #RRGGBB.");

    [Fact]
    public void Icon_has_at_most_50_characters() =>
        ShouldFailWith(Category.Create(UserId, "Lazer", TransactionType.Expense, null, new string('i', 51), null),
            "O ícone deve ter no máximo 50 caracteres.");

    [Fact]
    public void Blank_icon_and_color_become_null()
    {
        var category = Category.Create(UserId, "Lazer", TransactionType.Expense, null, "  ", "").Value;

        category.Icon.ShouldBeNull();
        category.Color.ShouldBeNull();
    }

    [Fact]
    public void Update_changes_name_icon_and_color()
    {
        var category = Root();

        category.Update(" Comida ", "pizza", "#123456").IsSuccess.ShouldBeTrue();

        category.Name.ShouldBe("Comida");
        category.Icon.ShouldBe("pizza");
        category.Color.ShouldBe("#123456");
    }

    [Fact]
    public void Failed_update_leaves_the_category_untouched()
    {
        var category = Category.Create(UserId, "Alimentação", TransactionType.Expense, null, "utensils", "#FF8800").Value;

        ShouldFailWith(category.Update("Comida", "pizza", "azul"), "A cor deve estar no formato #RRGGBB.");

        category.Name.ShouldBe("Alimentação");
        category.Icon.ShouldBe("utensils");
        category.Color.ShouldBe("#FF8800");
    }

    [Fact]
    public void Category_without_subcategories_or_transactions_can_be_deleted() =>
        Root().CheckCanDelete(activeSubcategoryCount: 0, activeTransactionCount: 0).ShouldBeNull();

    [Fact]
    public void Category_with_transactions_cannot_be_deleted()
    {
        var error = Root().CheckCanDelete(activeSubcategoryCount: 0, activeTransactionCount: 3);

        error.ShouldNotBeNull();
        error.Type.ShouldBe(ErrorType.Conflict);
        error.Message.ShouldBe("Esta categoria tem transações. Mova as transações para outra categoria antes de excluir.");
    }

    [Fact]
    public void Category_with_subcategories_cannot_be_deleted()
    {
        var error = Root().CheckCanDelete(activeSubcategoryCount: 2, activeTransactionCount: 0);

        error.ShouldNotBeNull();
        error.Type.ShouldBe(ErrorType.Conflict);
        error.Message.ShouldBe("Esta categoria tem subcategorias. Exclua as subcategorias antes.");
    }
}
