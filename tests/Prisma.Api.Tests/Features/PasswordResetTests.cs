using System.Net;
using System.Net.Http.Json;
using System.Text.RegularExpressions;
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

// docs/email.md, 5.2 (etapa H.4c): "Esqueci a senha". O link do e-mail é lido do que o transporte falso recebeu.
[Collection(ApiCollection.Name)]
public sealed partial class PasswordResetTests(PostgresFixture postgres)
{
    private const string Password = PrismaApiFactory.Password;
    private const string NewPassword = "senha-nova-456";

    private sealed record UserResponse(Guid Id, string Email);

    private sealed record Link(Guid UserId, string Token);

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

    private static Task<HttpResponseMessage> Forgot(HttpClient client, string email) =>
        client.PostAsJsonAsync("/auth/forgot-password", new { email });

    private static Task<HttpResponseMessage> Reset(HttpClient client, Link link, string password = NewPassword) =>
        client.PostAsJsonAsync("/auth/reset-password", new { userId = link.UserId, token = link.Token, password });

    private static Task SendQueued(PrismaApiFactory factory) =>
        factory.Services.GetRequiredService<EmailOutboxProcessor>().RunOnceAsync(CancellationToken.None);

    private static FakeEmailTransport Transport(PrismaApiFactory factory) =>
        factory.Services.GetRequiredService<FakeEmailTransport>();

    [GeneratedRegex(@"https://prisma\.teste/nova-senha#u=([0-9a-f-]{36})&t=([A-Za-z0-9_-]+)")]
    private static partial Regex ResetLink();

    private static Link LinkIn(EmailMessage message)
    {
        var match = ResetLink().Match(message.Text);
        match.Success.ShouldBeTrue("O e-mail não traz o link de senha nova.");
        return new Link(Guid.Parse(match.Groups[1].Value), match.Groups[2].Value);
    }

    private static async Task<string> ConfirmedUser(PrismaApiFactory factory)
    {
        var email = NewEmail();
        (await factory.CreateHttpsClient().PostAsJsonAsync("/auth/register", new { email, password = Password, acceptedTerms = true })).EnsureSuccessStatusCode();
        await factory.ConfirmEmailAsync(email);
        return email;
    }

    // Pede o link e devolve o e-mail recebido (descartando o da confirmação do cadastro).
    private async Task<EmailMessage> AskForLink(PrismaApiFactory factory, string email)
    {
        await SendQueued(factory);
        (await Forgot(factory.CreateHttpsClient(), email)).StatusCode.ShouldBe(HttpStatusCode.Accepted);
        await SendQueued(factory);
        return Transport(factory).To(email).Last(m => m.Subject == "Crie uma senha nova no Prisma Money");
    }

    [Fact]
    public async Task The_link_sets_a_new_password_and_signs_in()
    {
        await using var factory = Factory();
        var email = await ConfirmedUser(factory);
        var link = LinkIn(await AskForLink(factory, email));
        using var browser = factory.CreateHttpsClient();

        var reset = await Reset(browser, link);

        reset.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await reset.Content.ReadFromJsonAsync<UserResponse>()).ShouldBe(new UserResponse(link.UserId, email));
        (await browser.GetFromJsonAsync<UserResponse>("/auth/me"))!.Email.ShouldBe(email);
        (await Login(factory.CreateHttpsClient(), email, NewPassword)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await Login(factory.CreateHttpsClient(), email)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task The_email_carries_the_link_and_its_one_hour_deadline()
    {
        await using var factory = Factory();
        var email = await ConfirmedUser(factory);

        var message = await AskForLink(factory, email);

        var link = LinkIn(message);
        message.Html.ShouldContain($"href=\"https://prisma.teste/nova-senha#u={link.UserId}&amp;t={link.Token}\"");
        message.Html.ShouldContain("Criar senha nova");
        // 1 hora depois do envio, na hora de São Paulo: 03/11 às 12h (15h UTC) → 13h.
        message.Text.ShouldContain("Link válido até: 03/11/2026, às 13:00");
        message.Text.ShouldContain($"Conta: {email}");
    }

    [Fact]
    public void The_link_lasts_one_hour() =>
        new ResetPasswordTokenProviderOptions().TokenLifespan.ShouldBe(TimeSpan.FromHours(1));

    // A resposta não conta quem tem conta, e quem não tem não recebe nada.
    [Fact]
    public async Task Asking_answers_the_same_for_an_unknown_address()
    {
        await using var factory = Factory();
        var unknown = NewEmail();

        var response = await Forgot(factory.CreateHttpsClient(), unknown);

        response.StatusCode.ShouldBe(HttpStatusCode.Accepted);
        await SendQueued(factory);
        Transport(factory).To(unknown).ShouldBeEmpty();
    }

    [Fact]
    public async Task Asking_validates_the_address()
    {
        await using var factory = Factory();

        var response = await Forgot(factory.CreateHttpsClient(), "nao-e-email");

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await response.Content.ReadFromJsonAsync<HttpValidationProblemDetails>())!.Errors["Email"].ShouldContain("E-mail inválido.");
    }

