using Microsoft.EntityFrameworkCore;
using Prisma.Api.Infrastructure;
using Prisma.Api.Infrastructure.Auth;
using Prisma.Domain;
using Shouldly;

namespace Prisma.Architecture.Tests;

// CLAUDE.md, regra 6: toda tabela tem dono e filtro de dono, ou está na lista fechada de tabelas globais
// (AppDbContext.GlobalTables), com o motivo. Sem este teste, uma tabela de dados do usuário que esquecesse
// de herdar Entity ficaria sem filtro, visível para todos, e nada avisaria.
public sealed class OwnershipTests
{
    private static readonly Microsoft.EntityFrameworkCore.Metadata.IModel Model = BuildModel();

    [Fact]
    public void Every_table_has_an_owner_or_is_declared_global()
    {
        var violations = Model.GetEntityTypes()
            .Where(t => !t.IsOwned())
            .Select(t => t.ClrType)
            .Where(type => !typeof(Entity).IsAssignableFrom(type) && !AppDbContext.GlobalTables.Contains(type))
            .Select(type => type.FullName)
            .ToList();

        violations.ShouldBeEmpty("Herde Entity (dado do usuário) ou declare em AppDbContext.GlobalTables, com o motivo.");
    }

    [Fact]
    public void Every_owned_table_has_the_owner_filter()
    {
        var violations = Model.GetEntityTypes()
            .Where(t => typeof(Entity).IsAssignableFrom(t.ClrType))
            .Where(t => t.FindDeclaredQueryFilter(AppDbContext.OwnerFilter) is null)
            .Select(t => t.ClrType.FullName)
            .ToList();

        violations.ShouldBeEmpty();
    }

    [Fact]
    public void Global_tables_neither_have_an_owner_nor_are_stale()
    {
        var mapped = Model.GetEntityTypes().Select(t => t.ClrType).ToHashSet();

        AppDbContext.GlobalTables.ShouldAllBe(type => mapped.Contains(type), "Tabela global que não existe mais: tire da lista.");
        AppDbContext.GlobalTables.ShouldAllBe(type => !typeof(Entity).IsAssignableFrom(type));
    }

    private static Microsoft.EntityFrameworkCore.Metadata.IModel BuildModel()
    {
        // Só monta o modelo: nenhuma conexão é aberta.
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql("Host=localhost;Database=model-only")
            .UseSnakeCaseNamingConvention()
            .Options;
        using var db = new AppDbContext(options, new NoUser(), new NoClock());
        return db.Model;
    }

    private sealed class NoUser : ICurrentUser
    {
        public Guid UserId => throw new InvalidOperationException("Só o modelo é lido.");
    }

    private sealed class NoClock : IClock
    {
        public DateTime UtcNow => throw new InvalidOperationException("Só o modelo é lido.");
    }
}
