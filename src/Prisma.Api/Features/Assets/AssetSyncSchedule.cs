using Prisma.Domain;

namespace Prisma.Api.Features.Assets;

// Quando roda a próxima sincronização: uma vez por dia, na hora marcada em São Paulo (padrão 4h, fora do
// pregão). Ativo novo na bolsa é raro e ninguém precisa dele na hora (docs/investimentos.md, seção 4).
public static class AssetSyncSchedule
{
    public static DateTime NextRun(DateTime utcNow, int hourOfDay)
    {
        if (utcNow.Kind != DateTimeKind.Utc)
            throw new ArgumentException("O instante precisa estar em UTC (DateTimeKind.Utc).", nameof(utcNow));

        var local = TimeZoneInfo.ConvertTimeFromUtc(utcNow, SaoPauloTime.Zone);
        var candidate = local.Date.AddHours(hourOfDay);
        if (candidate <= local)
            candidate = candidate.AddDays(1);

        return TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(candidate, DateTimeKind.Unspecified), SaoPauloTime.Zone);
    }
}
