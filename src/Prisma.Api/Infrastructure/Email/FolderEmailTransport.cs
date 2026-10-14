using System.Text;
using System.Text.Encodings.Web;
using System.Text.Unicode;
using Microsoft.Extensions.Options;
using Prisma.Domain;

namespace Prisma.Api.Infrastructure.Email;

// Desenvolvimento: em vez de enviar, grava cada e-mail como .html numa pasta local (Email:Folder, fora do git), com
// remetente, destinatário e assunto no topo. Abre-se no navegador e clica-se no link como no e-mail de verdade.
public sealed class FolderEmailTransport(IOptions<EmailOptions> options, IHostEnvironment environment, IClock clock)
    : IEmailTransport
{
    public async Task<EmailSendResult> SendAsync(EmailMessage message, CancellationToken ct)
    {
        var folder = Path.Combine(environment.ContentRootPath, options.Value.Folder);
        Directory.CreateDirectory(folder);

        var id = Guid.CreateVersion7().ToString("N");
        var path = Path.Combine(folder, $"{clock.UtcNow:yyyyMMdd-HHmmss}-{id[^8..]}.html");
        var header = $"""
            <pre style="font:13px monospace;background:#eee;padding:12px;margin:0 0 16px">De: {Encode(message.From)}
            Responder para: {Encode(message.ReplyTo)}
            Para: {Encode(message.To)}
            Assunto: {Encode(message.Subject)}</pre>
            """;
        await File.WriteAllTextAsync(path, header + message.Html, Encoding.UTF8, ct);
        await File.WriteAllTextAsync(Path.ChangeExtension(path, ".txt"), message.Text, Encoding.UTF8, ct);
        return EmailSendResult.Sent($"folder:{id}");
    }

    // Só a marcação é escapada: os acentos ficam legíveis no arquivo.
    private static readonly HtmlEncoder Encoder = HtmlEncoder.Create(UnicodeRanges.All);

    private static string Encode(string value) => Encoder.Encode(value);
}
