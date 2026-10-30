using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Prisma.Api.Infrastructure;
using Prisma.Api.Infrastructure.Auth;
using Prisma.Api.Tests.Infrastructure;
using Shouldly;

namespace Prisma.Api.Tests.Features;

// Último acesso (docs/admin-email.md, 2): a conta ganha um carimbo por dia civil em São Paulo, no primeiro pedido
// autenticado do dia. Cobre quem fica dias com a sessão aberta sem entrar de novo, que um carimbo só no login perderia.
[Collection(ApiCollection.Name)]
public sealed class LastSeenTests(PostgresFixture postgres)
{
    // 12:00 em São Paulo (UTC-3), dia 15.
    private readonly FakeClock _clock = new(new DateTime(2026, 10, 15, 15, 0, 0, DateTimeKind.Utc));
    private readonly LogSink _logs = new();

    private PrismaApiFactory Factory(Action<IServiceCollection>? services = null) =>
        new(postgres.ConnectionString, null, _clock, _logs, services);

    private static async Task<(HttpClient Client, string Email)> SignedIn(PrismaApiFactory factory)
    {
        var email = $"{Guid.NewGuid():N}@teste.com.br";
        var client = factory.CreateHttpsClient();
        (await client.PostAsJsonAsync("/auth/register", new { email, password = PrismaApiFactory.Password, acceptedTerms = true }))
            .EnsureSuccessStatusCode();
        await factory.ConfirmEmailAsync(email);
        (await client.PostAsJsonAsync("/auth/login", new { email, password = PrismaApiFactory.Password })).EnsureSuccessStatusCode();
        return (client, email);
    }

    private static async Task<DateTime?> LastSeen(PrismaApiFactory factory, string email)
    {
        using var scope = factory.Services.CreateScope();
        var user = await scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>().FindByEmailAsync(email);
        return user!.LastSeenAt;
    }

    private static async Task ClearLastSeen(PrismaApiFactory factory, string email)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Users.Where(u => u.Email == email).ExecuteUpdateAsync(s => s.SetProperty(u => u.LastSeenAt, (DateTime?)null));
    }

    private static async Task Visit(HttpClient client) =>
        (await client.GetAsync("/auth/me")).StatusCode.ShouldBe(HttpStatusCode.OK);

    [Fact]
    public async Task First_authenticated_request_stamps_the_clock_instant()
    {
        await using var factory = Factory();
        var (client, email) = await SignedIn(factory);

        await Visit(client);

        (await LastSeen(factory, email)).ShouldBe(_clock.UtcNow);
    }

    [Fact]
    public async Task Account_that_never_made_an_authenticated_request_has_no_stamp()
    {
        await using var factory = Factory();
        var email = $"{Guid.NewGuid():N}@teste.com.br";
        (await factory.CreateHttpsClient().PostAsJsonAsync("/auth/register", new { email, password = PrismaApiFactory.Password, acceptedTerms = true }))
            .EnsureSuccessStatusCode();

        (await LastSeen(factory, email)).ShouldBeNull();
    }

    [Fact]
    public async Task Another_request_on_the_same_day_keeps_the_first_instant()
    {
        await using var factory = Factory();
        var (client, email) = await SignedIn(factory);
        await Visit(client);
        var first = _clock.UtcNow;

        _clock.UtcNow = first.AddHours(5); // 17:00 em São Paulo, ainda dia 15
        await Visit(client);

        (await LastSeen(factory, email)).ShouldBe(first);
    }

    [Fact]
    public async Task First_request_of_the_next_day_stamps_again()
    {
        await using var factory = Factory();
        var (client, email) = await SignedIn(factory);
        await Visit(client);

        _clock.UtcNow = new DateTime(2026, 10, 16, 15, 0, 0, DateTimeKind.Utc);
        await Visit(client);

        (await LastSeen(factory, email)).ShouldBe(_clock.UtcNow);
    }

    [Fact]
    public async Task The_day_changes_at_midnight_in_Sao_Paulo_not_in_UTC()
    {
        await using var factory = Factory();
        var (client, email) = await SignedIn(factory);
        await Visit(client);
        var first = _clock.UtcNow;

        // 23:30 de São Paulo no dia 15 já é 02:30 UTC do dia 16: ainda é o mesmo dia para a pessoa.
        _clock.UtcNow = new DateTime(2026, 10, 16, 2, 30, 0, DateTimeKind.Utc);
        await Visit(client);
        (await LastSeen(factory, email)).ShouldBe(first);

        // 00:10 em São Paulo no dia 16.
        _clock.UtcNow = new DateTime(2026, 10, 16, 3, 10, 0, DateTimeKind.Utc);
        await Visit(client);
        (await LastSeen(factory, email)).ShouldBe(_clock.UtcNow);
    }

    [Fact]
    public async Task Stamp_of_one_person_never_touches_another()
    {
        await using var factory = Factory();
        var (clientA, emailA) = await SignedIn(factory);
        var (_, emailB) = await SignedIn(factory);

        await Visit(clientA);

        (await LastSeen(factory, emailA)).ShouldBe(_clock.UtcNow);
        (await LastSeen(factory, emailB)).ShouldBeNull();
    }

    [Fact]
    public async Task Memory_of_the_day_spares_the_database_on_later_requests()
    {
        await using var factory = Factory();
        var (client, email) = await SignedIn(factory);
        await Visit(client);

        // Se o segundo pedido do dia consultasse o banco, a coluna voltaria a ter valor.
        await ClearLastSeen(factory, email);
        await Visit(client);

        (await LastSeen(factory, email)).ShouldBeNull();
    }

    [Fact]
    public async Task A_restart_loses_only_the_memory_and_the_database_decides_again()
    {
        await using var factory = Factory();
        var (client, email) = await SignedIn(factory);
        await Visit(client);
        var first = _clock.UtcNow;

        // Processo novo (deploy) no mesmo dia: sem memória, o UPDATE condicional não muda o que já está carimbado.
        await using var restarted = Factory();
        _clock.UtcNow = first.AddHours(3);
        var again = restarted.CreateHttpsClient();
        (await again.PostAsJsonAsync("/auth/login", new { email, password = PrismaApiFactory.Password })).EnsureSuccessStatusCode();
        await Visit(again);

        (await LastSeen(restarted, email)).ShouldBe(first);
    }

    [Fact]
    public async Task Failing_to_stamp_never_fails_the_request_and_is_logged()
    {
        await using var factory = Factory(services => services.AddSingleton<ILastSeenCache, BrokenCache>());
        var (client, email) = await SignedIn(factory);

        await Visit(client);

        var log = await _logs.WaitFor(l => l.EventId.Name == "LastSeenStampFailed");
        log.Level.ShouldBe(LogLevel.Warning);
        log.Message.ShouldNotContain(email);
    }

    private sealed class BrokenCache : ILastSeenCache
    {
        public ValueTask<bool> WasStampedAsync(Guid userId, DateOnly day) => throw new InvalidOperationException("cache fora do ar");

        public ValueTask RememberAsync(Guid userId, DateOnly day) => ValueTask.CompletedTask;
    }
}
