using Prisma.Domain.Transactions;

namespace Prisma.Domain.Categories;

// Conjunto padrão do novo usuário (docs/fase-1.md, 2.4). Nomes em pt-BR porque são dados
// exibidos ao usuário, não código.
public static class DefaultCategories
{
    private static readonly (TransactionType Type, string Name, string[] Subcategories)[] Tree =
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

    public static IReadOnlyList<Category> CreateFor(Guid userId)
    {
        var categories = new List<Category>();

        foreach (var (type, name, subcategories) in Tree)
        {
            var root = Category.Create(userId, name, type, null, null, null).Value;
            categories.Add(root);
            categories.AddRange(subcategories.Select(sub => Category.Create(userId, sub, type, root, null, null).Value));
        }

        return categories;
    }
}
