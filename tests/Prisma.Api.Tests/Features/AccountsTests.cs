using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Npgsql;
using Prisma.Api.Tests.Infrastructure;
using Shouldly;

namespace Prisma.Api.Tests.Features;

[Collection(ApiCollection.Name)]
public sealed class AccountsTests(PostgresFixture postgres)
{
    private sealed record AccountDto(
        Guid Id, string Name, string Type, long InitialBalanceCents,
        int? ClosingDay, int? DueDay, long? CreditLimitCents, bool IsActive);

    private static readonly object Checking = new
    {
        name = "Itaú Personnalité", type = "Checking", initialBalanceCents = 150000,
    };

    private static readonly object CreditCard = new
    {
        name = "Cartão Itaú Visa", type = "CreditCard", initialBalanceCents = 0,
        closingDay = 5, dueDay = 12, creditLimitCents = 500000,
    };

    private static async Task<AccountDto> Create(HttpClient client, object body)
    {
        var response = await client.PostAsJsonAsync("/accounts", body);
        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        return (await response.Content.ReadFromJsonAsync<AccountDto>())!;
    }

    private static async Task<List<AccountDto>> List(HttpClient client) =>
        (await client.GetFromJsonAsync<List<AccountDto>>("/accounts"))!;

    private async Task<(DateTime CreatedAt, DateTime UpdatedAt, DateTime? DeletedAt)?> ReadRow(Guid id)
    {
        await using var connection = new NpgsqlConnection(postgres.ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            "SELECT created_at, updated_at, deleted_at FROM accounts WHERE id = @id", connection);
        command.Parameters.AddWithValue("id", id);
        await using var reader = await command.ExecuteReaderAsync();
        if (!await reader.ReadAsync())
            return null;
        return (reader.GetDateTime(0), reader.GetDateTime(1), reader.IsDBNull(2) ? null : reader.GetDateTime(2));
    }

    [Fact]
    public async Task Creates_a_checking_account_and_lists_it()
    {
        await using var factory = new PrismaApiFactory(postgres.ConnectionString);
        using var client = await factory.CreateAuthenticatedClientAsync();

        var created = await Create(client, Checking);

        created.ShouldBe(new AccountDto(created.Id, "Itaú Personnalité", "Checking", 150000, null, null, null, true));
        (await List(client)).ShouldBe([created]);
    }

    [Fact]
    public async Task Creates_a_credit_card_with_closing_and_due_day()
    {
        await using var factory = new PrismaApiFactory(postgres.ConnectionString);
        using var client = await factory.CreateAuthenticatedClientAsync();

        var created = await Create(client, CreditCard);

        created.ShouldBe(new AccountDto(created.Id, "Cartão Itaú Visa", "CreditCard", 0, 5, 12, 500000, true));
    }

    [Fact]
    public async Task Credit_card_without_closing_day_is_rejected_in_portuguese()
    {
        await using var factory = new PrismaApiFactory(postgres.ConnectionString);
        using var client = await factory.CreateAuthenticatedClientAsync();

        var response = await client.PostAsJsonAsync("/accounts",
            new { name = "Cartão", type = "CreditCard", initialBalanceCents = 0, dueDay = 12 });

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        problem!.Detail.ShouldBe("Cartão de crédito exige dia de fechamento.");
        (await List(client)).ShouldBeEmpty();
    }

