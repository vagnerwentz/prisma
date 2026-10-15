using System.Net;
using System.Net.Http.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Prisma.Api.Infrastructure;
using Prisma.Api.Infrastructure.Auth;
using Prisma.Api.Infrastructure.Email;
using Prisma.Api.Tests.Email;
using Prisma.Api.Tests.Infrastructure;
using Shouldly;

namespace Prisma.Api.Tests.Features;

// docs/email.md, 5.1 (etapa H.4b): cadastro com confirmação de e-mail. O link do e-mail é lido do que o transporte
// falso recebeu, como a pessoa o leria.
[Collection(ApiCollection.Name)]
public sealed partial class EmailConfirmationTests(PostgresFixture postgres)
{
    private const string Password = PrismaApiFactory.Password;
    private const string PublicUrl = "https://prisma.teste/";

    private sealed record Registered(string Email);

    private sealed record UserResponse(Guid Id, string Email);

    private sealed record Link(Guid UserId, string Token);

    private static readonly DateTime Start = new(2026, 11, 2, 15, 0, 0, DateTimeKind.Utc);

    private readonly FakeClock _clock = new(Start);

    private PrismaApiFactory Factory(Action<IServiceCollection>? services = null) => new(postgres.ConnectionString,
        new Dictionary<string, string>
        {
            ["RateLimiting:Auth:PermitLimit"] = "1000",
            ["App:PublicUrl"] = PublicUrl,
            ["Email:Outbox:BatchSize"] = "500",
        }, _clock, services: services);

    private static string NewEmail() => $"{Guid.NewGuid():N}@teste.com.br";

    private static Task<HttpResponseMessage> Register(HttpClient client, string email, string password = Password) =>
        client.PostAsJsonAsync("/auth/register", new { email, password, acceptedTerms = true });

    private static Task<HttpResponseMessage> Login(HttpClient client, string email, string password = Password) =>
        client.PostAsJsonAsync("/auth/login", new { email, password });

    private static Task<HttpResponseMessage> Confirm(HttpClient client, Link link, string password = Password) =>
        client.PostAsJsonAsync("/auth/confirm-email", new { userId = link.UserId, token = link.Token, password });

    private static Task SendQueued(PrismaApiFactory factory) =>
        factory.Services.GetRequiredService<EmailOutboxProcessor>().RunOnceAsync(CancellationToken.None);

    private static FakeEmailTransport Transport(PrismaApiFactory factory) =>
        factory.Services.GetRequiredService<FakeEmailTransport>();

    // O token vai depois do "#": não chega a servidor nenhum ao abrir o link.
    [GeneratedRegex(@"https://prisma\.teste/confirmar-email#u=([0-9a-f-]{36})&t=([A-Za-z0-9_-]+)")]
    private static partial Regex ConfirmLink();

    private static Link LinkIn(EmailMessage message)
    {
        var match = ConfirmLink().Match(message.Text);
        match.Success.ShouldBeTrue("O e-mail não traz o link de confirmação.");
        return new Link(Guid.Parse(match.Groups[1].Value), match.Groups[2].Value);
    }

    private static async Task<string?> CodeOf(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<ProblemDetails>())!.Extensions.TryGetValue("code", out var code)
            ? code?.ToString()
            : null;

