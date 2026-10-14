using System.Collections.Concurrent;
using Prisma.Api.Infrastructure.Email;

namespace Prisma.Api.Tests.Email;

// No lugar do Resend nos testes: guarda o que seria enviado. Respond decide o resultado de cada envio (padrão: enviado).
public sealed class FakeEmailTransport : IEmailTransport
{
    private readonly ConcurrentQueue<EmailMessage> _sent = new();

    public Func<EmailMessage, Task<EmailSendResult>> Respond { get; set; } =
        _ => Task.FromResult(EmailSendResult.Sent($"fake-{Guid.NewGuid():N}"));

    // Tudo o que chegou ao transporte, inclusive os envios que ele respondeu com falha.
    public IReadOnlyList<EmailMessage> Messages => _sent.ToArray();

    public IReadOnlyList<EmailMessage> To(string address) =>
        _sent.Where(m => string.Equals(m.To, address, StringComparison.OrdinalIgnoreCase)).ToList();

    public async Task<EmailSendResult> SendAsync(EmailMessage message, CancellationToken ct)
    {
        _sent.Enqueue(message);
        return await Respond(message);
    }
}
