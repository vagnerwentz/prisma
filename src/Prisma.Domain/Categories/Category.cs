using System.Text.RegularExpressions;
using Prisma.Domain.Transactions;

namespace Prisma.Domain.Categories;

// Dois níveis apenas: categoria (ParentCategoryId nulo) e subcategoria.
public sealed partial class Category : Entity
{
    public const int NameMaxLength = 50;
    public const int IconMaxLength = 50;

    public static readonly Error DuplicateName =
        new(ErrorType.Conflict, "Já existe uma categoria com este nome neste nível.");

    private Category() { }

    public string Name { get; private set; } = "";
    public TransactionType Type { get; private set; }
    public Guid? ParentCategoryId { get; private set; }
    public string? Icon { get; private set; }
    public string? Color { get; private set; }

    // Chave estável da categoria padrão de onde esta veio (docs/fase-2.md, 2.11), como
    // "expense.food.groceries". Nula na categoria que a pessoa criou. Não muda quando ela renomeia.
    public string? TemplateKey { get; private set; }

    public static Result<Category> Create(
        Guid userId, string name, TransactionType type, Category? parent, string? icon, string? color)
    {
        if (type is not (TransactionType.Income or TransactionType.Expense))
            return Invalid("A categoria deve ser de receita ou de despesa.");

        if (parent is not null)
        {
            if (parent.ParentCategoryId is not null)
                return Invalid("Subcategoria não pode ter subcategorias.");

            if (parent.Type != type)
                return Invalid("A subcategoria deve ter o mesmo tipo da categoria pai.");
        }

        if (ValidateEditable(name, icon, color) is { } error)
            return error;

        return new Category
        {
            UserId = userId,
            Name = name.Trim(),
            Type = type,
            ParentCategoryId = parent?.Id,
            Icon = NullIfBlank(icon),
            Color = NullIfBlank(color),
        };
    }

    // Usado só pelo catálogo (DefaultCategories): a categoria nasce do padrão ou é adotada por ele.
    internal void StampTemplate(string key) => TemplateKey = key;

    // Tipo e categoria pai não são editáveis nesta fase.
    public Result<Category> Update(string name, string? icon, string? color)
    {
        if (ValidateEditable(name, icon, color) is { } error)
            return error;

        Name = name.Trim();
        Icon = NullIfBlank(icon);
        Color = NullIfBlank(color);
        return this;
    }

    // As contagens vêm da consulta; a decisão e as mensagens ficam aqui.
    public Error? CheckCanDelete(int activeSubcategoryCount, int activeTransactionCount)
    {
        if (activeSubcategoryCount > 0)
            return new Error(ErrorType.Conflict, "Esta categoria tem subcategorias. Exclua as subcategorias antes.");

        if (activeTransactionCount > 0)
            return new Error(ErrorType.Conflict,
                "Esta categoria tem transações. Mova as transações para outra categoria antes de excluir.");

        return null;
    }

    private static Error? ValidateEditable(string? name, string? icon, string? color)
    {
        if (string.IsNullOrWhiteSpace(name))
            return Invalid("Informe o nome da categoria.");

        if (name.Trim().Length > NameMaxLength)
            return Invalid($"O nome da categoria deve ter no máximo {NameMaxLength} caracteres.");

        if (NullIfBlank(icon)?.Length > IconMaxLength)
            return Invalid($"O ícone deve ter no máximo {IconMaxLength} caracteres.");

        if (NullIfBlank(color) is { } c && !HexColor().IsMatch(c))
            return Invalid("A cor deve estar no formato #RRGGBB.");

        return null;
    }

    private static string? NullIfBlank(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static Error Invalid(string message) => new(ErrorType.Validation, message);

    [GeneratedRegex("^#[0-9A-Fa-f]{6}$")]
    private static partial Regex HexColor();
}
