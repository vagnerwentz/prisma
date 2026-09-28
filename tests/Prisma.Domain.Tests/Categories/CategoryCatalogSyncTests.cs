using Prisma.Domain.Categories;
using Prisma.Domain.Transactions;
using Shouldly;

namespace Prisma.Domain.Tests.Categories;

// docs/fase-2.md, 2.11: cada usuário guarda a versão do catálogo que já recebeu; a sincronização
// entrega só as categorias das versões novas, uma vez, sem mexer no que a pessoa personalizou.
public sealed class CategoryCatalogSyncTests
{
    private static readonly Guid UserId = Guid.NewGuid();

    // O catálogo original (versão 1), como os cadastros antigos gravaram: sem chave. Transcrito de
    // docs/fase-1.md, 2.4.
    private static readonly (TransactionType Type, string Name, string[] Subcategories)[] Original =
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

    private static List<Category> OldRegistration()
    {
        var categories = new List<Category>();
        foreach (var (type, name, subs) in Original)
        {
            var root = Category.Create(UserId, name, type, null, "shapes", "#94A3B8").Value;
            categories.Add(root);
            categories.AddRange(subs.Select(sub => Category.Create(UserId, sub, type, root, "shapes", "#94A3B8").Value));
        }
        return categories;
    }

    private static Category Find(IEnumerable<Category> categories, string name, TransactionType type = TransactionType.Expense) =>
        categories.Single(c => c.Name == name && c.Type == type);

    // O pai do dono: cadastrado antes, está na versão 1 e passa a ver Seguros.
    [Fact]
    public void An_old_user_receives_insurance_once_under_its_own_root()
    {
        var active = OldRegistration();

        var sync = DefaultCategories.Sync(UserId, userVersion: 1, active);

        sync.Version.ShouldBe(DefaultCategories.Version);
        sync.Version.ShouldBe(2);
        var insurance = Find(sync.Added, "Seguros");
        insurance.ParentCategoryId.ShouldBeNull();
        insurance.UserId.ShouldBe(UserId);
        sync.Added.Where(c => c.ParentCategoryId == insurance.Id).Select(c => c.Name)
            .ShouldBe(["Vida", "Residencial", "Veicular"], ignoreOrder: true);
        sync.Added.Count.ShouldBe(4);
    }

    // As originais que ainda têm o nome padrão recebem a chave (regra 4), sem nascer outra.
    [Fact]
    public void Original_categories_with_the_default_name_get_their_key()
    {
        var active = OldRegistration();

        var sync = DefaultCategories.Sync(UserId, userVersion: 1, active);

        sync.Stamped.Count.ShouldBe(active.Count);
        Find(active, "Mercado").TemplateKey.ShouldBe("expense.food.groceries");
        Find(active, "Outros", TransactionType.Income).TemplateKey.ShouldBe("income.other");
        Find(active, "Outros").TemplateKey.ShouldBe("expense.other");
    }

    [Fact]
    public void A_user_already_on_the_current_version_gets_nothing()
    {
        var active = DefaultCategories.CreateFor(UserId).ToList();

        var sync = DefaultCategories.Sync(UserId, DefaultCategories.Version, active);

        sync.Added.ShouldBeEmpty();
        sync.Stamped.ShouldBeEmpty();
        sync.Version.ShouldBe(DefaultCategories.Version);
    }

    // Regra 3: o que a pessoa renomeou não ganha cópia com o nome antigo, e o que excluiu não volta.
    [Fact]
    public void Renamed_and_deleted_originals_do_not_come_back()
    {
        var active = OldRegistration();
        Find(active, "Mercado").Update("Supermercado", "shopping-cart", "#F59E0B");
        active.Remove(Find(active, "Educação"));
        active.RemoveAll(c => c.ParentCategoryId is not null && new[] { "Cursos", "Livros", "Mensalidade" }.Contains(c.Name));

        var sync = DefaultCategories.Sync(UserId, userVersion: 1, active);

        sync.Added.Select(c => c.Name).ShouldBe(["Seguros", "Vida", "Residencial", "Veicular"], ignoreOrder: true);
        Find(active, "Supermercado").TemplateKey.ShouldBeNull();
        Find(active, "Supermercado").Name.ShouldBe("Supermercado");
    }

    // Regra 4: a pessoa já tinha criado "seguros" (em minúsculas) com "Vida": adota, não duplica.
    [Fact]
    public void An_insurance_category_the_user_created_is_adopted()
    {
        var active = OldRegistration();
        var own = Category.Create(UserId, "seguros", TransactionType.Expense, null, "umbrella", "#123456").Value;
        var life = Category.Create(UserId, "Vida", TransactionType.Expense, own, null, null).Value;
        active.AddRange([own, life]);

        var sync = DefaultCategories.Sync(UserId, userVersion: 1, active);

        own.TemplateKey.ShouldBe("expense.insurance");
        life.TemplateKey.ShouldBe("expense.insurance.life");
        (own.Name, own.Icon, own.Color).ShouldBe(("seguros", "umbrella", "#123456"));
        sync.Added.Select(c => c.Name).ShouldBe(["Residencial", "Veicular"], ignoreOrder: true);
        sync.Added.ShouldAllBe(c => c.ParentCategoryId == own.Id);
    }

    // Só adota no mesmo lugar: "Seguros" de receita, ou "Vida" debaixo de outra raiz, não servem.
    [Fact]
    public void A_same_name_in_another_place_is_not_adopted()
    {
        var active = OldRegistration();
        var incomeInsurance = Category.Create(UserId, "Seguros", TransactionType.Income, null, null, null).Value;
        var lifeUnderHealth = Category.Create(UserId, "Vida", TransactionType.Expense, Find(active, "Saúde"), null, null).Value;
        active.AddRange([incomeInsurance, lifeUnderHealth]);

        var sync = DefaultCategories.Sync(UserId, userVersion: 1, active);

        incomeInsurance.TemplateKey.ShouldBeNull();
        lifeUnderHealth.TemplateKey.ShouldBeNull();
        sync.Added.Select(c => c.Name).ShouldBe(["Seguros", "Vida", "Residencial", "Veicular"], ignoreOrder: true);
    }

    // Recebeu Seguros, renomeou para "Proteção" e excluiu "Veicular": nada volta nem duplica.
    [Fact]
    public void After_receiving_the_new_version_changes_are_respected()
    {
        var active = OldRegistration();
        var first = DefaultCategories.Sync(UserId, userVersion: 1, active);
        active.AddRange(first.Added);
        Find(active, "Seguros").Update("Proteção", "shield-check", "#10B981");
        active.Remove(Find(active, "Veicular"));

        var second = DefaultCategories.Sync(UserId, first.Version, active);

        second.Added.ShouldBeEmpty();
        second.Stamped.ShouldBeEmpty();
    }

    [Fact]
    public void Syncing_twice_from_the_same_old_version_does_not_duplicate()
    {
        var active = OldRegistration();
        active.AddRange(DefaultCategories.Sync(UserId, userVersion: 1, active).Added);

        // Outra aba ainda leu a versão 1: encontra tudo pelas chaves e não acrescenta nada.
        var again = DefaultCategories.Sync(UserId, userVersion: 1, active);

        again.Added.ShouldBeEmpty();
    }

    [Fact]
    public void Categories_of_another_user_are_a_programming_error()
    {
        var foreign = DefaultCategories.CreateFor(Guid.NewGuid()).ToList();

        Should.Throw<ArgumentException>(() => DefaultCategories.Sync(UserId, userVersion: 1, foreign));
    }
}
