using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc;
using Prisma.Api.Tests.Infrastructure;
using Shouldly;

namespace Prisma.Api.Tests.Features;

[Collection(ApiCollection.Name)]
public sealed class CategoriesTests(PostgresFixture postgres)
{
    private sealed record CategoryDto(Guid Id, string Name, string Type, Guid? ParentCategoryId, string? Icon, string? Color);

    private sealed record NodeDto(Guid Id, string Name, string Type, string? Icon, string? Color, List<CategoryDto> Subcategories);

    private static async Task<List<NodeDto>> Tree(HttpClient client) =>
        (await client.GetFromJsonAsync<List<NodeDto>>("/categories"))!;

    private static async Task<NodeDto> Root(HttpClient client, string type, string name) =>
        (await Tree(client)).Single(n => n.Type == type && n.Name == name);

    private static Task<HttpResponseMessage> Post(HttpClient client, string name, string type, Guid? parentId = null) =>
        client.PostAsJsonAsync("/categories", new { name, type, parentCategoryId = parentId });

    private static async Task<CategoryDto> Create(HttpClient client, string name, string type, Guid? parentId = null)
    {
        var response = await Post(client, name, type, parentId);
        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        return (await response.Content.ReadFromJsonAsync<CategoryDto>())!;
    }

    private static async Task ShouldBeProblem(HttpResponseMessage response, HttpStatusCode status, string detail)
    {
        response.StatusCode.ShouldBe(status);
        (await response.Content.ReadFromJsonAsync<ProblemDetails>())!.Detail.ShouldBe(detail);
    }

    [Fact]
    public async Task New_user_is_born_with_the_default_categories()
    {
        await using var factory = new PrismaApiFactory(postgres.ConnectionString);
        using var client = await factory.CreateAuthenticatedClientAsync();

        var tree = await Tree(client);

        // docs/fase-1.md, 2.4, mais Seguros com 3 subcategorias (docs/fase-2.md, 2.11, versão 2 do catálogo).
        tree.Count(n => n.Type == "Expense").ShouldBe(11);
        tree.Count(n => n.Type == "Income").ShouldBe(5);
        tree.Sum(n => n.Subcategories.Count).ShouldBe(34);

        // "Outros" existe nos dois tipos.
        tree.Where(n => n.Name == "Outros").Select(n => n.Type).ShouldBe(["Income", "Expense"], ignoreOrder: true);

        // Subcategorias em ordem alfabética do português.
        (await Root(client, "Expense", "Moradia")).Subcategories.Select(s => s.Name)
            .ShouldBe(["Água", "Aluguel", "Condomínio", "Energia", "Gás", "Internet"]);
    }

    [Fact]
    public async Task Each_user_gets_an_own_copy()
    {
        await using var factory = new PrismaApiFactory(postgres.ConnectionString);
        using var first = await factory.CreateAuthenticatedClientAsync();
        using var second = await factory.CreateAuthenticatedClientAsync();

        var firstIds = (await Tree(first)).Select(n => n.Id);
        var secondIds = (await Tree(second)).Select(n => n.Id);

        firstIds.Intersect(secondIds).ShouldBeEmpty();
    }

    [Fact]
    public async Task Creates_a_category_and_a_subcategory()
    {
        await using var factory = new PrismaApiFactory(postgres.ConnectionString);
        using var client = await factory.CreateAuthenticatedClientAsync();

        var pets = await Create(client, "Pets", "Expense");
        var vet = await Create(client, "Veterinário", "Expense", pets.Id);

        vet.ParentCategoryId.ShouldBe(pets.Id);
        (await Root(client, "Expense", "Pets")).Subcategories.ShouldBe([vet]);
    }

    [Fact]
    public async Task Subcategory_of_another_type_or_level_is_rejected()
    {
        await using var factory = new PrismaApiFactory(postgres.ConnectionString);
        using var client = await factory.CreateAuthenticatedClientAsync();
        var food = await Root(client, "Expense", "Alimentação");
        var market = food.Subcategories.Single(s => s.Name == "Mercado");

        await ShouldBeProblem(await Post(client, "Bônus", "Income", food.Id),
            HttpStatusCode.BadRequest, "A subcategoria deve ter o mesmo tipo da categoria pai.");
        await ShouldBeProblem(await Post(client, "Hortifruti", "Expense", market.Id),
            HttpStatusCode.BadRequest, "Subcategoria não pode ter subcategorias.");
        await ShouldBeProblem(await Post(client, "Transferências", "Transfer"),
            HttpStatusCode.BadRequest, "A categoria deve ser de receita ou de despesa.");
    }

