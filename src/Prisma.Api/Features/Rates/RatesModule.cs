using System.Globalization;

namespace Prisma.Api.Features.Rates;

// Taxas de referência do CDI (docs/renda-fixa.md, seção 3): a sincronização com o Banco Central e a tarefa que a roda.
public static class RatesModule
{
    public static IServiceCollection AddReferenceRateFeatures(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<RateSyncOptions>()
            .Bind(configuration.GetSection(RateSyncOptions.Section))
            .Validate(o => o.IsValid(), "Rates:Sync inválido: confira RunTimes (HH:mm, ao menos um), OverlapDays (0 a 60) e InitialLoadFrom.")
            .ValidateOnStart();

        services.AddSingleton<ReferenceRateSync>();

        // Liga por padrão. Os testes de integração desligam: a tabela é global, e cada teste chama a sincronização quando quer.
        if (configuration.GetValue($"{RateSyncOptions.Section}:{nameof(RateSyncOptions.Enabled)}", defaultValue: true))
            services.AddHostedService<ReferenceRateWorker>();
        return services;
    }
}

public sealed class RateSyncOptions
{
    public const string Section = "Rates:Sync";

    private const string TimeFormat = "HH:mm";

    public bool Enabled { get; init; } = true;

    // Horários em São Paulo. O CDI do dia sai à noite: 19h30 pega o normal, 23h30 pega o atraso.
    public string[] RunTimes { get; init; } = ["19:30", "23:30"];

    // Quantos dias para trás cada sincronização repete, para pegar correção recente da fonte.
    public int OverlapDays { get; init; } = 10;

    // Com a tabela vazia, de onde carregar o histórico (docs/renda-fixa.md, D9). Aporte anterior a esta data é recusado.
    public DateOnly InitialLoadFrom { get; init; } = new(2015, 1, 1);

    public bool IsValid() =>
        RunTimes.Length > 0
        && RunTimes.All(t => TimeOnly.TryParseExact(t, TimeFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
        && OverlapDays is >= 0 and <= 60
        && InitialLoadFrom >= new DateOnly(1998, 1, 1);

    public IReadOnlyList<TimeOnly> ParsedRunTimes() =>
        RunTimes.Select(t => TimeOnly.ParseExact(t, TimeFormat, CultureInfo.InvariantCulture)).ToList();
}
