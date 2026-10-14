using Prisma.Api.Features.Auth;
using Prisma.Api.Infrastructure.Email;

namespace Prisma.Api.Tests.Email;

// Grava cada e-mail como .html para conferir o visual no navegador (e o .txt ao lado). Fora do `dotnet test`: rode ao
// mexer no layout ou num texto.
//
//   PRISMA_EMAIL_PREVIEW=/tmp/emails dotnet test tests/Prisma.Api.Tests --filter FullyQualifiedName~EmailPreview
public sealed class EmailPreviewTests
{
    private static readonly Uri PublicUrl = new("https://prismamoney.com.br/");

    public static TheoryData<string> Emails => new() { "confirm-email", "already-registered" };

    [PreviewTheory]
    [MemberData(nameof(Emails))]
    public async Task Writes_the_email(string name)
    {
        var template = name switch
        {
            "confirm-email" => AuthEmails.ConfirmEmail("pessoa@exemplo.com.br",
                AuthEmails.ConfirmEmailLink(PublicUrl, Guid.Parse("0199e3a2-6f1c-7d3e-9a41-5b8c2d7e4f10"), "CfDJ8Exemplo+de/token=="),
                new DateTime(2026, 10, 17, 0, 31, 0, DateTimeKind.Utc)),
            "already-registered" => AuthEmails.AlreadyRegistered("pessoa@exemplo.com.br", AuthEmails.SignInLink(PublicUrl)),
            _ => throw new ArgumentOutOfRangeException(nameof(name)),
        };
        var content = EmailLayout.Render(template, PublicUrl);

        var folder = Environment.GetEnvironmentVariable("PRISMA_EMAIL_PREVIEW")!;
        Directory.CreateDirectory(folder);
        await File.WriteAllTextAsync(Path.Combine(folder, $"{name}.html"), content.Html);
        await File.WriteAllTextAsync(Path.Combine(folder, $"{name}.txt"), $"Assunto: {content.Subject}\n\n{content.Text}");
    }
}

public sealed class PreviewTheoryAttribute : TheoryAttribute
{
    public PreviewTheoryAttribute()
    {
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("PRISMA_EMAIL_PREVIEW")))
            Skip = "Grava a prévia dos e-mails: rode com PRISMA_EMAIL_PREVIEW=<pasta>.";
    }
}