    [Fact]
    public async Task Signing_up_sends_the_link_that_confirms_and_signs_in()
    {
        await using var factory = Factory();
        var email = NewEmail();
        (await Register(factory.CreateHttpsClient(), email)).StatusCode.ShouldBe(HttpStatusCode.Accepted);

        await SendQueued(factory);

        var link = LinkIn(Transport(factory).To(email).ShouldHaveSingleItem());
        using var browser = factory.CreateHttpsClient();
        var confirmed = await Confirm(browser, link);

        confirmed.StatusCode.ShouldBe(HttpStatusCode.OK);
        var user = (await confirmed.Content.ReadFromJsonAsync<UserResponse>())!;
        user.ShouldBe(new UserResponse(link.UserId, email));
        // Entra direto: o link provou que o e-mail é dela.
        (await browser.GetFromJsonAsync<UserResponse>("/auth/me")).ShouldBe(user);
        // E daqui em diante entra com a senha.
        (await Login(factory.CreateHttpsClient(), email)).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task The_confirmation_email_carries_the_identity_the_link_and_its_deadline()
    {
        await using var factory = Factory();
        var email = NewEmail();
        await Register(factory.CreateHttpsClient(), email);

        await SendQueued(factory);

        var message = Transport(factory).To(email).ShouldHaveSingleItem();
        message.From.ShouldBe("Prisma Money <nao-responda@prismamoney.com.br>");
        message.ReplyTo.ShouldBe("contato@prismamoney.com.br");
        message.Subject.ShouldBe("Confirme seu e-mail no Prisma Money");
        var link = LinkIn(message);
        message.Html.ShouldContain($"href=\"https://prisma.teste/confirmar-email#u={link.UserId}&amp;t={link.Token}\"");
        message.Html.ShouldContain("src=\"https://prisma.teste/email/prisma-logo.png\"");
        message.Html.ShouldContain("Confirmar e-mail");
        // 3 dias depois do envio, na hora de São Paulo: 02/11 às 12h (15h UTC) → 05/11 às 12h.
        message.Html.ShouldContain("05/11/2026, às 12:00");
        message.Text.ShouldContain("Link válido até: 05/11/2026, às 12:00");
        message.Text.ShouldContain($"Conta: {email}");
    }

    [Fact]
    public async Task A_link_works_only_once()
    {
        await using var factory = Factory();
        var email = NewEmail();
        await Register(factory.CreateHttpsClient(), email);
        await SendQueued(factory);
        var link = LinkIn(Transport(factory).To(email).Single());
        (await Confirm(factory.CreateHttpsClient(), link)).StatusCode.ShouldBe(HttpStatusCode.OK);

        using var other = factory.CreateHttpsClient();
        var again = await Confirm(other, link);

        again.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await CodeOf(again)).ShouldBe("InvalidLink");
        again.Headers.Contains("Set-Cookie").ShouldBeFalse();
        (await other.GetAsync("/auth/me")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    // Pediu outro e confirmou por ele: o primeiro, que ainda estava no prazo, deixa de valer.
    [Fact]
    public async Task Confirming_invalidates_the_other_links_already_sent()
    {
        await using var factory = Factory();
        var email = NewEmail();
        await Register(factory.CreateHttpsClient(), email);
        await SendQueued(factory);
        _clock.UtcNow = Start.AddMinutes(2);
        (await factory.CreateHttpsClient().PostAsJsonAsync("/auth/resend-confirmation", new { email }))
            .StatusCode.ShouldBe(HttpStatusCode.Accepted);
        await SendQueued(factory);
        var links = Transport(factory).To(email).Select(LinkIn).ToList();
        links.Count.ShouldBe(2);
        links[0].Token.ShouldNotBe(links[1].Token);

        (await Confirm(factory.CreateHttpsClient(), links[1])).StatusCode.ShouldBe(HttpStatusCode.OK);

        var older = await Confirm(factory.CreateHttpsClient(), links[0]);
        older.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await CodeOf(older)).ShouldBe("InvalidLink");
    }

    [Fact]
    public async Task A_tampered_or_unknown_link_is_refused_the_same_way()
    {
        await using var factory = Factory();
        var email = NewEmail();
        await Register(factory.CreateHttpsClient(), email);
        await SendQueued(factory);
        var link = LinkIn(Transport(factory).To(email).Single());
        var otherEmail = NewEmail();
        await Register(factory.CreateHttpsClient(), otherEmail);
        var otherUser = await factory.ConfirmEmailAsync(otherEmail);

        Link[] refused =
        [
            link with { Token = link.Token[..^4] + "AAAA" },
            link with { Token = "não é base64!" },
            link with { UserId = Guid.NewGuid() },
            link with { UserId = otherUser },
        ];
        foreach (var bad in refused)
        {
            var response = await Confirm(factory.CreateHttpsClient(), bad);
            response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
            var problem = (await response.Content.ReadFromJsonAsync<ProblemDetails>())!;
            problem.Detail.ShouldBe("Este link não vale mais. Peça outro abaixo ou entre, se já confirmou.");
            problem.Extensions["code"]!.ToString().ShouldBe("InvalidLink");
        }

        // O link certo continua valendo depois das tentativas.
        (await Confirm(factory.CreateHttpsClient(), link)).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    // Confirmar pede a senha do cadastro: o link prova o e-mail, a senha prova quem criou a conta.
    [Fact]
    public async Task Confirming_with_the_wrong_password_keeps_the_account_waiting()
    {
        await using var factory = Factory();
        var email = NewEmail();
        await Register(factory.CreateHttpsClient(), email);
        await SendQueued(factory);
        var link = LinkIn(Transport(factory).To(email).Single());
        using var browser = factory.CreateHttpsClient();

        var wrong = await Confirm(browser, link, "senha-errada-999");

        wrong.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        var problem = (await wrong.Content.ReadFromJsonAsync<ProblemDetails>())!;
        problem.Detail.ShouldBe("A senha não confere com a do cadastro.");
        problem.Extensions["code"]!.ToString().ShouldBe("WrongPassword");
        wrong.Headers.Contains("Set-Cookie").ShouldBeFalse();
        (await Login(factory.CreateHttpsClient(), email)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        // O link não foi gasto: com a senha certa, confirma.
        (await Confirm(browser, link)).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    // Pré-sequestro: alguém se cadastra antes com o e-mail de outra pessoa e uma senha sua. Quando a dona do e-mail se
    // cadastra e abre o link, a senha dela não confere: a conta não é confirmada, e a senha do intruso não abre nada.
    [Fact]
    public async Task Someone_who_signed_up_first_with_your_email_cannot_take_your_account()
    {
        await using var factory = Factory();
        var victim = NewEmail();
        await Register(factory.CreateHttpsClient(), victim, "senha-do-intruso");
        await SendQueued(factory);
        _clock.UtcNow = Start.AddMinutes(2);
        (await Register(factory.CreateHttpsClient(), victim, "senha-da-dona-1")).StatusCode.ShouldBe(HttpStatusCode.Accepted);
        await SendQueued(factory);
        var link = LinkIn(Transport(factory).To(victim).Last());

        (await Confirm(factory.CreateHttpsClient(), link, "senha-da-dona-1")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

        (await Login(factory.CreateHttpsClient(), victim, "senha-do-intruso")).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        using var scope = factory.Services.CreateScope();
        (await scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>().FindByEmailAsync(victim))!
            .EmailConfirmed.ShouldBeFalse();
    }

    [Fact]
    public async Task Five_wrong_passwords_on_the_link_lock_the_account_like_the_login()
    {
        await using var factory = Factory();
        var email = NewEmail();
        await Register(factory.CreateHttpsClient(), email);
        await SendQueued(factory);
        var link = LinkIn(Transport(factory).To(email).Single());
        using var client = factory.CreateHttpsClient();

        for (var i = 0; i < 4; i++)
            (await Confirm(client, link, "senha-errada-999")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await Confirm(client, link, "senha-errada-999")).StatusCode.ShouldBe(HttpStatusCode.TooManyRequests);

        (await Confirm(client, link)).StatusCode.ShouldBe(HttpStatusCode.TooManyRequests);
    }

    // Link inválido nem chega a testar a senha: não serve para adivinhar senha nem gasta as tentativas da dona.
    [Fact]
    public async Task An_invalid_link_does_not_test_the_password_nor_count_an_attempt()
    {
        await using var factory = Factory();
        var email = NewEmail();
        await Register(factory.CreateHttpsClient(), email);
        await SendQueued(factory);
        var link = LinkIn(Transport(factory).To(email).Single());
        var forged = link with { Token = link.Token[..^4] + "AAAA" };

        var withRightPassword = await Confirm(factory.CreateHttpsClient(), forged);
        var withWrongPassword = await Confirm(factory.CreateHttpsClient(), forged, "senha-errada-999");

        withRightPassword.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        withWrongPassword.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        using var scope = factory.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
        (await users.GetAccessFailedCountAsync((await users.FindByEmailAsync(email))!)).ShouldBe(0);
    }

    [Fact]
    public void The_link_lasts_three_days() =>
        new ConfirmEmailTokenProviderOptions().TokenLifespan.ShouldBe(TimeSpan.FromDays(3));

    // O provedor de token do Identity lê o relógio do sistema: o prazo é encurtado aqui, e o de produção, conferido acima.
    [Fact]
    public async Task An_expired_link_is_refused()
    {
        await using var factory = Factory(services =>
            services.Configure<ConfirmEmailTokenProviderOptions>(o => o.TokenLifespan = TimeSpan.FromSeconds(1)));
        var email = NewEmail();
        await Register(factory.CreateHttpsClient(), email);
        await SendQueued(factory);
        var link = LinkIn(Transport(factory).To(email).Single());

        await Task.Delay(TimeSpan.FromSeconds(1.5));
        var response = await Confirm(factory.CreateHttpsClient(), link);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await CodeOf(response)).ShouldBe("InvalidLink");
    }

    // Cadastro com um e-mail que já tem conta: a resposta é igual à de um cadastro novo, nada muda na conta, e o dono
    // do e-mail fica sabendo.
    [Fact]
    public async Task Signing_up_with_a_registered_email_warns_its_owner_and_changes_nothing()
    {
        await using var factory = Factory();
        var email = NewEmail();
        await Register(factory.CreateHttpsClient(), email);
        var ownerId = await factory.ConfirmEmailAsync(email);

        // Caixa diferente: o Identity normaliza, então é o mesmo e-mail.
        var response = await Register(factory.CreateHttpsClient(), email.ToUpperInvariant(), "outra-senha-456");

        response.StatusCode.ShouldBe(HttpStatusCode.Accepted);
        (await response.Content.ReadFromJsonAsync<Registered>())!.Email.ShouldBe(email.ToUpperInvariant());
        response.Headers.Contains("Set-Cookie").ShouldBeFalse();

        await SendQueued(factory);
        var warning = Transport(factory).To(email).ShouldHaveSingleItem();
        warning.Subject.ShouldBe("Alguém tentou criar uma conta com seu e-mail");
        warning.To.ShouldBe(email);
        warning.Html.ShouldContain("href=\"https://prisma.teste/entrar\"");
        ConfirmLink().IsMatch(warning.Text).ShouldBeFalse();

        (await Login(factory.CreateHttpsClient(), email)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await Login(factory.CreateHttpsClient(), email, "outra-senha-456")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        using var scope = factory.Services.CreateScope();
        (await scope.ServiceProvider.GetRequiredService<AppDbContext>().Users
            .CountAsync(u => u.NormalizedEmail == email.ToUpperInvariant())).ShouldBe(1);
        (await scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>().FindByIdAsync(ownerId.ToString()))!
            .EmailConfirmed.ShouldBeTrue();
    }

    // Quem se cadastrou e não confirmou, e se cadastra de novo, recebe a confirmação outra vez (não o aviso).
    [Fact]
    public async Task Signing_up_again_before_confirming_sends_the_confirmation_again()
    {
        await using var factory = Factory();
        var email = NewEmail();
        await Register(factory.CreateHttpsClient(), email);
        await SendQueued(factory);

        _clock.UtcNow = Start.AddMinutes(2);
        (await Register(factory.CreateHttpsClient(), email)).StatusCode.ShouldBe(HttpStatusCode.Accepted);
        await SendQueued(factory);

        var messages = Transport(factory).To(email);
        messages.Count.ShouldBe(2);
        messages.ShouldAllBe(m => m.Subject == "Confirme seu e-mail no Prisma Money");
        (await Confirm(factory.CreateHttpsClient(), LinkIn(messages[1]))).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Resending_answers_the_same_and_only_sends_to_who_still_has_to_confirm()
    {
        await using var factory = Factory();
        var pending = NewEmail();
        var confirmed = NewEmail();
        var unknown = NewEmail();
        await Register(factory.CreateHttpsClient(), pending);
        await Register(factory.CreateHttpsClient(), confirmed);
        await factory.ConfirmEmailAsync(confirmed);
        await SendQueued(factory);
        _clock.UtcNow = Start.AddMinutes(2);
        using var client = factory.CreateHttpsClient();

        foreach (var email in new[] { pending, confirmed, unknown })
            (await client.PostAsJsonAsync("/auth/resend-confirmation", new { email })).StatusCode.ShouldBe(HttpStatusCode.Accepted);
        // Duas vezes no mesmo minuto: um e-mail só (limite da fila).
        (await client.PostAsJsonAsync("/auth/resend-confirmation", new { email = pending })).StatusCode.ShouldBe(HttpStatusCode.Accepted);
        await SendQueued(factory);

        Transport(factory).To(pending).Count.ShouldBe(2);
        Transport(factory).To(confirmed).ShouldBeEmpty();
        Transport(factory).To(unknown).ShouldBeEmpty();
        // Nem entra na fila: quem já confirmou não gasta o limite nem vira linha pulada.
        using var scope = factory.Services.CreateScope();
        (await scope.ServiceProvider.GetRequiredService<AppDbContext>().OutboxEmails
            .CountAsync(e => e.ToAddress == confirmed && e.CreatedAt > Start)).ShouldBe(0);
    }

    [Fact]
    public async Task Resending_validates_the_address()
    {
        await using var factory = Factory();

        var response = await factory.CreateHttpsClient().PostAsJsonAsync("/auth/resend-confirmation", new { email = "nao-e-email" });

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await response.Content.ReadFromJsonAsync<HttpValidationProblemDetails>())!.Errors["Email"].ShouldContain("E-mail inválido.");
    }

    // Confirmou pelo primeiro e-mail antes de a fila mandar o segundo: o segundo não sai.
    [Fact]
    public async Task A_confirmation_still_queued_after_confirming_is_not_sent()
    {
        await using var factory = Factory();
        var email = NewEmail();
        await Register(factory.CreateHttpsClient(), email);
        await SendQueued(factory);
        _clock.UtcNow = Start.AddMinutes(2);
        await factory.CreateHttpsClient().PostAsJsonAsync("/auth/resend-confirmation", new { email });

        (await Confirm(factory.CreateHttpsClient(), LinkIn(Transport(factory).To(email).Single()))).StatusCode.ShouldBe(HttpStatusCode.OK);
        await SendQueued(factory);

        Transport(factory).To(email).ShouldHaveSingleItem();
        using var scope = factory.Services.CreateScope();
        var statuses = await scope.ServiceProvider.GetRequiredService<AppDbContext>().OutboxEmails
            .Where(e => e.ToAddress == email).OrderBy(e => e.CreatedAt).Select(e => e.Status).ToListAsync();
        statuses.ShouldBe([EmailStatus.Sent, EmailStatus.Skipped]);
    }

    // Quem se cadastrou antes da confirmação passa a constar como confirmado: ninguém fica de fora no deploy.
    [Fact]
    public async Task The_migration_confirms_the_accounts_that_already_exist()
    {
        const string before = "20261013231809_AddOutboxEmails";
        var database = $"migration_{Guid.NewGuid():N}";
        await Execute(postgres.ConnectionString, $"CREATE DATABASE {database}");
        var connectionString = new NpgsqlConnectionStringBuilder(postgres.ConnectionString) { Database = database }.ConnectionString;
        try
        {
            await using var factory = new PrismaApiFactory(connectionString);
            using var scope = factory.Services.CreateScope();
            var migrator = scope.ServiceProvider.GetRequiredService<AppDbContext>().GetService<IMigrator>();
            await migrator.MigrateAsync(before);
            await Execute(connectionString, """
                INSERT INTO users (id, user_name, normalized_user_name, email, normalized_email, email_confirmed, phone_number_confirmed,
                                   two_factor_enabled, lockout_enabled, access_failed_count, category_catalog_version)
                VALUES ('0199e3a2-0000-7000-8000-000000000001', 'antiga@teste.com.br', 'ANTIGA@TESTE.COM.BR', 'antiga@teste.com.br',
                        'ANTIGA@TESTE.COM.BR', false, false, false, true, 0, 2);
                """);

            await migrator.MigrateAsync();

            await using var connection = new NpgsqlConnection(connectionString);
            await connection.OpenAsync();
            await using var command = new NpgsqlCommand(
                "SELECT email_confirmed FROM users WHERE id = '0199e3a2-0000-7000-8000-000000000001'", connection);
            ((bool)(await command.ExecuteScalarAsync())!).ShouldBeTrue();
        }
        finally
        {
            NpgsqlConnection.ClearAllPools();
            await Execute(postgres.ConnectionString, $"DROP DATABASE IF EXISTS {database} WITH (FORCE)");
        }
    }

    private static async Task Execute(string connectionString, string sql)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }
}
