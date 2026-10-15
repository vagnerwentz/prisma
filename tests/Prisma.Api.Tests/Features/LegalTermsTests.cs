using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Prisma.Api.Infrastructure.Auth;
using Prisma.Api.Infrastructure.Privacy;
using Prisma.Api.Tests.Infrastructure;
using Shouldly;

namespace Prisma.Api.Tests.Features;

// Etapa A.4: o aceite dos termos de uso e da política de privacidade. Sem ele não há cadastro; quem já tinha conta (ou
// aceitou uma versão anterior) aceita ao entrar, e a API guarda as versões e o instante.
[Collection(ApiCollection.Name)]
public sealed class LegalTermsTests(PostgresFixture postgres)
{
    private sealed record SessionDto(Guid Id, string Email, bool MustAcceptTerms);

    private readonly FakeClock _clock = new(new DateTime(2026, 10, 15, 13, 0, 0, DateTimeKind.Utc));
    private readonly LogSink _logs = new();

    private PrismaApiFactory Factory() => new(postgres.ConnectionString, null, _clock, _logs);

    private static string NewEmail() => $"{Guid.NewGuid():N}@teste.com.br";

    private static async Task<AppUser?> Find(PrismaApiFactory factory, string email)
    {
        using var scope = factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>().FindByEmailAsync(email);
    }

    // Como estava quem se cadastrou antes da A.4, ou quem aceitou uma versão anterior.
    private static async Task SetAccepted(PrismaApiFactory factory, string email, string? termsVersion, string? privacyVersion)
    {
        using var scope = factory.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
        var user = (await users.FindByEmailAsync(email))!;
        user.TermsVersion = termsVersion;
        user.PrivacyVersion = privacyVersion;
        user.TermsAcceptedAt = termsVersion is null ? null : new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        (await users.UpdateAsync(user)).Succeeded.ShouldBeTrue();
    }

    private static async Task<HttpClient> SignedIn(PrismaApiFactory factory, string email)
    {
        var client = factory.CreateHttpsClient();
        (await client.PostAsJsonAsync("/auth/register", new { email, password = PrismaApiFactory.Password, acceptedTerms = true }))
            .StatusCode.ShouldBe(HttpStatusCode.Accepted);
        await factory.ConfirmEmailAsync(email);
        return client;
    }

    private static async Task<SessionDto> Login(HttpClient client, string email)
    {
        var response = await client.PostAsJsonAsync("/auth/login", new { email, password = PrismaApiFactory.Password });
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        return (await response.Content.ReadFromJsonAsync<SessionDto>())!;
    }

    [Theory]
    [InlineData(false)]
    [InlineData(null)]
    public async Task Sign_up_without_accepting_the_terms_creates_nothing(bool? accepted)
    {
        await using var factory = Factory();
        var email = NewEmail();
        object body = accepted is null
            ? new { email, password = PrismaApiFactory.Password }
            : new { email, password = PrismaApiFactory.Password, acceptedTerms = accepted };

        var response = await factory.CreateHttpsClient().PostAsJsonAsync("/auth/register", body);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var problem = await response.Content.ReadFromJsonAsync<HttpValidationProblemDetails>();
        problem!.Errors.Values.SelectMany(e => e).ShouldContain(Prisma.Api.Features.Auth.Register.AcceptTermsMessage);
        (await Find(factory, email)).ShouldBeNull();
    }

    [Fact]
    public async Task Sign_up_records_the_accepted_versions_and_when()
    {
        await using var factory = Factory();
        var email = NewEmail();
        var client = await SignedIn(factory, email);

        var user = (await Find(factory, email))!;
        (user.TermsVersion, user.PrivacyVersion).ShouldBe((LegalTerms.TermsVersion, LegalTerms.PrivacyVersion));
        user.TermsAcceptedAt.ShouldBe(_clock.UtcNow);
        (await Login(client, email)).MustAcceptTerms.ShouldBeFalse();
    }

    // Quem já tinha conta antes do aceite existir: entra, a sessão avisa, e o aceite grava as versões.
    [Fact]
    public async Task An_existing_account_without_acceptance_accepts_when_signing_in()
    {
        await using var factory = Factory();
        var email = NewEmail();
        var client = await SignedIn(factory, email);
        await SetAccepted(factory, email, null, null);

        (await Login(client, email)).MustAcceptTerms.ShouldBeTrue();
        (await client.GetFromJsonAsync<SessionDto>("/auth/me"))!.MustAcceptTerms.ShouldBeTrue();

        _clock.UtcNow = _clock.UtcNow.AddMinutes(5);
        var accepted = await client.PostAsync("/auth/accept-terms", null);

        accepted.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await accepted.Content.ReadFromJsonAsync<SessionDto>())!.MustAcceptTerms.ShouldBeFalse();
        (await client.GetFromJsonAsync<SessionDto>("/auth/me"))!.MustAcceptTerms.ShouldBeFalse();
        var user = (await Find(factory, email))!;
        (user.TermsVersion, user.PrivacyVersion, user.TermsAcceptedAt)
            .ShouldBe((LegalTerms.TermsVersion, LegalTerms.PrivacyVersion, _clock.UtcNow));
        _logs.Entries.ShouldContain(l => l.EventId.Name == "TermsAccepted" && Equals(l["UserId"], user.Id));
    }

    // Nova versão de um dos dois textos: quem aceitou a anterior aceita de novo.
    [Theory]
    [InlineData("0.9", LegalTerms.PrivacyVersion)]
    [InlineData(LegalTerms.TermsVersion, "0.9")]
    public async Task A_new_version_of_either_text_asks_for_acceptance_again(string termsVersion, string privacyVersion)
    {
        await using var factory = Factory();
        var email = NewEmail();
        var client = await SignedIn(factory, email);
        await SetAccepted(factory, email, termsVersion, privacyVersion);

        (await Login(client, email)).MustAcceptTerms.ShouldBeTrue();
    }

    [Fact]
    public async Task Accepting_needs_a_session()
    {
        await using var factory = Factory();

        (await factory.CreateHttpsClient().PostAsync("/auth/accept-terms", null)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }
}
