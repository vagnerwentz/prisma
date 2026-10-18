using System.Collections.Concurrent;
using Prisma.Api.Infrastructure.Market;
using Prisma.Domain.FixedIncome;

namespace Prisma.Api.Tests.Market;

// O "Banco Central" sob controle do teste: sabe um conjunto de taxas e devolve as da janela pedida, como a API de verdade
// (só as datas publicadas, em ordem), ou lança a falha marcada. Guarda os pedidos, para conferir as janelas.
public sealed class FakeReferenceRateSource : IReferenceRateSource
{
    private readonly ConcurrentQueue<(DateOnly From, DateOnly To)> _requests = new();

    public List<DailyRate> Rates { get; set; } = [];

    public Exception? Failure { get; set; }

    // Roda no meio da busca, antes de devolver: para simular outra execução gravando ao mesmo tempo.
    public Func<Task>? OnFetch { get; set; }

    public IReadOnlyList<(DateOnly From, DateOnly To)> Requests => _requests.ToArray();

    public int Calls => _requests.Count;

    public async Task<IReadOnlyList<DailyRate>> FetchAsync(DateOnly from, DateOnly to, CancellationToken ct)
    {
        _requests.Enqueue((from, to));
        if (OnFetch is not null)
            await OnFetch();
        if (Failure is not null)
            throw Failure;

        return Rates.Where(r => r.Date >= from && r.Date <= to).OrderBy(r => r.Date).ToList();
    }
}
