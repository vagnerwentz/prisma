using Prisma.Domain.Categories;
using Prisma.Domain.Transactions;
using Shouldly;

namespace Prisma.Domain.Tests.Categories;

public sealed class DefaultCategoriesTests
{
    // Transcrito de docs/fase-1.md, seção 2.4, e docs/fase-2.md, 2.11 (Seguros, versão 2 do catálogo).
    // É a especificação, não o código.
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
        (TransactionType.Expense, "Seguros", ["Vida", "Residencial", "Veicular"]),
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

    // Identidade visual (etapa 1.13): cada categoria nasce com ícone (nome do Lucide) e cor; a
    // subcategoria herda a cor da categoria pai, para a família ser reconhecida pela cor.
    [Fact]
    public void Every_category_has_an_icon_and_subcategories_share_the_parent_color()
    {
        var categories = DefaultCategories.CreateFor(Guid.NewGuid());

        categories.ShouldAllBe(c => !string.IsNullOrWhiteSpace(c.Icon) && c.Color != null);
        foreach (var sub in categories.Where(c => c.ParentCategoryId is not null))
            sub.Color.ShouldBe(categories.Single(c => c.Id == sub.ParentCategoryId).Color);
    }

    [Fact]
    public void Root_categories_of_the_same_type_have_distinct_colors_except_Outros()
    {
        var roots = DefaultCategories.CreateFor(Guid.NewGuid()).Where(c => c.ParentCategoryId is null && c.Name != "Outros");

        foreach (var group in roots.GroupBy(c => c.Type))
            group.Select(c => c.Color).ShouldBeUnique();
    }

    [Fact]
    public void Each_call_creates_new_categories()
    {
        var first = DefaultCategories.CreateFor(Guid.NewGuid()).Select(c => c.Id);
        var second = DefaultCategories.CreateFor(Guid.NewGuid()).Select(c => c.Id);

        first.Intersect(second).ShouldBeEmpty();
    }

    // docs/fase-2.md, 2.11, regra 1: toda categoria padrão tem chave estável, única e legível.
    [Fact]
    public void Every_default_category_has_a_unique_stable_key()
    {
        var categories = DefaultCategories.CreateFor(Guid.NewGuid());

        categories.ShouldAllBe(c => c.TemplateKey != null);
        categories.Select(c => c.TemplateKey).ShouldBeUnique();
        categories.ShouldAllBe(c => System.Text.RegularExpressions.Regex.IsMatch(c.TemplateKey!, "^(expense|income)(\\.[a-z-]+)+$"));
        foreach (var sub in categories.Where(c => c.ParentCategoryId is not null))
            sub.TemplateKey!.ShouldStartWith(categories.Single(c => c.Id == sub.ParentCategoryId).TemplateKey + ".");
    }

    [Fact]
    public void Insurance_is_in_the_catalog_with_its_own_color_and_icons()
    {
        var categories = DefaultCategories.CreateFor(Guid.NewGuid());

        var insurance = categories.Single(c => c.TemplateKey == "expense.insurance");
        insurance.Name.ShouldBe("Seguros");
        insurance.Color.ShouldBe("#10B981");
        insurance.Icon.ShouldBe("shield-check");
        categories.Where(c => c.ParentCategoryId == insurance.Id)
            .Select(c => (c.TemplateKey, c.Name, c.Icon))
            .ShouldBe([
                ("expense.insurance.life", "Vida", "heart-handshake"),
                ("expense.insurance.home", "Residencial", "house-plus"),
                ("expense.insurance.vehicle", "Veicular", "car-front"),
            ], ignoreOrder: true);
    }
}

