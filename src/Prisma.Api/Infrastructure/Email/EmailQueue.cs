using Microsoft.EntityFrameworkCore;
using Prisma.Api.Infrastructure.Logging;
using Prisma.Domain;

namespace Prisma.Api.Infrastructure.Email;

// Pede um e-mail: acrescenta à fila no mesmo DbContext de quem pede, e o SaveChanges dele grava o motivo e o pedido
// juntos (docs/email.md, seção 3, regra 1). Nada é enviado aqui; a tarefa (EmailOutboxWorker) envia em segundos.
public sealed class EmailQueue(AppDbContext db, IClock clock, ILogger<EmailQueue> logger)
{
    public const int MaxPerDay = 5;
    public static readonly TimeSpan MinInterval = TimeSpan.FromMinutes(1);

    // Falso: o limite do tipo para a pessoa foi atingido (regra 6) e nada entrou na fila. Quem chama responde como
    // se tivesse pedido, para não revelar nada, e a cota do fornecedor não é gasta.
    public async Task<bool> EnqueueAsync(EmailKind kind, Guid userId, string toAddress, CancellationToken ct)
    {
        var now = clock.UtcNow;
        var recent = await db.OutboxEmails
            .Where(e => e.UserId == userId && e.Kind == kind && e.CreatedAt > now.AddDays(-1))
            .Select(e => e.CreatedAt)
            .ToListAsync(ct);

        if (recent.Count >= MaxPerDay || recent.Any(createdAt => createdAt > now - MinInterval))
        {
            logger.EmailThrottled(userId, kind);
            return false;
        }

        db.OutboxEmails.Add(OutboxEmail.Create(kind, userId, toAddress, now));
        return true;
    }
}
