using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Npgsql;
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
        client.PostAsJsonAsync("/auth/register", new { email, password, acceptedTerms = true });

    private static Task<HttpResponseMessage> Login(HttpClient client, string email, string password = Password) =>
        client.PostAsJsonAsync("/auth/login", new { email, password });

    private sealed record Registered(string Email);

    // docs/email.md, 5.1: o cadastro não entra. A conta nasce sem o e-mail confirmado, com a confirmação na fila.
    [Fact]
    public async Task Register_creates_the_account_without_signing_in()
    {
        await using var factory = CreateFactory();
        using var client = factory.CreateHttpsClient();
        var email = NewEmail();

        var response = await Register(client, email);

        response.StatusCode.ShouldBe(HttpStatusCode.Accepted);
        (await response.Content.ReadFromJsonAsync<Registered>())!.Email.ShouldBe(email);
        response.Headers.Contains("Set-Cookie").ShouldBeFalse();
        (await client.GetAsync("/auth/me")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    // Login com a senha certa e o e-mail sem confirmar: recusado, com um código para a tela oferecer o "Reenviar".
    [Fact]
    public async Task Login_before_confirming_is_refused_with_its_code()
    {
        await using var factory = CreateFactory();
        var email = NewEmail();
        await Register(factory.CreateHttpsClient(), email);
        using var client = factory.CreateHttpsClient();

        var response = await Login(client, email);

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        problem!.Detail.ShouldBe("Falta confirmar seu e-mail. Abra o link que enviamos ou peça outro.");
        problem.Extensions["code"]!.ToString().ShouldBe("EmailNotConfirmed");
        response.Headers.Contains("Set-Cookie").ShouldBeFalse();
        (await client.GetAsync("/auth/me")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    // Com a senha errada, a resposta é a de sempre: o estado da conta só aparece para quem sabe a senha.
    [Fact]
    public async Task Wrong_password_before_confirming_does_not_reveal_the_account()
    {
        await using var factory = CreateFactory();
        var email = NewEmail();
        await Register(factory.CreateHttpsClient(), email);
        using var client = factory.CreateHttpsClient();

        var response = await Login(client, email, "senha-errada-999");

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        problem!.Detail.ShouldBe("E-mail ou senha inválidos.");
        problem.Extensions.ShouldNotContainKey("code");
    }

    // O bloqueio por tentativas vale também para quem não confirmou: a confirmação não abre um atalho para adivinhar.
    [Fact]
    public async Task Five_wrong_passwords_lock_an_unconfirmed_account_too()
    {
        await using var factory = CreateFactory();
        var email = NewEmail();
        await Register(factory.CreateHttpsClient(), email);
        using var client = factory.CreateHttpsClient();

        for (var i = 0; i < 4; i++)
            (await Login(client, email, "senha-errada-999")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await Login(client, email, "senha-errada-999")).StatusCode.ShouldBe(HttpStatusCode.TooManyRequests);

        (await Login(client, email)).StatusCode.ShouldBe(HttpStatusCode.TooManyRequests);
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
        await factory.ConfirmEmailAsync(email);
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
        await factory.ConfirmEmailAsync(email);
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
        using var client = await factory.CreateAuthenticatedClientAsync();

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
        await factory.ConfirmEmailAsync(email);
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

    // O bloqueio dura 15 minutos e depois acaba sozinho, com a contagem zerada: a senha certa entra, e uma senha errada
    // a mais não bloqueia de novo na hora. O Identity confere o fim do bloqueio pelo relógio do sistema; o teste não
    // espera 15 minutos: confere o prazo gravado e depois o leva para o passado, como se o tempo tivesse passado.
    [Fact]
    public async Task Lockout_lasts_15_minutes_and_then_the_account_signs_in_again()
    {
        await using var factory = CreateFactory();
        var email = NewEmail();
        await Register(factory.CreateHttpsClient(), email);
        await factory.ConfirmEmailAsync(email);
        using var client = factory.CreateHttpsClient();
        for (var i = 0; i < 5; i++)
            await Login(client, email, "senha-errada-999");

        var remaining = await Sql<TimeSpan>("SELECT lockout_end - now() FROM users WHERE email = @email", email);
        remaining.ShouldBeInRange(TimeSpan.FromMinutes(14), TimeSpan.FromMinutes(15));
        (await Sql<int>("SELECT access_failed_count FROM users WHERE email = @email", email)).ShouldBe(0);
        (await Login(client, email)).StatusCode.ShouldBe(HttpStatusCode.TooManyRequests);

        await Sql<int>("UPDATE users SET lockout_end = now() - interval '1 second' WHERE email = @email RETURNING 0", email);

        (await Login(client, email, "senha-errada-999")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await Login(client, email)).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    // Etapa H.4e: a senha errada só soma com as anteriores se vier até 15 minutos depois da última. Quem erra de vez em
    // quando (hoje uma, amanhã outra) não acaba bloqueado por uma soma de dias. O prazo corre pelo IClock.
    [Fact]
    public async Task Wrong_passwords_spread_over_time_do_not_add_up()
    {
        var clock = new FakeClock(new DateTime(2026, 11, 3, 15, 0, 0, DateTimeKind.Utc));
        await using var factory = new PrismaApiFactory(postgres.ConnectionString, clock: clock);
        var email = NewEmail();
        await Register(factory.CreateHttpsClient(), email);
        await factory.ConfirmEmailAsync(email);
        using var client = factory.CreateHttpsClient();
        for (var i = 0; i < 4; i++)
            (await Login(client, email, "senha-errada-999")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

        clock.UtcNow = clock.UtcNow.AddMinutes(16);
        for (var i = 0; i < 4; i++)
            (await Login(client, email, "senha-errada-999")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

        (await Login(client, email)).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    // O outro lado da janela: errar de novo antes dos 15 minutos soma, e a 5ª bloqueia como antes.
    [Fact]
    public async Task Wrong_passwords_within_15_minutes_of_the_last_one_add_up()
    {
        var clock = new FakeClock(new DateTime(2026, 11, 3, 15, 0, 0, DateTimeKind.Utc));
        await using var factory = new PrismaApiFactory(postgres.ConnectionString, clock: clock);
        var email = NewEmail();
        await Register(factory.CreateHttpsClient(), email);
        await factory.ConfirmEmailAsync(email);
        using var client = factory.CreateHttpsClient();
        for (var i = 0; i < 4; i++)
        {
            (await Login(client, email, "senha-errada-999")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
            clock.UtcNow = clock.UtcNow.AddMinutes(14);
        }

        (await Login(client, email, "senha-errada-999")).StatusCode.ShouldBe(HttpStatusCode.TooManyRequests);
        (await Login(client, email)).StatusCode.ShouldBe(HttpStatusCode.TooManyRequests);
    }

    private async Task<T> Sql<T>(string sql, string email)
    {
        await using var connection = new NpgsqlConnection(postgres.ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("email", email);
        return (T)(await command.ExecuteScalarAsync())!;
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
