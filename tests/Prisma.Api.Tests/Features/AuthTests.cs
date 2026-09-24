using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Prisma.Api.Tests.Infrastructure;
using Shouldly;

namespace Prisma.Api.Tests.Features;

[Collection(ApiCollection.Name)]
public sealed class AuthTests(PostgresFixture postgres)
{
    private const string Password = PrismaApiFactory.Password;

    private sealed record UserResponse(Guid Id, string Email);

    private PrismaApiFactory CreateFactory() => new(postgres.ConnectionString);

    private static string NewEmail() => $"{Guid.NewGuid():N}@teste.com.br";

    private static Task<HttpResponseMessage> Register(HttpClient client, string email, string password = Password) =>
        client.PostAsJsonAsync("/auth/register", new { email, password });

    private static Task<HttpResponseMessage> Login(HttpClient client, string email, string password = Password) =>
        client.PostAsJsonAsync("/auth/login", new { email, password });

    [Fact]
    public async Task Register_creates_the_account_and_signs_in()
    {
        await using var factory = CreateFactory();
        using var client = factory.CreateHttpsClient();
        var email = NewEmail();

        var response = await Register(client, email);

        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        var created = await response.Content.ReadFromJsonAsync<UserResponse>();
        created!.Email.ShouldBe(email);

        var me = await client.GetFromJsonAsync<UserResponse>("/auth/me");
        me.ShouldBe(created);
    }

    [Fact]
    public async Task Register_with_an_existing_email_returns_409_in_portuguese()
    {
        await using var factory = CreateFactory();
        using var client = factory.CreateHttpsClient();
        var email = NewEmail();
        await Register(client, email);

        // Caixa diferente: o Identity normaliza, então é o mesmo e-mail.
        var response = await Register(factory.CreateHttpsClient(), email.ToUpperInvariant());

        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        problem!.Detail.ShouldBe("Já existe uma conta com este e-mail.");
    }

    [Theory]
    [InlineData("nao-e-email", Password, "Email", "E-mail inválido.")]
    [InlineData("", Password, "Email", "Informe o e-mail.")]
    [InlineData("valido@teste.com.br", "curta", "Password", "A senha deve ter pelo menos 8 caracteres.")]
    public async Task Register_with_invalid_data_returns_400_in_portuguese(
        string email, string password, string field, string message)
    {
        await using var factory = CreateFactory();
        using var client = factory.CreateHttpsClient();

        var response = await Register(client, email, password);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var problem = await response.Content.ReadFromJsonAsync<HttpValidationProblemDetails>();
        problem!.Errors[field].ShouldContain(message);
    }

    [Fact]
    public async Task Login_sets_an_httponly_secure_lax_session_cookie()
    {
        await using var factory = CreateFactory();
        var email = NewEmail();
        await Register(factory.CreateHttpsClient(), email);
        using var client = factory.CreateHttpsClient();

        var response = await Login(client, email);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var cookie = response.Headers.GetValues("Set-Cookie")
            .Single(c => c.StartsWith(".AspNetCore.Identity.Application="));
        cookie.ShouldContain("httponly", Case.Insensitive);
        cookie.ShouldContain("secure", Case.Insensitive);
        cookie.ShouldContain("samesite=lax", Case.Insensitive);

        var me = await client.GetFromJsonAsync<UserResponse>("/auth/me");
        me!.Email.ShouldBe(email);
    }

    [Fact]
    public async Task Login_with_wrong_password_returns_401_without_session()
    {
        await using var factory = CreateFactory();
        var email = NewEmail();
        await Register(factory.CreateHttpsClient(), email);
        using var client = factory.CreateHttpsClient();

        var response = await Login(client, email, "senha-errada-999");

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        problem!.Detail.ShouldBe("E-mail ou senha inválidos.");
        response.Headers.Contains("Set-Cookie").ShouldBeFalse();
        (await client.GetAsync("/auth/me")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Login_with_unknown_email_gives_the_same_answer_as_wrong_password()
    {
        await using var factory = CreateFactory();
        using var client = factory.CreateHttpsClient();

        var response = await Login(client, NewEmail());

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        problem!.Detail.ShouldBe("E-mail ou senha inválidos.");
    }

    [Fact]
    public async Task Protected_endpoint_without_session_returns_401_instead_of_redirecting()
    {
        await using var factory = CreateFactory();
        using var client = factory.CreateHttpsClient();

        var response = await client.GetAsync("/auth/me");

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Logout_ends_the_session()
    {
        await using var factory = CreateFactory();
        using var client = factory.CreateHttpsClient();
        await Register(client, NewEmail());

        var response = await client.PostAsync("/auth/logout", null);

        response.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await client.GetAsync("/auth/me")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Five_wrong_passwords_lock_the_account_even_with_the_right_password()
    {
        await using var factory = CreateFactory();
        var email = NewEmail();
        await Register(factory.CreateHttpsClient(), email);
        using var client = factory.CreateHttpsClient();

        const string lockedMessage =
            "Conta bloqueada temporariamente por excesso de tentativas. Tente novamente em alguns minutos.";

        for (var i = 0; i < 4; i++)
            (await Login(client, email, "senha-errada-999")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

        // A 5ª senha errada já bloqueia e avisa na mesma resposta.
        var fifthWrong = await Login(client, email, "senha-errada-999");
        fifthWrong.StatusCode.ShouldBe(HttpStatusCode.TooManyRequests);
        (await fifthWrong.Content.ReadFromJsonAsync<ProblemDetails>())!.Detail.ShouldBe(lockedMessage);

        var rightPassword = await Login(client, email);

        rightPassword.StatusCode.ShouldBe(HttpStatusCode.TooManyRequests);
        (await rightPassword.Content.ReadFromJsonAsync<ProblemDetails>())!.Detail.ShouldBe(lockedMessage);
        rightPassword.Headers.Contains("Set-Cookie").ShouldBeFalse();
    }

    [Fact]
    public async Task Rate_limit_blocks_the_sixth_attempt_within_a_minute()
    {
        // Sem sobrescrever o limite: verifica o valor padrão de produção (5 por minuto).
        await using var factory = new PrismaApiFactory(postgres.ConnectionString, settings: new Dictionary<string, string>());
        using var client = factory.CreateHttpsClient();

        for (var i = 0; i < 5; i++)
            (await Login(client, NewEmail())).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

        var response = await Login(client, NewEmail());

        response.StatusCode.ShouldBe(HttpStatusCode.TooManyRequests);
        response.Headers.RetryAfter.ShouldNotBeNull();
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        problem!.Detail.ShouldBe("Muitas tentativas. Aguarde um minuto e tente novamente.");
    }
}
