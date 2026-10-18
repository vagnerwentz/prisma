using Prisma.Domain;

namespace Prisma.Api.Features.Rates;

// Quando roda a próxima sincronização do CDI: nos horários marcados, em São Paulo, todos os dias (docs/renda-fixa.md, D6).
// O CDI de um dia só sai à noite, então são dois horários (19h30 e 23h30): se o primeiro ainda não encontrou a taxa do dia, o
// segundo encontra. Fim de semana e feriado rodam igual e voltam sem novidade, o que custa uma requisição.
public static class ReferenceRateSchedule
{
    public static DateTime NextRun(DateTime utcNow, IReadOnlyList<TimeOnly> times)
    {
        if (utcNow.Kind != DateTimeKind.Utc)
            throw new ArgumentException("O instante precisa estar em UTC (DateTimeKind.Utc).", nameof(utcNow));
        if (times.Count == 0)
            throw new ArgumentException("Precisa de ao menos um horário.", nameof(times));

        var local = TimeZoneInfo.ConvertTimeFromUtc(utcNow, SaoPauloTime.Zone);
        var ordered = times.Order().ToList();
        var candidate = ordered.Select(time => local.Date.Add(time.ToTimeSpan())).FirstOrDefault(at => at > local);
        if (candidate == default)
            candidate = local.Date.AddDays(1).Add(ordered[0].ToTimeSpan());

        return TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(candidate, DateTimeKind.Unspecified), SaoPauloTime.Zone);
    }
}
