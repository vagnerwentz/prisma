using Prisma.Domain.Transactions;

namespace Prisma.Domain.Categories;

// O que a sincronização fez: categorias novas (a gravar), categorias existentes que receberam a chave
// (já alteradas) e a versão do catálogo que o usuário passa a ter.
public sealed record CatalogSync(IReadOnlyList<Category> Added, IReadOnlyList<Category> Stamped, int Version);

// Catálogo de categorias padrão (docs/fase-1.md, 2.4; docs/fase-2.md, 2.11). Nomes em pt-BR porque são
// dados exibidos ao usuário, não código. Ícones são nomes do Lucide (kebab-case), e cada um precisa estar
// no mapa do front (categoryIcons.ts; um teste confere). Cada categoria é uma faixa do espectro, e as
// subcategorias herdam a cor da categoria pai.
//
// Cada categoria tem uma chave estável (nunca renomeie uma chave) e a versão em que entrou no catálogo.
// Para acrescentar categorias: suba Version e marque as novas com ela; cada usuário as recebe uma vez.
public static class DefaultCategories
{
    public const int Version = 2;

    private sealed record Entry(string Slug, string Name, string Icon, int AddedIn, Entry[] Subcategories);

    private sealed record Root(TransactionType Type, string Color, Entry Entry)
    {
        public string Key => $"{(Type == TransactionType.Expense ? "expense" : "income")}.{Entry.Slug}";
    }

    private static Entry E(string slug, string name, string icon, params Entry[] subcategories) =>
        new(slug, name, icon, AddedIn: 1, subcategories);

    // Categoria (com as subcategorias) que entrou numa versão posterior do catálogo.
    private static Entry Since(int version, Entry entry) => entry with { AddedIn = version };

    private static readonly Root[] Tree =
    [
        new(TransactionType.Expense, "#8B5CF6", E("housing", "Moradia", "house",
            E("rent", "Aluguel", "key-round"), E("condo", "Condomínio", "building-2"), E("electricity", "Energia", "zap"),
            E("water", "Água", "droplets"), E("internet", "Internet", "wifi"), E("gas", "Gás", "flame"))),
        new(TransactionType.Expense, "#F59E0B", E("food", "Alimentação", "utensils",
            E("groceries", "Mercado", "shopping-cart"), E("restaurant", "Restaurante", "utensils-crossed"),
            E("delivery", "Delivery", "bike"), E("bakery", "Padaria", "croissant"))),
        new(TransactionType.Expense, "#3B82F6", E("transport", "Transporte", "car",
            E("fuel", "Combustível", "fuel"), E("ride-hailing", "App de transporte", "car-taxi-front"),
            E("parking", "Estacionamento", "square-parking"), E("maintenance", "Manutenção", "wrench"),
            E("public-transit", "Transporte público", "bus"))),
        new(TransactionType.Expense, "#F43F5E", E("health", "Saúde", "heart-pulse",
            E("health-plan", "Plano de saúde", "shield-plus"), E("pharmacy", "Farmácia", "pill"),
            E("appointments", "Consultas", "stethoscope"), E("gym", "Academia", "dumbbell"))),
        new(TransactionType.Expense, "#6366F1", E("education", "Educação", "graduation-cap",
            E("courses", "Cursos", "presentation"), E("books", "Livros", "book-open"), E("tuition", "Mensalidade", "school"))),
        new(TransactionType.Expense, "#EC4899", E("leisure", "Lazer", "party-popper",
            E("streaming", "Streaming", "tv"), E("travel", "Viagem", "plane"), E("bars", "Bares", "beer"),
            E("movies", "Cinema", "clapperboard"))),
        new(TransactionType.Expense, "#14B8A6", E("shopping", "Compras", "shopping-bag",
            E("clothing", "Roupas", "shirt"), E("electronics", "Eletrônicos", "smartphone"), E("home", "Casa", "sofa"))),
        new(TransactionType.Expense, "#06B6D4", E("services", "Serviços", "layers",
            E("subscriptions", "Assinaturas", "repeat"), E("phone", "Telefonia", "phone"))),
        // Versão 2: pedido do pai do dono ("Faltou tipos de seguros, como de vida, residenciais, veicular").
        new(TransactionType.Expense, "#10B981", Since(2, E("insurance", "Seguros", "shield-check",
            E("life", "Vida", "heart-handshake"), E("home", "Residencial", "house-plus"), E("vehicle", "Veicular", "car-front")))),
        new(TransactionType.Expense, "#64748B", E("taxes-and-fees", "Impostos e Tarifas", "landmark")),
        new(TransactionType.Expense, "#94A3B8", E("other", "Outros", "shapes")),
        new(TransactionType.Income, "#84CC16", E("salary", "Salário", "briefcase")),
        new(TransactionType.Income, "#22D3EE", E("freelance", "Freelance", "laptop")),
        new(TransactionType.Income, "#A78BFA", E("investment-income", "Rendimentos", "sprout")),
        new(TransactionType.Income, "#FBBF24", E("reimbursement", "Reembolso", "undo-2")),
        new(TransactionType.Income, "#94A3B8", E("other", "Outros", "shapes")),
    ];

