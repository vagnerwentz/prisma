using Microsoft.Extensions.Hosting.Internal;
using Microsoft.Extensions.Options;
using Prisma.Api.Infrastructure.Email;
using Prisma.Api.Tests.Infrastructure;
using Shouldly;

namespace Prisma.Api.Tests.Email;

// Desenvolvimento: o e-mail vira um .html (e um .txt) na pasta local, com remetente, destinatário e assunto no topo.
public sealed class FolderEmailTransportTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"prisma-emails-{Guid.NewGuid():N}");

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    [Fact]
    public async Task Writes_the_email_to_the_folder()
    {
        var transport = new FolderEmailTransport(
            Options.Create(new EmailOptions { Transport = EmailTransportKind.Folder, Folder = ".dev-emails" }),
            new HostingEnvironment { ContentRootPath = _root },
            new FakeClock(new DateTime(2026, 11, 2, 12, 30, 15, DateTimeKind.Utc)));

        var result = await transport.SendAsync(new EmailMessage(
            "Prisma Money <nao-responda@prismamoney.com.br>", "contato@prismamoney.com.br", "pessoa@exemplo.com.br",
            "Confirme <já>", "<p>Olá</p>", "Olá"), CancellationToken.None);

        result.Status.ShouldBe(EmailSendStatus.Sent);
        var html = Directory.GetFiles(Path.Combine(_root, ".dev-emails"), "*.html").ShouldHaveSingleItem();
        Path.GetFileName(html).ShouldStartWith("20261102-123015-");
        var content = await File.ReadAllTextAsync(html);
        content.ShouldContain("Para: pessoa@exemplo.com.br");
        content.ShouldContain("De: Prisma Money &lt;nao-responda@prismamoney.com.br&gt;");
        content.ShouldContain("Assunto: Confirme &lt;já&gt;");
        content.ShouldEndWith("<p>Olá</p>");
        (await File.ReadAllTextAsync(Path.ChangeExtension(html, ".txt"))).ShouldBe("Olá");
    }
}
