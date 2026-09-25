using System.Globalization;
using Prisma.Domain;

namespace Prisma.Api.Features.Dashboard;

// O mês do dashboard vem como "aaaa-mm"; sem ele, o mês de hoje em São Paulo.
public static class DashboardMonth
{
    public static Result<DateOnly> FirstDay(string? month, IClock clock)
    {
        if (string.IsNullOrWhiteSpace(month))
        {
            var today = clock.Today;
            return new DateOnly(today.Year, today.Month, 1);
        }

        return DateOnly.TryParseExact(month + "-01", "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var first)
            ? first
            : new Error(ErrorType.Validation, "Informe o mês no formato aaaa-mm.");
    }

    public static string Format(DateOnly firstDay) => firstDay.ToString("yyyy-MM", CultureInfo.InvariantCulture);
}