    // Todos os ícones do catálogo, para o teste que os confere contra o mapa do front.
    public static IEnumerable<string> Icons =>
        Tree.SelectMany(root => root.Entry.Subcategories.Select(sub => sub.Icon).Prepend(root.Entry.Icon)).Distinct();

    // Cadastro: o catálogo inteiro, já com as chaves; o usuário nasce na versão atual.
    public static IReadOnlyList<Category> CreateFor(Guid userId) => Sync(userId, userVersion: 0, []).Added;

    // Entrega ao usuário as categorias das versões que ele ainda não recebeu (docs/fase-2.md, 2.11). Cada
    // categoria é encontrada pela chave ou, sem chave, pelo mesmo nome no mesmo lugar (mesmo tipo e mesmo
    // pai), e então recebe a chave. Só nasce o que é de versão nova e não foi encontrado: o que a pessoa
    // renomeou ou excluiu de uma versão que ela já recebeu não volta. Nome, cor e ícone nunca mudam.
    // active: as categorias ativas do usuário.
    public static CatalogSync Sync(Guid userId, int userVersion, IReadOnlyCollection<Category> active)
    {
        if (active.Any(c => c.UserId != userId))
            throw new ArgumentException("Todas as categorias devem ser do usuário sincronizado.", nameof(active));

        var added = new List<Category>();
        var stamped = new List<Category>();

        Category? Place(string key, string name, string icon, TransactionType type, string color, Category? parent, int addedIn)
        {
            var existing = active.FirstOrDefault(c => c.TemplateKey == key)
                ?? active.FirstOrDefault(c => c.TemplateKey is null && c.Type == type
                    && c.ParentCategoryId == parent?.Id && SameName(c.Name, name));

            if (existing is not null)
            {
                if (existing.TemplateKey is null)
                {
                    existing.StampTemplate(key);
                    stamped.Add(existing);
                }
                return existing;
            }

            if (addedIn <= userVersion)
                return null;

            var created = Category.Create(userId, name, type, parent, icon, color).Value;
            created.StampTemplate(key);
            added.Add(created);
            return created;
        }

        foreach (var root in Tree)
        {
            var entry = root.Entry;
            var parent = Place(root.Key, entry.Name, entry.Icon, root.Type, root.Color, null, entry.AddedIn);

            // Raiz que a pessoa excluiu ou renomeou: as subcategorias dela não têm onde ficar.
            if (parent is null)
                continue;

            foreach (var sub in entry.Subcategories)
                Place($"{root.Key}.{sub.Slug}", sub.Name, sub.Icon, root.Type, root.Color, parent,
                    Math.Max(entry.AddedIn, sub.AddedIn));
        }

        return new CatalogSync(added, stamped, Version);
    }

    private static bool SameName(string a, string b) =>
        string.Equals(a.Trim(), b, StringComparison.InvariantCultureIgnoreCase);
}
