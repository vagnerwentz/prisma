using System.Net;
using System.Net.Http.Json;
using Prisma.Api.Tests.Infrastructure;
using Shouldly;

namespace Prisma.Api.Tests.Features;

// docs/patrimonio.md, I.9: "acompanhar o caixa" é opção da conta de investimento, desligada por padrão. A API só acrescenta: o pedido
// de uma tela aberta antes do deploy não manda o campo.
[Collection(ApiCollection.Name)]
public sealed class AccountTracksCashApiTests(PostgresFixture postgres)
{
    private sealed record AccountDto(Guid Id, string Name, string Type, bool IsActive, bool TracksCash);

    private sealed record ProblemDto(string? Detail);

    private async Task<(PrismaApiFactory, HttpClient)> Start()
    {
        var factory = new PrismaApiFactory(postgres.ConnectionString);
        return (factory, await factory.CreateAuthenticatedClientAsync());
    }

    private static async Task<AccountDto> Create(HttpClient client, object body)
    {
        var response = await client.PostAsJsonAsync("/accounts", body);
        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<AccountDto>())!;
    }

    private static Task<HttpResponseMessage> Patch(HttpClient client, Guid id, object body) =>
        client.SendAsync(new HttpRequestMessage(HttpMethod.Patch, $"/accounts/{id}") { Content = JsonContent.Create(body) });

    [Fact]
    public async Task Accounts_do_not_track_cash_by_default()
    {
        var (factory, client) = await Start();
        await using var _ = factory;

        var broker = await Create(client, new { name = "Íon", type = "Investment", initialBalanceCents = 0 });
        var checking = await Create(client, new { name = "Itaú", type = "Checking", initialBalanceCents = 0 });

        (broker.TracksCash, checking.TracksCash).ShouldBe((false, false));
        (await client.GetFromJsonAsync<List<AccountDto>>("/accounts"))!.ShouldAllBe(a => !a.TracksCash);
    }

    [Fact]
    public async Task An_investment_account_can_be_created_tracking_its_cash_and_it_comes_back_in_the_list()
    {
        var (factory, client) = await Start();
        await using var _ = factory;

        var broker = await Create(client, new { name = "Íon", type = "Investment", initialBalanceCents = 0, tracksCash = true });

        broker.TracksCash.ShouldBeTrue();
        (await client.GetFromJsonAsync<List<AccountDto>>("/accounts"))!.Single(a => a.Id == broker.Id).TracksCash.ShouldBeTrue();
    }

    [Fact]
    public async Task Editing_turns_the_tracking_on_and_off()
    {
        var (factory, client) = await Start();
        await using var _ = factory;
        var broker = await Create(client, new { name = "Íon", type = "Investment", initialBalanceCents = 0 });

        var on = await Patch(client, broker.Id, new { name = "Íon", initialBalanceCents = 0, isActive = true, tracksCash = true });
        on.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await on.Content.ReadFromJsonAsync<AccountDto>())!.TracksCash.ShouldBeTrue();

        var off = await Patch(client, broker.Id, new { name = "Íon", initialBalanceCents = 0, isActive = true, tracksCash = false });
        (await off.Content.ReadFromJsonAsync<AccountDto>())!.TracksCash.ShouldBeFalse();
    }

    // A tela aberta antes do deploy edita a conta sem o campo: a escolha da pessoa não pode voltar para desligada.
    [Fact]
    public async Task An_edit_without_the_field_keeps_the_choice()
    {
        var (factory, client) = await Start();
        await using var _ = factory;
        var broker = await Create(client, new { name = "Íon", type = "Investment", initialBalanceCents = 0, tracksCash = true });

        var response = await Patch(client, broker.Id, new { name = "Íon Itaú", initialBalanceCents = 100, isActive = true });

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var edited = (await response.Content.ReadFromJsonAsync<AccountDto>())!;
        (edited.Name, edited.TracksCash).ShouldBe(("Íon Itaú", true));
    }

    [Theory]
    [InlineData("Checking")]
    [InlineData("Cash")]
    public async Task Only_the_investment_account_can_track_cash(string type)
    {
        var (factory, client) = await Start();
        await using var _ = factory;

        var response = await client.PostAsJsonAsync("/accounts", new { name = "Conta", type, initialBalanceCents = 0, tracksCash = true });

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await response.Content.ReadFromJsonAsync<ProblemDto>())!.Detail.ShouldBe("Só a conta de investimento acompanha o caixa.");
        (await client.GetFromJsonAsync<List<AccountDto>>("/accounts"))!.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_checking_account_cannot_be_edited_to_track_cash()
    {
        var (factory, client) = await Start();
        await using var _ = factory;
        var checking = await Create(client, new { name = "Itaú", type = "Checking", initialBalanceCents = 0 });

        var response = await Patch(client, checking.Id, new { name = "Itaú", initialBalanceCents = 0, isActive = true, tracksCash = true });

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await client.GetFromJsonAsync<List<AccountDto>>("/accounts"))!.Single().TracksCash.ShouldBeFalse();
    }

    [Fact]
    public async Task Another_person_does_not_change_the_choice_of_someone_else()
    {
        var (factory, client) = await Start();
        await using var _ = factory;
        var broker = await Create(client, new { name = "Íon", type = "Investment", initialBalanceCents = 0 });
        using var stranger = await factory.CreateAuthenticatedClientAsync();

        var response = await Patch(stranger, broker.Id, new { name = "Íon", initialBalanceCents = 0, isActive = true, tracksCash = true });

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await client.GetFromJsonAsync<List<AccountDto>>("/accounts"))!.Single().TracksCash.ShouldBeFalse();
    }
}
