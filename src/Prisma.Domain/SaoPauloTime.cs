namespace Prisma.Domain;

/// <summary>
/// Fuso de negócio da aplicação. Toda data do domínio (PurchaseDate, SettlementDate) é
/// o dia civil em São Paulo, nunca o dia em UTC.
/// </summary>
public static class SaoPauloTime
{
    public static readonly TimeZoneInfo Zone = TimeZoneInfo.FindSystemTimeZoneById("America/Sao_Paulo");

    public static DateOnly DateOf(DateTime utc)
    {
        if (utc.Kind != DateTimeKind.Utc)
            throw new ArgumentException("O instante precisa estar em UTC (DateTimeKind.Utc).", nameof(utc));

        return DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(utc, Zone));
    }
}
