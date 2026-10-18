using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Prisma.Api.Infrastructure;
using Prisma.Api.Infrastructure.Auth;
using Prisma.Api.Infrastructure.Configurations;
using Prisma.Api.Tests.Infrastructure;
using Prisma.Domain.FixedIncome;
using Shouldly;

namespace Prisma.Api.Tests.Features;

// Tabela global de taxas do CDI (docs/renda-fixa.md, 2.4), contra o Postgres das migrations reais: a chave única que torna
// a sincronização idempotente, os limites da taxa, e o fato de ser de todos (sem dono, sem filtro).
[Collection(ApiCollection.Name)]
public sealed class ReferenceRateTableTests(PostgresFixture postgres)
{
    private static readonly DateTime Fetched = new(2026, 10, 17, 12, 0, 0, DateTimeKind.Utc);

    private static ReferenceRate Rate(DateOnly date, long rateE8 = 50_788) =>
        ReferenceRate.From(ReferenceRateSeries.Cdi, new DailyRate(date, rateE8), "bcb-sgs-12", Fetched);

    [Fact]
    public async Task A_rate_is_stored_and_read_back_exactly()
    {
        await using var factory = await Factory();
        var date = new DateOnly(2026, 10, 15);

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.ReferenceRates.Add(Rate(date));
            await db.SaveChangesAsync();
        }

        await using var reading = factory.Services.CreateAsyncScope();
        var stored = await reading.ServiceProvider.GetRequiredService<AppDbContext>().ReferenceRates.AsNoTracking().SingleAsync();
        stored.Series.ShouldBe(ReferenceRateSeries.Cdi);
        stored.ReferenceDate.ShouldBe(date);
        stored.DailyRateE8.ShouldBe(50_788);
        stored.Source.ShouldBe("bcb-sgs-12");
        stored.FetchedAt.ShouldBe(Fetched);
        stored.RevisedAt.ShouldBeNull();
        stored.Rate.ShouldBe(new DailyRate(date, 50_788));
    }

    // A chave (série, data) é o que impede a mesma taxa duas vezes, mesmo com duas execuções ao mesmo tempo.
    [Fact]
    public async Task The_same_date_cannot_be_stored_twice()
    {
        await using var factory = await Factory();
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.ReferenceRates.Add(Rate(new DateOnly(2026, 10, 15)));
        await db.SaveChangesAsync();

        db.ReferenceRates.Add(Rate(new DateOnly(2026, 10, 15), 60_000));
        var error = await Should.ThrowAsync<DbUpdateException>(() => db.SaveChangesAsync());

        error.IsUniqueViolation(ReferenceRateConfiguration.SeriesDateIndex).ShouldBeTrue();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-50_788)]
    [InlineData(RateHistory.MaxRateE8)]
    [InlineData(13_650_000)] // a anualizada (13,65 %) no lugar da diária
    public async Task A_rate_outside_the_plausible_range_is_refused_by_the_database(long rateE8)
    {
        await using var factory = await Factory();
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        db.ReferenceRates.Add(Rate(new DateOnly(2026, 10, 15), rateE8));
        var error = await Should.ThrowAsync<DbUpdateException>(() => db.SaveChangesAsync());

        error.InnerException.ShouldBeOfType<PostgresException>().ConstraintName.ShouldBe("ck_reference_rates_rate");
    }

    [Theory]
    [InlineData(1)]
    [InlineData(RateHistory.MaxRateE8 - 1)]
    public async Task The_edges_of_the_plausible_range_are_accepted(long rateE8)
    {
        await using var factory = await Factory();
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        db.ReferenceRates.Add(Rate(new DateOnly(2026, 10, 15), rateE8));

        (await db.SaveChangesAsync()).ShouldBe(1);
    }

    // Dado de mercado, o mesmo para todos: dois usuários diferentes leem as mesmas linhas, e a tabela não tem user_id.
    [Fact]
    public async Task Every_user_reads_the_same_rates_and_the_table_has_no_owner()
    {
        await using var factory = await Factory();
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.ReferenceRates.AddRange(Rate(new DateOnly(2026, 10, 14)), Rate(new DateOnly(2026, 10, 15)));
            await db.SaveChangesAsync();
        }

        foreach (var user in new[] { Guid.NewGuid(), Guid.NewGuid() })
        {
            await using var scope = factory.Services.CreateAsyncScope();
            scope.ServiceProvider.GetRequiredService<ScopedUser>().UserId = user;
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            (await db.ReferenceRates.CountAsync()).ShouldBe(2);
        }

        await using var schema = factory.Services.CreateAsyncScope();
        var columns = await schema.ServiceProvider.GetRequiredService<AppDbContext>().Database
            .SqlQuery<string>($"SELECT column_name AS \"Value\" FROM information_schema.columns WHERE table_name = 'reference_rates'")
            .ToListAsync();
        columns.ShouldNotContain("user_id");
        columns.ShouldNotContain("deleted_at");
    }

    private async Task<PrismaApiFactory> Factory()
    {
        var factory = new PrismaApiFactory(postgres.ConnectionString);
        await using var scope = factory.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<AppDbContext>().ReferenceRates.ExecuteDeleteAsync();
        return factory;
    }
}