    [Fact]
    public async Task Duplicate_name_on_the_same_level_is_rejected_ignoring_case()
    {
        await using var factory = new PrismaApiFactory(postgres.ConnectionString);
        using var client = await factory.CreateAuthenticatedClientAsync();
        var food = await Root(client, "Expense", "Alimentação");

        await ShouldBeProblem(await Post(client, "mercado", "Expense", food.Id),
            HttpStatusCode.Conflict, "Já existe uma categoria com este nome neste nível.");
        await ShouldBeProblem(await Post(client, "LAZER", "Expense"),
            HttpStatusCode.Conflict, "Já existe uma categoria com este nome neste nível.");

        // Mesmo nome em outro pai ou outro tipo é permitido.
        await Create(client, "Mercado", "Expense", (await Root(client, "Expense", "Compras")).Id);
        await Create(client, "Lazer", "Income");
    }

    [Fact]
    public async Task Updates_name_icon_and_color()
    {
        await using var factory = new PrismaApiFactory(postgres.ConnectionString);
        using var client = await factory.CreateAuthenticatedClientAsync();
        var leisure = await Root(client, "Expense", "Lazer");

        var response = await client.PatchAsJsonAsync($"/categories/{leisure.Id}",
            new { name = "Diversão", icon = "party-popper", color = "#AA00FF" });

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var updated = await Root(client, "Expense", "Diversão");
        (updated.Icon, updated.Color).ShouldBe(("party-popper", "#AA00FF"));
        updated.Subcategories.Count.ShouldBe(4);
    }

    [Fact]
    public async Task Rename_to_an_existing_name_is_rejected()
    {
        await using var factory = new PrismaApiFactory(postgres.ConnectionString);
        using var client = await factory.CreateAuthenticatedClientAsync();
        var leisure = await Root(client, "Expense", "Lazer");

        await ShouldBeProblem(
            await client.PatchAsJsonAsync($"/categories/{leisure.Id}", new { name = "Compras" }),
            HttpStatusCode.Conflict, "Já existe uma categoria com este nome neste nível.");
        (await Root(client, "Expense", "Lazer")).Id.ShouldBe(leisure.Id);
    }

    [Fact]
    public async Task Category_with_subcategories_cannot_be_deleted_until_they_are_gone()
    {
        await using var factory = new PrismaApiFactory(postgres.ConnectionString);
        using var client = await factory.CreateAuthenticatedClientAsync();
        var services = await Root(client, "Expense", "Serviços");

        await ShouldBeProblem(await client.DeleteAsync($"/categories/{services.Id}"),
            HttpStatusCode.Conflict, "Esta categoria tem subcategorias. Exclua as subcategorias antes.");

        foreach (var sub in services.Subcategories)
            (await client.DeleteAsync($"/categories/{sub.Id}")).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        (await client.DeleteAsync($"/categories/{services.Id}")).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await Tree(client)).ShouldNotContain(n => n.Id == services.Id);
    }

    [Fact]
    public async Task Deleted_name_can_be_reused()
    {
        await using var factory = new PrismaApiFactory(postgres.ConnectionString);
        using var client = await factory.CreateAuthenticatedClientAsync();
        var taxes = await Root(client, "Expense", "Impostos e Tarifas");

        await client.DeleteAsync($"/categories/{taxes.Id}");

        var recreated = await Create(client, "Impostos e Tarifas", "Expense");
        recreated.Id.ShouldNotBe(taxes.Id);
    }

    [Fact]
    public async Task Categories_of_another_user_are_invisible()
    {
        await using var factory = new PrismaApiFactory(postgres.ConnectionString);
        using var owner = await factory.CreateAuthenticatedClientAsync();
        using var intruder = await factory.CreateAuthenticatedClientAsync();
        var pets = await Create(owner, "Pets", "Expense");

        (await Tree(intruder)).ShouldNotContain(n => n.Id == pets.Id);
        (await intruder.PatchAsJsonAsync($"/categories/{pets.Id}", new { name = "Invadida" }))
            .StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await intruder.DeleteAsync($"/categories/{pets.Id}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        await ShouldBeProblem(await Post(intruder, "Filhote", "Expense", pets.Id),
            HttpStatusCode.BadRequest, "Categoria pai não encontrada.");

        (await Root(owner, "Expense", "Pets")).Subcategories.ShouldBeEmpty();
    }

    [Fact]
    public async Task Requires_authentication()
    {
        await using var factory = new PrismaApiFactory(postgres.ConnectionString);
        using var client = factory.CreateHttpsClient();

        (await client.GetAsync("/categories")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }
}
