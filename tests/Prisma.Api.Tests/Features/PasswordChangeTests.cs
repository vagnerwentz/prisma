using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Prisma.Api.Infrastructure.Auth;
using Prisma.Api.Infrastructure.Email;
using Prisma.Api.Tests.Email;
using Prisma.Api.Tests.Infrastructure;
using Shouldly;

namespace Prisma.Api.Tests.Features;

// docs/email.md, 5.3 (etapa H.4d): "Trocar senha", com a sessão aberta. Pede a senha atual; esta sessão continua, as
// outras caem, e vai o aviso de senha trocada.
[Collection(ApiCollection.Name)]
public sealed class PasswordChangeTests(PostgresFixture postgres)
{
    private const string Password = PrismaApiFactory.Password;
    private const string NewPassword = "senha-nova-456";
    private const string WrongPassword = "senha-errada-999";

    private static readonly DateTime Start = new(2026, 11, 3, 15, 0, 0, DateTimeKind.Utc);

    private readonly FakeClock _clock = new(Start);

    private PrismaApiFactory Factory(Action<IServiceCollection>? services = null) => new(postgres.ConnectionString,
        new Dictionary<string, string>
        {
            ["RateLimiting:Auth:PermitLimit"] = "1000",
            ["App:PublicUrl"] = "https://prisma.teste/",
            ["Email:Outbox:BatchSize"] = "500",
        }, _clock, services: services);

    private static string NewEmail() => $"{Guid.NewGuid():N}@teste.com.br";

    private static Task<HttpResponseMessage> Login(HttpClient client, string email, string password = Password) =>
        client.PostAsJsonAsync("/auth/login", new { email, password });

    private static Task<HttpResponseMessage> Change(HttpClient client, string currentPassword, string newPassword = NewPassword) =>
        client.PostAsJsonAsync("/auth/change-password", new { currentPassword, newPassword });

    private static Task SendQueued(PrismaApiFactory factory) =>
        factory.Services.GetRequiredService<EmailOutboxProcessor>().RunOnceAsync(CancellationToken.None);

    // Conta confirmada e com a sessão aberta neste cliente.
    private static async Task<(string Email, HttpClient Client)> SignedIn(PrismaApiFactory factory)
    {
        var email = NewEmail();
        (await factory.CreateHttpsClient().PostAsJsonAsync("/auth/register", new { email, password = Password, acceptedTerms = true }))
            .EnsureSuccessStatusCode();
        await factory.ConfirmEmailAsync(email);
        var client = factory.CreateHttpsClient();
        (await Login(client, email)).StatusCode.ShouldBe(HttpStatusCode.OK);
        return (email, client);
    }

    [Fact]
    public async Task Changing_sets_the_new_password_and_keeps_this_session()
    {
        await using var factory = Factory();
        var (email, client) = await SignedIn(factory);

        (await Change(client, Password)).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        (await client.GetAsync("/auth/me")).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await Login(factory.CreateHttpsClient(), email, NewPassword)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await Login(factory.CreateHttpsClient(), email)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    // A senha trocada derruba a sessão aberta em outro aparelho na conferência seguinte do cookie (aqui, imediata). A
    // desta tela ganha um cookie novo, com o selo novo, e continua.
    [Fact]
    public async Task Other_sessions_end_and_this_one_continues()
    {
        await using var factory = Factory(services =>
            services.Configure<SecurityStampValidatorOptions>(o => o.ValidationInterval = TimeSpan.Zero));
        var (email, thisDevice) = await SignedIn(factory);
        using var otherDevice = factory.CreateHttpsClient();
        (await Login(otherDevice, email)).StatusCode.ShouldBe(HttpStatusCode.OK);

        (await Change(thisDevice, Password)).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        (await otherDevice.GetAsync("/auth/me")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await thisDevice.GetAsync("/auth/me")).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Changing_warns_by_email()
    {
        await using var factory = Factory();
        var (email, client) = await SignedIn(factory);

        await Change(client, Password);
        await SendQueued(factory);

        var warning = factory.Services.GetRequiredService<FakeEmailTransport>().To(email).Last();
        warning.Subject.ShouldBe("Sua senha do Prisma Money foi trocada");
        warning.Text.ShouldContain("Quando: 03/11/2026, às 12:00");
    }

    // 400 com código, e não 401: na tela, o 401 leva ao login e derrubaria a sessão de quem só errou a senha.
    [Fact]
    public async Task A_wrong_current_password_is_refused_and_nothing_changes()
    {
        await using var factory = Factory();
        var (email, client) = await SignedIn(factory);

        var response = await Change(client, WrongPassword);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var problem = (await response.Content.ReadFromJsonAsync<ProblemDetails>())!;
        problem.Detail.ShouldBe("Senha atual incorreta.");
        problem.Extensions["code"]!.ToString().ShouldBe("WrongPassword");
        (await client.GetAsync("/auth/me")).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await Login(factory.CreateHttpsClient(), email)).StatusCode.ShouldBe(HttpStatusCode.OK);
        await SendQueued(factory);
        factory.Services.GetRequiredService<FakeEmailTransport>().To(email)
            .ShouldNotContain(m => m.Subject == "Sua senha do Prisma Money foi trocada");
    }

    // Senão a tela viraria um jeito de adivinhar a senha sem limite com uma sessão roubada. Conta junto com o login.
    [Fact]
    public async Task Wrong_current_passwords_count_for_the_lockout_with_the_login()
    {
        await using var factory = Factory();
        var (email, client) = await SignedIn(factory);
        for (var i = 0; i < 4; i++)
            (await Change(client, WrongPassword)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        (await Login(factory.CreateHttpsClient(), email, WrongPassword)).StatusCode.ShouldBe(HttpStatusCode.TooManyRequests);

        var locked = await Change(client, Password);
        locked.StatusCode.ShouldBe(HttpStatusCode.TooManyRequests);
        (await locked.Content.ReadFromJsonAsync<ProblemDetails>())!.Detail
            .ShouldBe("Muitas tentativas com a senha errada. Tente novamente em alguns minutos.");
        // Bloqueada, a senha atual certa não troca nada.
        using var scope = factory.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
        var user = (await users.FindByEmailAsync(email))!;
        (await users.CheckPasswordAsync(user, Password)).ShouldBeTrue();
    }

    [Fact]
    public async Task The_fifth_wrong_current_password_locks_on_the_same_answer()
    {
        await using var factory = Factory();
        var (_, client) = await SignedIn(factory);
        for (var i = 0; i < 4; i++)
            await Change(client, WrongPassword);

        (await Change(client, WrongPassword)).StatusCode.ShouldBe(HttpStatusCode.TooManyRequests);
    }

    [Fact]
    public async Task The_new_password_follows_the_rules()
    {
        await using var factory = Factory();
        var (email, client) = await SignedIn(factory);

        var response = await Change(client, Password, "curta");

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await response.Content.ReadFromJsonAsync<HttpValidationProblemDetails>())!.Errors["NewPassword"]
            .ShouldContain("A senha deve ter pelo menos 8 caracteres.");
        (await Login(factory.CreateHttpsClient(), email)).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Without_a_session_it_is_401()
    {
        await using var factory = Factory();

        (await Change(factory.CreateHttpsClient(), Password)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }
}
