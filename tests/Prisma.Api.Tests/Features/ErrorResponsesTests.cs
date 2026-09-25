using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc;
using Prisma.Api.Tests.Infrastructure;
using Prisma.Domain;
using Shouldly;

namespace Prisma.Api.Tests.Features;

// Toda resposta de erro sai como ProblemDetails em pt-BR, inclusive a exceção que ninguém previu.
[Collection(ApiCollection.Name)]
public sealed class ErrorResponsesTests(PostgresFixture postgres)
{
    // Relógio que passa a falhar sob comando: provoca uma exceção dentro de um endpoint qualquer.
    private sealed class BrokenClock : IClock
    {
        public bool Broken { get; set; }

        public DateTime UtcNow => Broken ? throw new InvalidOperationException("relógio quebrado") : DateTime.UtcNow;
    }

    private static async Task<ProblemDetails> Problem(HttpResponseMessage response, HttpStatusCode status)
    {
        response.StatusCode.ShouldBe(status);
        response.Content.Headers.ContentType?.MediaType.ShouldBe("application/problem+json");
        return (await response.Content.ReadFromJsonAsync<ProblemDetails>())!;
    }

    [Fact]
    public async Task An_unexpected_exception_is_a_500_in_portuguese_without_internal_details()
    {
        var clock = new BrokenClock();
        await using var factory = new PrismaApiFactory(postgres.ConnectionString, clock: clock);
        using var client = await factory.CreateAuthenticatedClientAsync();
        clock.Broken = true;

        var problem = await Problem(await client.GetAsync("/dashboard/summary"), HttpStatusCode.InternalServerError);

        problem.Title.ShouldBe("Erro inesperado.");
        problem.Detail.ShouldBe("Algo deu errado do nosso lado. Tente novamente em instantes.");
        (await (await client.GetAsync("/dashboard/summary")).Content.ReadAsStringAsync()).ShouldNotContain("relógio quebrado");
    }

    [Fact]
    public async Task Unknown_route_and_missing_session_also_answer_with_problem_details()
    {
        await using var factory = new PrismaApiFactory(postgres.ConnectionString);
        using var client = await factory.CreateAuthenticatedClientAsync();
        using var anonymous = factory.CreateHttpsClient();

        await Problem(await client.GetAsync("/nao-existe"), HttpStatusCode.NotFound);
        await Problem(await anonymous.GetAsync("/accounts"), HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task A_body_that_cannot_be_read_is_a_400_in_portuguese()
    {
        await using var factory = new PrismaApiFactory(postgres.ConnectionString);
        using var client = await factory.CreateAuthenticatedClientAsync();

        var response = await client.PostAsync("/accounts", new StringContent(
            "{ \"name\": ", System.Text.Encoding.UTF8, "application/json"));

        (await Problem(response, HttpStatusCode.BadRequest)).Detail
            .ShouldBe("Os dados enviados estão em um formato que a API não entende.");
    }
}