    [Theory]
    [InlineData("\"Poupanca\"")]
    [InlineData("1")]
    public async Task Unknown_account_type_is_rejected(string typeJson)
    {
        await using var factory = new PrismaApiFactory(postgres.ConnectionString);
        using var client = await factory.CreateAuthenticatedClientAsync();

        var response = await client.PostAsync("/accounts", new StringContent(
            $$"""{ "name": "Conta", "type": {{typeJson}}, "initialBalanceCents": 0 }""",
            System.Text.Encoding.UTF8, "application/json"));

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await List(client)).ShouldBeEmpty();
    }

    [Fact]
    public async Task Updates_the_editable_fields()
    {
        await using var factory = new PrismaApiFactory(postgres.ConnectionString);
        using var client = await factory.CreateAuthenticatedClientAsync();
        var card = await Create(client, CreditCard);

        var response = await client.PatchAsJsonAsync($"/accounts/{card.Id}", new
        {
            name = "Visa Infinite", initialBalanceCents = -1000, closingDay = 10, dueDay = 20,
            creditLimitCents = (long?)null, isActive = false,
        });

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var expected = new AccountDto(card.Id, "Visa Infinite", "CreditCard", -1000, 10, 20, null, false);
        (await response.Content.ReadFromJsonAsync<AccountDto>()).ShouldBe(expected);
        (await List(client)).ShouldBe([expected]);
    }

    [Fact]
    public async Task Update_that_breaks_an_invariant_is_rejected_and_changes_nothing()
    {
        await using var factory = new PrismaApiFactory(postgres.ConnectionString);
        using var client = await factory.CreateAuthenticatedClientAsync();
        var card = await Create(client, CreditCard);

        var response = await client.PatchAsJsonAsync($"/accounts/{card.Id}", new
        {
            name = "Outro nome", initialBalanceCents = 0, closingDay = (int?)null, dueDay = 12,
            creditLimitCents = 500000, isActive = true,
        });

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await response.Content.ReadFromJsonAsync<ProblemDetails>())!.Detail
            .ShouldBe("Cartão de crédito exige dia de fechamento.");
        (await List(client)).ShouldBe([card]);
    }

    [Fact]
    public async Task Delete_is_a_soft_delete()
    {
        await using var factory = new PrismaApiFactory(postgres.ConnectionString);
        using var client = await factory.CreateAuthenticatedClientAsync();
        var account = await Create(client, Checking);

        var response = await client.DeleteAsync($"/accounts/{account.Id}");

        response.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await List(client)).ShouldBeEmpty();
        (await ReadRow(account.Id))!.Value.DeletedAt.ShouldNotBeNull();

        // Excluída, a conta deixa de existir para a API.
        (await client.DeleteAsync($"/accounts/{account.Id}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await client.PatchAsJsonAsync($"/accounts/{account.Id}", new
        {
            name = "Volta", initialBalanceCents = 0, isActive = true,
        })).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Unknown_id_returns_404_in_portuguese()
    {
        await using var factory = new PrismaApiFactory(postgres.ConnectionString);
        using var client = await factory.CreateAuthenticatedClientAsync();

        var response = await client.DeleteAsync($"/accounts/{Guid.NewGuid()}");

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await response.Content.ReadFromJsonAsync<ProblemDetails>())!.Detail.ShouldBe("Conta não encontrada.");
    }

    [Fact]
    public async Task Audit_dates_come_from_the_clock()
    {
        var t1 = new DateTime(2026, 3, 1, 12, 0, 0, DateTimeKind.Utc);
        var clock = new FakeClock(t1);
        await using var factory = new PrismaApiFactory(postgres.ConnectionString, clock: clock);
        using var client = await factory.CreateAuthenticatedClientAsync();

        var account = await Create(client, Checking);
        (await ReadRow(account.Id)).ShouldBe((t1, t1, null));

        var t2 = t1.AddDays(1);
        clock.UtcNow = t2;
        await client.PatchAsJsonAsync($"/accounts/{account.Id}", new
        {
            name = "Renomeada", initialBalanceCents = 0, isActive = true,
        });
        (await ReadRow(account.Id)).ShouldBe((t1, t2, null));

        var t3 = t2.AddDays(1);
        clock.UtcNow = t3;
        await client.DeleteAsync($"/accounts/{account.Id}");
        (await ReadRow(account.Id)).ShouldBe((t1, t3, t3));
    }

    [Fact]
    public async Task Accounts_of_another_user_are_invisible()
    {
        await using var factory = new PrismaApiFactory(postgres.ConnectionString);
        using var owner = await factory.CreateAuthenticatedClientAsync();
        using var intruder = await factory.CreateAuthenticatedClientAsync();
        var account = await Create(owner, CreditCard);

        (await List(intruder)).ShouldBeEmpty();
        (await intruder.PatchAsJsonAsync($"/accounts/{account.Id}", new
        {
            name = "Invadida", initialBalanceCents = 0, closingDay = 1, dueDay = 10, isActive = true,
        })).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await intruder.DeleteAsync($"/accounts/{account.Id}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        (await List(owner)).ShouldBe([account]);
    }

    [Fact]
    public async Task Requires_authentication()
    {
        await using var factory = new PrismaApiFactory(postgres.ConnectionString);
        using var client = factory.CreateHttpsClient();

        (await client.GetAsync("/accounts")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await client.PostAsJsonAsync("/accounts", Checking)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }
}
