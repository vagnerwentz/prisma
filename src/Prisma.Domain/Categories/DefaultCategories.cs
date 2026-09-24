using Prisma.Domain.Transactions;

namespace Prisma.Domain.Categories;

// Conjunto padrão do novo usuário (docs/fase-1.md, 2.4). Nomes em pt-BR porque são dados
// exibidos ao usuário, não código. Ícones são nomes do Lucide (kebab-case); cada categoria é uma
// faixa do espectro e as subcategorias herdam a cor da categoria pai.
public static class DefaultCategories
{
    private sealed record Seed(string Name, string Icon, string[] Subcategories, string[] SubcategoryIcons);

    private static Seed S(string name, string icon, params (string Name, string Icon)[] subs) =>
        new(name, icon, subs.Select(s => s.Name).ToArray(), subs.Select(s => s.Icon).ToArray());

    private static readonly (TransactionType Type, string Color, Seed Seed)[] Tree =
    [
        (TransactionType.Expense, "#8B5CF6", S("Moradia", "house",
            ("Aluguel", "key-round"), ("Condomínio", "building-2"), ("Energia", "zap"), ("Água", "droplets"),
            ("Internet", "wifi"), ("Gás", "flame"))),
        (TransactionType.Expense, "#F59E0B", S("Alimentação", "utensils",
            ("Mercado", "shopping-cart"), ("Restaurante", "utensils-crossed"), ("Delivery", "bike"), ("Padaria", "croissant"))),
        (TransactionType.Expense, "#3B82F6", S("Transporte", "car",
            ("Combustível", "fuel"), ("App de transporte", "car-taxi-front"), ("Estacionamento", "square-parking"),
            ("Manutenção", "wrench"), ("Transporte público", "bus"))),
        (TransactionType.Expense, "#F43F5E", S("Saúde", "heart-pulse",
            ("Plano de saúde", "shield-plus"), ("Farmácia", "pill"), ("Consultas", "stethoscope"), ("Academia", "dumbbell"))),
        (TransactionType.Expense, "#6366F1", S("Educação", "graduation-cap",
            ("Cursos", "presentation"), ("Livros", "book-open"), ("Mensalidade", "school"))),
        (TransactionType.Expense, "#EC4899", S("Lazer", "party-popper",
            ("Streaming", "tv"), ("Viagem", "plane"), ("Bares", "beer"), ("Cinema", "clapperboard"))),
        (TransactionType.Expense, "#14B8A6", S("Compras", "shopping-bag",
            ("Roupas", "shirt"), ("Eletrônicos", "smartphone"), ("Casa", "sofa"))),
        (TransactionType.Expense, "#06B6D4", S("Serviços", "layers",
            ("Assinaturas", "repeat"), ("Telefonia", "phone"))),
        (TransactionType.Expense, "#64748B", S("Impostos e Tarifas", "landmark")),
        (TransactionType.Expense, "#94A3B8", S("Outros", "shapes")),
        (TransactionType.Income, "#84CC16", S("Salário", "briefcase")),
        (TransactionType.Income, "#22D3EE", S("Freelance", "laptop")),
        (TransactionType.Income, "#A78BFA", S("Rendimentos", "sprout")),
        (TransactionType.Income, "#FBBF24", S("Reembolso", "undo-2")),
        (TransactionType.Income, "#94A3B8", S("Outros", "shapes")),
    ];

    public static IReadOnlyList<Category> CreateFor(Guid userId)
    {
        var categories = new List<Category>();

        foreach (var (type, color, seed) in Tree)
        {
            var root = Category.Create(userId, seed.Name, type, null, seed.Icon, color).Value;
            categories.Add(root);
            categories.AddRange(seed.Subcategories.Select((sub, i) =>
                Category.Create(userId, sub, type, root, seed.SubcategoryIcons[i], color).Value));
        }

        return categories;
    }
}
