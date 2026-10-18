namespace Prisma.Domain.FixedIncome;

// Séries de taxa de referência que o Prisma guarda. Hoje só o CDI diário (SGS 12 do Banco Central).
public enum ReferenceRateSeries { Cdi }

// A taxa diária de uma série numa data, como o Banco Central publicou (docs/renda-fixa.md, 2.4). É dado de mercado, o
// mesmo para todo mundo: não tem dono nem soft delete, por isso não herda Entity (CLAUDE.md, regra 6, exceção dos dados
// de mercado). Só a sincronização escreve; o usuário só lê, através do cálculo.
//
// Nunca é apagada. Uma data sem taxa publicada (fim de semana, feriado) simplesmente não tem linha: o cálculo não
// preenche lacuna com zero. Chave única (série, data).
public sealed class ReferenceRate
{
    // EF Core.
    private ReferenceRate()
    {
    }

    private ReferenceRate(ReferenceRateSeries series, DailyRate rate, string source, DateTime utcNow)
    {
        Series = series;
        ReferenceDate = rate.Date;
        DailyRateE8 = rate.RateE8;
        Source = source;
        FetchedAt = utcNow;
    }

    public Guid Id { get; private init; } = Guid.CreateVersion7();

    public ReferenceRateSeries Series { get; private init; }

    public DateOnly ReferenceDate { get; private init; }

    // A taxa diária como fração em unidades de 10⁻⁸ (0,050788 % ao dia = 50788); ver DailyRate.
    public long DailyRateE8 { get; private set; }

    // De onde veio: "bcb-sgs-12". Rastreia a origem de cada número usado no cálculo.
    public string Source { get; private set; } = null!;

    // Quando o Prisma baixou o valor (UTC).
    public DateTime FetchedAt { get; private set; }

    // Preenchido se um valor já gravado mudou na fonte; nulo enquanto nunca foi revisto.
    public DateTime? RevisedAt { get; private set; }

    public DailyRate Rate => new(ReferenceDate, DailyRateE8);

    // A fonte trouxe outro valor para uma data já gravada: o número muda e o instante fica anotado. Devolve se mudou.
    // Valor igual não toca em nada (nem no FetchedAt): rodar a sincronização de novo não deixa rastro.
    public bool Revise(DailyRate incoming, DateTime utcNow)
    {
        if (incoming.Date != ReferenceDate)
            throw new ArgumentException("A taxa é de outra data.", nameof(incoming));
        if (incoming.RateE8 == DailyRateE8)
            return false;

        DailyRateE8 = incoming.RateE8;
        FetchedAt = utcNow;
        RevisedAt = utcNow;
        return true;
    }

    public static ReferenceRate From(ReferenceRateSeries series, DailyRate rate, string source, DateTime utcNow) =>
        new(series, rate, source, utcNow);
}
