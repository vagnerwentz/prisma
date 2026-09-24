using Prisma.Domain.Categories;
using Prisma.Domain.Transactions;
using Shouldly;

namespace Prisma.Domain.Tests.Categories;

public sealed class DefaultCategoriesTests
{
    // Transcrito de docs/fase-1.md, seção 2.4. É a especificação, não o código.
    private static readonly (TransactionType Type, string Name, string[] Subcategories)[] Spec =
    [
        (TransactionType.Expense, "Moradia", ["Aluguel", "Condomínio", "Energia", "Água", "Internet", "Gás"]),
        (TransactionType.Expense, "Alimentação", ["Mercado", "Restaurante", "Delivery", "Padaria"]),
        (TransactionType.Expense, "Transporte", ["Combustível", "App de transporte", "Estacionamento", "Manutenção", "Transporte público"]),
        (TransactionType.Expense, "Saúde", ["Plano de saúde", "Farmácia", "Consultas", "Academia"]),
        (TransactionType.Expense, "Educação", ["Cursos", "Livros", "Mensalidade"]),
        (TransactionType.Expense, "Lazer", ["Streaming", "Viagem", "Bares", "Cinema"]),
        (TransactionType.Expense, "Compras", ["Roupas", "Eletrônicos", "Casa"]),
        (TransactionType.Expense, "Serviços", ["Assinaturas", "Telefonia"]),
        (TransactionType.Expense, "Impostos e Tarifas", []),
        (TransactionType.Expense, "Outros", []),
        (TransactionType.Income, "Salário", []),
        (TransactionType.Income, "Freelance", []),
        (TransactionType.Income, "Rendimentos", []),
        (TransactionType.Income, "Reembolso", []),
        (TransactionType.Income, "Outros", []),
    ];

    [Fact]
    public void Creates_exactly_the_specified_tree()
    {
        var categories = DefaultCategories.CreateFor(Guid.NewGuid());

        var roots = categories.Where(c => c.ParentCategoryId is null).ToList();
        var tree = roots
            .Select(root => (
                root.Type,
                root.Name,
                Subcategories: categories.Where(c => c.ParentCategoryId == root.Id).Select(c => c.Name).ToArray()))
            .ToList();

        tree.Count.ShouldBe(Spec.Length);
        foreach (var expected in Spec)
        {
            var actual = tree.Single(t => t.Type == expected.Type && t.Name == expected.Name);
            actual.Subcategories.ShouldBe(expected.Subcategories, ignoreOrder: true);
        }

        categories.Count.ShouldBe(Spec.Length + Spec.Sum(s => s.Subcategories.Length));
    }

    [Fact]
    public void Every_category_belongs_to_the_user_and_subcategories_share_the_parent_type()
    {
        var userId = Guid.NewGuid();

        var categories = DefaultCategories.CreateFor(userId);

        categories.ShouldAllBe(c => c.UserId == userId);
        foreach (var sub in categories.Where(c => c.ParentCategoryId is not null))
            sub.Type.ShouldBe(categories.Single(c => c.Id == sub.ParentCategoryId).Type);
    }

    [Fact]
    public void Each_call_creates_new_categories()
    {
        var first = DefaultCategories.CreateFor(Guid.NewGuid()).Select(c => c.Id);
        var second = DefaultCategories.CreateFor(Guid.NewGuid()).Select(c => c.Id);

        first.Intersect(second).ShouldBeEmpty();
    }
}
