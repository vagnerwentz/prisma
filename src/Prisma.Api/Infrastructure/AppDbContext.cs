using System.Reflection;
using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Prisma.Api.Infrastructure.Auth;
using Prisma.Api.Infrastructure.Email;
using Prisma.Api.Infrastructure.Privacy;
using Prisma.Domain;
using Prisma.Domain.Accounts;
using Prisma.Domain.Categories;
using Prisma.Domain.FixedIncome;
using Prisma.Domain.Investments;
using Prisma.Domain.Investments.Trades;
using Prisma.Domain.Market;
using Prisma.Domain.Recurrences;
using Prisma.Domain.Statements;
using Prisma.Domain.Transactions;

namespace Prisma.Api.Infrastructure;

// IdentityUserContext, e não IdentityDbContext, porque a aplicação não usa roles.
public sealed class AppDbContext(
    DbContextOptions<AppDbContext> options,
    ICurrentUser currentUser,
    IClock clock)
    : IdentityUserContext<AppUser, Guid>(options), IDataProtectionKeyContext
{
    // Filtros nomeados (EF Core 10): permitem ignorar só o soft delete, por exemplo para
    // restaurar um registro, sem nunca desligar o isolamento por usuário.
    public const string OwnerFilter = "Owner";
    public const string SoftDeleteFilter = "SoftDelete";

    public DbSet<Account> Accounts => Set<Account>();

    public DbSet<Category> Categories => Set<Category>();

    public DbSet<Statement> Statements => Set<Statement>();

    public DbSet<Transaction> Transactions => Set<Transaction>();

    public DbSet<InstallmentPurchase> InstallmentPurchases => Set<InstallmentPurchase>();

    public DbSet<Recurrence> Recurrences => Set<Recurrence>();

    public DbSet<RecurrencePending> RecurrencePendings => Set<RecurrencePending>();

    // Ativos da carteira de cada pessoa (docs/investimentos.md, etapa 5a).
    public DbSet<Holding> Holdings => Set<Holding>();

    // Notas de corretagem digitadas, com as linhas dentro (docs/investimentos.md, I.7b1).
    public DbSet<TradeNote> TradeNotes => Set<TradeNote>();

    // "Não encontrou? Avise a gente": o código que falta no catálogo, pedido por cada pessoa (I.7c3).
    public DbSet<AssetRequest> AssetRequests => Set<AssetRequest>();

    public DbSet<EventReview> EventReviews => Set<EventReview>();

    // Chaves que assinam o cookie de sessão. No banco, e não no disco do contêiner, para que um
    // deploy novo não derrube a sessão de quem está logado. Não é do usuário: fica sem filtro.
    public DbSet<DataProtectionKey> DataProtectionKeys => Set<DataProtectionKey>();

    // Catálogo de ativos da bolsa: dado de mercado, o mesmo para todos (docs/investimentos.md). Sem filtro.
    public DbSet<Asset> Assets => Set<Asset>();

    // O logo de cada ativo, já limpo (etapa 4). Global, como o catálogo.
    public DbSet<AssetLogo> AssetLogos => Set<AssetLogo>();

    // Eventos da empresa (desdobramento, grupamento, conversão; I.7c1). Globais, como o catálogo.
    public DbSet<CorporateEvent> CorporateEvents => Set<CorporateEvent>();

    // Objetivos (as caixas) e aportes em renda fixa de cada pessoa (docs/renda-fixa.md, 2.2 e 2.3).
    public DbSet<SavingsGoal> SavingsGoals => Set<SavingsGoal>();

    public DbSet<FixedIncomeDeposit> FixedIncomeDeposits => Set<FixedIncomeDeposit>();

    // Taxas diárias do CDI, do Banco Central: dado de mercado, o mesmo para todos (docs/renda-fixa.md, 2.4). Sem filtro.
    public DbSet<ReferenceRate> ReferenceRates => Set<ReferenceRate>();

    // Execuções da sincronização dos dados de mercado, para a tela de admin (docs/admin-mercado.md). Global, sem dado pessoal.
    public DbSet<MarketSyncRun> MarketSyncRuns => Set<MarketSyncRun>();

    // Fila de e-mails a enviar (docs/email.md, seção 3). Sem filtro: a tarefa atende todo mundo.
    public DbSet<OutboxEmail> OutboxEmails => Set<OutboxEmail>();

    // Contas apagadas nos últimos 90 dias, para apagar de novo depois de restaurar um backup (etapa A.3). Sem filtro.
    public DbSet<DeletedAccount> DeletedAccounts => Set<DeletedAccount>();

    // Tabelas sem dono, de propósito (CLAUDE.md, regra 6). Toda outra tabela herda Entity e recebe o filtro
    // de dono; um teste de arquitetura recusa tabela que não seja nem uma coisa nem outra. Tabela global
    // nova entra aqui, com o motivo.
    public static readonly IReadOnlySet<Type> GlobalTables = new HashSet<Type>
    {
        // Identity: o próprio usuário e os dados de login dele.
        typeof(AppUser),
        typeof(IdentityUserClaim<Guid>),
        typeof(IdentityUserLogin<Guid>),
        typeof(IdentityUserToken<Guid>),
        // Chaves do cookie de sessão.
        typeof(DataProtectionKey),
        // Dados de mercado.
        typeof(Asset),
        typeof(AssetLogo),
        typeof(CorporateEvent),
        // Taxas diárias do CDI (renda fixa): o mesmo histórico para todos; só a sincronização escreve.
        typeof(ReferenceRate),
        // Execuções da sincronização do mercado: dado operacional, o mesmo para todos, sem quem pediu; só a sincronização escreve.
        typeof(MarketSyncRun),
        // Fila de e-mails: só a API escreve e só a tarefa lê, para todos; nenhum endpoint a lista. O UserId é referência.
        typeof(OutboxEmail),
        // Contas apagadas: o dono não existe mais; só a limpeza lê (AccountEraser).
        typeof(DeletedAccount),
    };

    // Lido pelo EF a cada consulta, não no momento em que o modelo é construído.
    private Guid CurrentUserId => currentUser.UserId;

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.HasPostgresExtension("citext");

        // Nomes curtos em vez de asp_net_users etc. O snake_case vem da convenção global.
        builder.Entity<AppUser>().ToTable("users");

        // O Identity nomeia estes dois índices explicitamente, o que escapa da convenção.
        builder.Entity<AppUser>().HasIndex(u => u.NormalizedEmail).HasDatabaseName("ix_users_normalized_email");
        builder.Entity<AppUser>().HasIndex(u => u.NormalizedUserName).HasDatabaseName("ix_users_normalized_user_name");

        builder.Entity<IdentityUserClaim<Guid>>().ToTable("user_claims");
        builder.Entity<IdentityUserLogin<Guid>>().ToTable("user_logins");
        builder.Entity<IdentityUserToken<Guid>>().ToTable("user_tokens");

        builder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);

        // Toda entidade do domínio recebe os filtros automaticamente (CLAUDE.md, regras 6 e 7).
        var applyFilters = typeof(AppDbContext)
            .GetMethod(nameof(ApplyEntityFilters), BindingFlags.NonPublic | BindingFlags.Instance)!;
        foreach (var type in builder.Model.GetEntityTypes()
                     .Select(t => t.ClrType)
                     .Where(t => typeof(Entity).IsAssignableFrom(t))
                     .ToList())
            applyFilters.MakeGenericMethod(type).Invoke(this, [builder]);
    }

    private void ApplyEntityFilters<TEntity>(ModelBuilder builder) where TEntity : Entity =>
        builder.Entity<TEntity>()
            .HasQueryFilter(OwnerFilter, e => e.UserId == CurrentUserId)
            .HasQueryFilter(SoftDeleteFilter, e => e.DeletedAt == null);

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        ApplyAuditing();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        ApplyAuditing();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    // Datas de auditoria vêm do IClock; Remove vira soft delete (CLAUDE.md, regra 7).
    private void ApplyAuditing()
    {
        // Na precisão do Postgres (microssegundo): a resposta de quem grava traz o mesmo instante que a leitura depois.
        // O .NET conta décimos de microssegundo, e o relógio do Linux usa o dígito a mais.
        var utcNow = clock.UtcNow;
        var now = utcNow.AddTicks(-(utcNow.Ticks % (TimeSpan.TicksPerMillisecond / 1000)));

        foreach (var user in ChangeTracker.Entries<AppUser>().Where(e => e.State == EntityState.Added))
            user.Entity.CreatedAt = now;

        // Lista fechada antes: o soft delete muda o estado de outras entradas (as partes da entidade).
        foreach (var entry in ChangeTracker.Entries<Entity>().ToList())
        {
            switch (entry.State)
            {
                case EntityState.Added:
                    if (entry.Entity.UserId != CurrentUserId)
                        throw new InvalidOperationException("Entidade criada para outro usuário que não o autenticado.");
                    entry.Property(e => e.CreatedAt).CurrentValue = now;
                    entry.Property(e => e.UpdatedAt).CurrentValue = now;
                    break;

                case EntityState.Modified:
                    entry.Property(e => e.UpdatedAt).CurrentValue = now;
                    break;

                case EntityState.Deleted:
                    entry.State = EntityState.Modified;
                    entry.Property(e => e.DeletedAt).CurrentValue = now;
                    entry.Property(e => e.UpdatedAt).CurrentValue = now;
                    KeepOwnedParts(entry);
                    break;
            }
        }
    }

    // O EF apaga junto com o dono as partes que vivem dentro dele (tipos owned, ex.: as linhas da nota). No soft delete o
    // dono fica, então as partes ficam: sem isto, desfazer a exclusão devolveria a nota sem linhas.
    private void KeepOwnedParts(EntityEntry entry)
    {
        foreach (var navigation in entry.Navigations.Where(n => n.Metadata.TargetEntityType.IsOwned()))
        {
            var parts = navigation is CollectionEntry collection
                ? collection.CurrentValue?.Cast<object>() ?? []
                : navigation.CurrentValue is { } single ? [single] : [];
            foreach (var part in parts)
            {
                var partEntry = Entry(part);
                if (partEntry.State == EntityState.Deleted)
                    partEntry.State = EntityState.Unchanged;
            }
        }
    }
}
