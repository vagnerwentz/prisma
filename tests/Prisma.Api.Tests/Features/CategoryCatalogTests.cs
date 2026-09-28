using System.Net;
using System.Net.Http.Json;
using Npgsql;
using Prisma.Api.Tests.Infrastructure;
using Shouldly;

namespace Prisma.Api.Tests.Features;

// docs/fase-2.md, 2.11 (etapa 2.23, tarefa 2): cada usuário recebe uma vez as categorias das versões novas
// do catálogo, ao pedir a lista, sem perder o que personalizou nem ver voltar o que excluiu.
[Collection(ApiCollection.Name)]
public sealed class CategoryCatalogTests(PostgresFixture postgres)
{
    private sealed record MeDto(Guid Id, string Email);

    private sealed record CategoryDto(Guid Id, string Name, string Type, Guid? ParentCategoryId);

    private sealed record NodeDto(Guid Id, string Name, string Type, List<CategoryDto> Subcategories);

    private static async Task<List<NodeDto>> Tree(HttpClient client) =>
        (await client.GetFromJsonAsync<List<NodeDto>>("/categories"))!;

    private static async Task<Guid> UserId(HttpClient client) =>
        (await client.GetFromJsonAsync<MeDto>("/auth/me"))!.Id;

    private async Task<object?> Scalar(string sql, Guid userId)
    {
        await using var connection = new NpgsqlConnection(postgres.ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("user", userId);
        return await command.ExecuteScalarAsync();
    }

    // Deixa o usuário como os cadastros anteriores à 2.23 gravaram: versão 1, sem Seguros e sem chaves.
    private async Task<Guid> MakeOld(HttpClient client)
    {
        var userId = await UserId(client);
        await Scalar("""
            DELETE FROM categories WHERE user_id = @user AND template_key LIKE 'expense.insurance%';
            UPDATE categories SET template_key = NULL WHERE user_id = @user;
            UPDATE users SET category_catalog_version = 1 WHERE id = @user;
            """, userId);
        return userId;
    }

    private async Task<int> VersionOf(Guid userId) =>
        (int)(await Scalar("SELECT category_catalog_version FROM users WHERE id = @user", userId))!;

    private static int Insurances(List<NodeDto> tree) => tree.Count(n => n.Type == "Expense" && n.Name == "Seguros");

    // O pai do dono: cadastrado antes, vê Seguros na próxima vez que o app pedir as categorias.
    [Fact]
    public async Task An_old_user_sees_insurance_once()
    {
        await using var factory = new PrismaApiFactory(postgres.ConnectionString);
        using var client = await factory.CreateAuthenticatedClientAsync();
        var userId = await MakeOld(client);

        var tree = await Tree(client);

        Insurances(tree).ShouldBe(1);
        tree.Single(n => n.Name == "Seguros").Subcategories.Select(s => s.Name)
            .ShouldBe(["Residencial", "Veicular", "Vida"]);
        (await VersionOf(userId)).ShouldBe(2);
        ((long)(await Scalar("SELECT count(*) FROM categories WHERE user_id = @user AND template_key IS NULL", userId))!)
            .ShouldBe(0);

        Insurances(await Tree(client)).ShouldBe(1);
    }

    [Fact]
    public async Task A_new_user_is_born_on_the_current_version()
    {
        await using var factory = new PrismaApiFactory(postgres.ConnectionString);
        using var client = await factory.CreateAuthenticatedClientAsync();
        var userId = await UserId(client);

        (await VersionOf(userId)).ShouldBe(2);
        Insurances(await Tree(client)).ShouldBe(1);
    }

    // Regra 3: recebeu, excluiu, e não volta.
    [Fact]
    public async Task Insurance_deleted_after_receiving_does_not_come_back()
    {
        await using var factory = new PrismaApiFactory(postgres.ConnectionString);
        using var client = await factory.CreateAuthenticatedClientAsync();
        await MakeOld(client);
        var insurance = (await Tree(client)).Single(n => n.Name == "Seguros");

        foreach (var sub in insurance.Subcategories)
            (await client.DeleteAsync($"/categories/{sub.Id}")).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await client.DeleteAsync($"/categories/{insurance.Id}")).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        Insurances(await Tree(client)).ShouldBe(0);
    }

    // Regra 4: a pessoa já tinha criado "Seguros" com "Vida"; adota, só acrescenta o que falta.
    [Fact]
    public async Task An_insurance_category_created_by_hand_is_adopted()
    {
        await using var factory = new PrismaApiFactory(postgres.ConnectionString);
        using var client = await factory.CreateAuthenticatedClientAsync();
        await MakeOld(client);
        var own = (await (await client.PostAsJsonAsync("/categories", new { name = "Seguros", type = "Expense" }))
            .Content.ReadFromJsonAsync<CategoryDto>())!;
        await client.PostAsJsonAsync("/categories", new { name = "Vida", type = "Expense", parentCategoryId = own.Id });

        var tree = await Tree(client);

        Insurances(tree).ShouldBe(1);
        var insurance = tree.Single(n => n.Name == "Seguros");
        insurance.Id.ShouldBe(own.Id);
        insurance.Subcategories.Select(s => s.Name).ShouldBe(["Residencial", "Veicular", "Vida"]);
    }

    // Regra 6: duas abas pedem a lista ao mesmo tempo; nenhuma falha e nada duplica.
    [Fact]
    public async Task Two_lists_at_once_do_not_duplicate()
    {
        await using var factory = new PrismaApiFactory(postgres.ConnectionString);

        for (var round = 0; round < 5; round++)
        {
            using var client = await factory.CreateAuthenticatedClientAsync();
            var userId = await MakeOld(client);

            var responses = await Task.WhenAll(client.GetAsync("/categories"), client.GetAsync("/categories"));

            responses.ShouldAllBe(r => r.StatusCode == HttpStatusCode.OK);
            Insurances(await Tree(client)).ShouldBe(1);
            ((long)(await Scalar("SELECT count(*) FROM categories WHERE user_id = @user AND name = 'Vida' AND deleted_at IS NULL", userId))!)
                .ShouldBe(1);
        }
    }

    [Fact]
    public async Task Syncing_one_user_does_not_touch_another()
    {
        await using var factory = new PrismaApiFactory(postgres.ConnectionString);
        using var old = await factory.CreateAuthenticatedClientAsync();
        using var other = await factory.CreateAuthenticatedClientAsync();
        await MakeOld(other);
        var otherId = await UserId(other);
        var before = (long)(await Scalar("SELECT count(*) FROM categories WHERE user_id = @user", otherId))!;
        await MakeOld(old);

        await Tree(old);

        ((long)(await Scalar("SELECT count(*) FROM categories WHERE user_id = @user", otherId))!).ShouldBe(before);
        (await VersionOf(otherId)).ShouldBe(1);
    }
}
