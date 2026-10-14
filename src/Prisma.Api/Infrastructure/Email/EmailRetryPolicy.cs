namespace Prisma.Api.Infrastructure.Email;

// Quando tentar de novo um e-mail que falhou por motivo passageiro (docs/email.md, seção 3, regra 3): 30 s, 2 min,
// 10 min, 1 h e depois a cada 6 h, enquanto não passar de 24 h desde o pedido. Depois disso, desiste: um link de
// confirmação que chega no dia seguinte já não ajuda, e a pessoa tem o "Reenviar".
public static class EmailRetryPolicy
{
    public static readonly TimeSpan GiveUpAfter = TimeSpan.FromHours(24);

    private static readonly TimeSpan[] Delays =
    [
        TimeSpan.FromSeconds(30),
        TimeSpan.FromMinutes(2),
        TimeSpan.FromMinutes(10),
        TimeSpan.FromHours(1),
    ];

    private static readonly TimeSpan LaterDelay = TimeSpan.FromHours(6);

    // attempts: quantas tentativas já falharam, contando a de agora. Nulo: desistir.
    public static DateTime? NextAttemptAt(DateTime createdAt, int attempts, DateTime utcNow)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(attempts, 1);
        var delay = attempts <= Delays.Length ? Delays[attempts - 1] : LaterDelay;
        var next = utcNow + delay;
        return next - createdAt > GiveUpAfter ? null : next;
    }
}