    [Fact]
    public async Task One_link_per_minute()
    {
        await using var factory = Factory();
        var email = await ConfirmedUser(factory);
        await AskForLink(factory, email);

        (await Forgot(factory.CreateHttpsClient(), email)).StatusCode.ShouldBe(HttpStatusCode.Accepted);
        await SendQueued(factory);

        Transport(factory).To(email).Count(m => m.Subject == "Crie uma senha nova no Prisma Money").ShouldBe(1);
    }

    [Fact]
    public async Task A_link_works_only_once_and_older_links_die_with_the_change()
    {
        await using var factory = Factory();
        var email = await ConfirmedUser(factory);
        var first = LinkIn(await AskForLink(factory, email));
        _clock.UtcNow = Start.AddMinutes(2);
        var second = LinkIn(await AskForLink(factory, email));

        (await Reset(factory.CreateHttpsClient(), second)).StatusCode.ShouldBe(HttpStatusCode.OK);

        foreach (var used in new[] { second, first })
        {
            var response = await Reset(factory.CreateHttpsClient(), used, "outra-senha-789");
            response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
            var problem = (await response.Content.ReadFromJsonAsync<ProblemDetails>())!;
            problem.Detail.ShouldBe("Este link não vale mais. Peça outro abaixo.");
            problem.Extensions["code"]!.ToString().ShouldBe("InvalidLink");
        }

        (await Login(factory.CreateHttpsClient(), email, NewPassword)).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task A_tampered_or_foreign_link_is_refused()
    {
        await using var factory = Factory();
        var email = await ConfirmedUser(factory);
        var link = LinkIn(await AskForLink(factory, email));
        var other = await ConfirmedUser(factory);
        var otherId = (await factory.Services.CreateScope().ServiceProvider.GetRequiredService<UserManager<AppUser>>()
            .FindByEmailAsync(other))!.Id;

        Link[] refused =
        [
            link with { Token = link.Token[..^4] + "AAAA" },
            link with { Token = "não é base64!" },
            link with { UserId = Guid.NewGuid() },
            link with { UserId = otherId },
        ];
        foreach (var bad in refused)
            (await Reset(factory.CreateHttpsClient(), bad)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        (await Login(factory.CreateHttpsClient(), other)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await Reset(factory.CreateHttpsClient(), link)).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    // O token de senha nova não confirma e-mail, e o de confirmação não troca senha: cada um vale para a sua finalidade.
    [Fact]
    public async Task A_confirmation_link_does_not_reset_the_password()
    {
        await using var factory = Factory();
        var email = NewEmail();
        (await factory.CreateHttpsClient().PostAsJsonAsync("/auth/register", new { email, password = Password, acceptedTerms = true })).EnsureSuccessStatusCode();
        await SendQueued(factory);
        var confirmation = Regex.Match(Transport(factory).To(email).Single().Text,
            @"confirmar-email#u=([0-9a-f-]{36})&t=([A-Za-z0-9_-]+)");

        var response = await Reset(factory.CreateHttpsClient(),
            new Link(Guid.Parse(confirmation.Groups[1].Value), confirmation.Groups[2].Value));

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task An_expired_link_is_refused()
    {
        await using var factory = Factory(services =>
            services.Configure<ResetPasswordTokenProviderOptions>(o => o.TokenLifespan = TimeSpan.FromSeconds(1)));
        var email = await ConfirmedUser(factory);
        var link = LinkIn(await AskForLink(factory, email));

        await Task.Delay(TimeSpan.FromSeconds(1.5));

        (await Reset(factory.CreateHttpsClient(), link)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await Login(factory.CreateHttpsClient(), email)).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task The_new_password_follows_the_rules()
    {
        await using var factory = Factory();
        var email = await ConfirmedUser(factory);
        var link = LinkIn(await AskForLink(factory, email));

        var response = await Reset(factory.CreateHttpsClient(), link, "curta");

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await response.Content.ReadFromJsonAsync<HttpValidationProblemDetails>())!.Errors["Password"]
            .ShouldContain("A senha deve ter pelo menos 8 caracteres.");
        // O link não foi gasto.
        (await Reset(factory.CreateHttpsClient(), link)).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    // Quem esqueceu a senha costuma ter errado várias vezes antes: a conta bloqueada destrava.
    [Fact]
    public async Task Resetting_unlocks_a_locked_account()
    {
        await using var factory = Factory();
        var email = await ConfirmedUser(factory);
        for (var i = 0; i < 5; i++)
            await Login(factory.CreateHttpsClient(), email, "senha-errada-999");
        (await Login(factory.CreateHttpsClient(), email)).StatusCode.ShouldBe(HttpStatusCode.TooManyRequests);
        var link = LinkIn(await AskForLink(factory, email));

        (await Reset(factory.CreateHttpsClient(), link)).StatusCode.ShouldBe(HttpStatusCode.OK);

        (await Login(factory.CreateHttpsClient(), email, NewPassword)).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Changing_the_password_warns_by_email()
    {
        await using var factory = Factory();
        var email = await ConfirmedUser(factory);
        var link = LinkIn(await AskForLink(factory, email));

        await Reset(factory.CreateHttpsClient(), link);
        await SendQueued(factory);

        var warning = Transport(factory).To(email).Last();
        warning.Subject.ShouldBe("Sua senha do Prisma Money foi trocada");
        warning.Text.ShouldContain("Quando: 03/11/2026, às 12:00");
        warning.Html.ShouldContain("href=\"https://prisma.teste/esqueci-senha\"");
    }

    // A senha trocada derruba a sessão aberta em outro aparelho na conferência seguinte do cookie (aqui, imediata).
    [Fact]
    public async Task Other_sessions_end_after_the_change()
    {
        await using var factory = Factory(services =>
            services.Configure<SecurityStampValidatorOptions>(o => o.ValidationInterval = TimeSpan.Zero));
        var email = await ConfirmedUser(factory);
        using var otherDevice = factory.CreateHttpsClient();
        (await Login(otherDevice, email)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await otherDevice.GetAsync("/auth/me")).StatusCode.ShouldBe(HttpStatusCode.OK);
        var link = LinkIn(await AskForLink(factory, email));
        using var thisDevice = factory.CreateHttpsClient();

        (await Reset(thisDevice, link)).StatusCode.ShouldBe(HttpStatusCode.OK);

        (await otherDevice.GetAsync("/auth/me")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await thisDevice.GetAsync("/auth/me")).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public void Sessions_are_checked_every_five_minutes() =>
        AuthSetup.SessionCheckInterval.ShouldBe(TimeSpan.FromMinutes(5));

    // Fim do pré-sequestro (docs/email.md, 5.1): a dona do e-mail não sabe a senha do intruso, cria uma senha nova pelo
    // e-mail, a conta fica confirmada com a senha dela, e a do intruso não abre mais nada.
    [Fact]
    public async Task Resetting_takes_back_an_account_someone_created_with_your_email()
    {
        await using var factory = Factory();
        var victim = NewEmail();
        (await factory.CreateHttpsClient().PostAsJsonAsync("/auth/register", new { email = victim, password = "senha-do-intruso", acceptedTerms = true }))
            .EnsureSuccessStatusCode();
        var link = LinkIn(await AskForLink(factory, victim));

        (await Reset(factory.CreateHttpsClient(), link, "senha-da-dona-1")).StatusCode.ShouldBe(HttpStatusCode.OK);

        (await Login(factory.CreateHttpsClient(), victim, "senha-do-intruso")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await Login(factory.CreateHttpsClient(), victim, "senha-da-dona-1")).StatusCode.ShouldBe(HttpStatusCode.OK);
    }
}
