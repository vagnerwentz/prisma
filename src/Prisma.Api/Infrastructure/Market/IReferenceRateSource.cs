using Prisma.Domain.FixedIncome;

namespace Prisma.Api.Infrastructure.Market;

// Fonte do CDI diário (docs/renda-fixa.md, seção 3). Hoje é a SGS 12 do Banco Central; trocar de fornecedor é escrever
// outra implementação, sem mexer em quem consome.
public interface IReferenceRateSource
{
    // As taxas publicadas de `from` a `to`, inclusive, em ordem de data, uma por data. Dias sem publicação (fim de semana,
    // feriado) não aparecem: faixa sem nenhum dia útil devolve lista vazia, e isso não é falha. Qualquer problema (fora do
    // ar, lento, resposta que não é a série) lança MarketDataException e nada é devolvido pela metade.
    Task<IReadOnlyList<DailyRate>> FetchAsync(DateOnly from, DateOnly to, CancellationToken ct);
}
