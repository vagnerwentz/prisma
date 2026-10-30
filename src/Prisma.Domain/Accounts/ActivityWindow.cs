namespace Prisma.Domain.Accounts;

/// <summary>
/// A janela de "há quanto tempo a pessoa não aparece" (docs/admin-email.md, AE.2). Conta dias civis de São Paulo entre o
/// último acesso (<c>AppUser.LastSeenAt</c>) e hoje: vista há menos de N dias é <b>ativa</b>, há N dias ou mais é
/// <b>inativa</b>. As duas metades não deixam buraco entre si. O mesmo N serve à contagem do admin e, mais adiante, ao
/// segmento do e-mail de quem não entra: a regra mora num lugar só.
/// </summary>
public readonly record struct ActivityWindow
{
    public const int MaxDays = 365;
    public const int DefaultDays = 30;

    public static readonly ActivityWindow Default = new(DefaultDays);

    private ActivityWindow(int days) => Days = days;

    public int Days { get; }

    public static Result<ActivityWindow> Of(int days) =>
        days is >= 1 and <= MaxDays
            ? new ActivityWindow(days)
            : new Error(ErrorType.Validation, $"Informe de 1 a {MaxDays} dias.", "InvalidDays");

    /// <summary>
    /// O instante (UTC) a partir do qual um último acesso ainda é "ativo": o começo, em São Paulo, do dia de N−1 dias atrás.
    /// Quem foi visto neste instante ou depois está ativo; antes dele, inativo. É a forma que o SQL consegue comparar sem
    /// converter o fuso de cada linha.
    /// </summary>
    public DateTime ActiveSince(DateOnly today) => SaoPauloTime.StartOf(today.AddDays(1 - Days));

    public bool IsActive(DateTime lastSeenUtc, DateOnly today) => lastSeenUtc >= ActiveSince(today);
}
