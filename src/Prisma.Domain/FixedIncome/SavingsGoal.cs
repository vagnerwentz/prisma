namespace Prisma.Domain.FixedIncome;

// O objetivo financeiro ("Casamento", "Reserva de emergência"): uma caixa onde se põe e de onde se tira investimentos
// (docs/renda-fixa.md, 2.2). Só organiza: não guarda saldo, rendimento nem imposto, que saem dos aportes. O produto e o
// percentual do CDI de cada investimento vivem nele, não aqui; o percentual do objetivo é só o que pré-preenche o formulário
// de aporte novo.
//
// Excluir é soft delete (CLAUDE.md, regra 7). O que fazer com os aportes da caixa é decisão de quem exclui, no handler.
public sealed class SavingsGoal : Entity
{
    public const int NameMaxLength = 80;
    public const int DescriptionMaxLength = 300;

    private SavingsGoal() { }

    public string Name { get; private set; } = "";

    public string? Description { get; private set; }

    // A meta em centavos; sem meta, a caixa só acumula.
    public long? TargetCents { get; private set; }

    // O percentual do CDI que o formulário de aporte já traz preenchido (IndexPercent, em E4). Só conveniência.
    public long? DefaultIndexPercentE4 { get; private set; }

    public static Result<SavingsGoal> Create(
        Guid userId, string name, string? description, long? targetCents, long? defaultIndexPercentE4)
    {
        var goal = new SavingsGoal { UserId = userId };
        var error = goal.Update(name, description, targetCents, defaultIndexPercentE4);
        return error is null ? goal : error;
    }

    // Troca tudo. Recusada, o objetivo fica como estava.
    public Error? Update(string name, string? description, long? targetCents, long? defaultIndexPercentE4)
    {
        name = name.Trim();
        description = string.IsNullOrWhiteSpace(description) ? null : description.Trim();

        if (name.Length == 0)
            return Invalid("Dê um nome ao objetivo.");
        if (name.Length > NameMaxLength)
            return Invalid($"O nome pode ter até {NameMaxLength} caracteres.");
        if (description is { Length: > DescriptionMaxLength })
            return Invalid($"A descrição pode ter até {DescriptionMaxLength} caracteres.");
        if (targetCents is < 1 or > FixedIncomeEngine.MaxPrincipalCents)
            return Invalid("A meta precisa ser maior que zero e caber no limite.");
        if (defaultIndexPercentE4 is { } percent && !new IndexPercent(percent).IsInRange)
            return Invalid("O percentual do CDI precisa estar entre 0,01% e 500%.");

        Name = name;
        Description = description;
        TargetCents = targetCents;
        DefaultIndexPercentE4 = defaultIndexPercentE4;
        return null;
    }

    // Desfaz o soft delete.
    public void Restore() => ClearDeletion();

    private static Error Invalid(string message) => new(ErrorType.Validation, message);
}
