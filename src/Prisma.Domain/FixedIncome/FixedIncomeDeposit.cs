namespace Prisma.Domain.FixedIncome;

// O produto de renda fixa (docs/renda-fixa.md, D10 e 6.5): CDB, LCI e LCA. A remuneração é a mesma nos três (FixedIncomeIndexKind); o que muda
// é o imposto (FixedIncomeTax): a LCI e a LCA são isentas de IR para pessoa física. Outros produtos entram com regra tributária própria.
public enum FixedIncomeProductKind { Cdb, Lci, Lca }

// Como o aporte rende (docs/renda-fixa.md, 5.1 e 5.5): Cdi (X % do CDI), CdiPlus (CDI + taxa fixa ao ano) ou Prefixed (taxa fixa ao
// ano). IPCA e IPCA+ pedem fonte e fórmula próprias (2.5) e não são aceitos antes disso, para nunca mostrar um número que pareça certo.
public enum FixedIncomeIndexKind { Cdi, CdiPlus, Prefixed }

// Liquidez do investimento, só informativa por enquanto (não muda o cálculo).
public enum DepositLiquidity { Unknown, Daily, AtMaturity }

// Um aporte: uma aplicação em renda fixa com a sua data, o seu valor e o seu contrato (produto, índice, percentual, vencimento)
// (docs/renda-fixa.md, 2.3). É a unidade do cálculo e do imposto: cada aporte tem o seu rendimento e o seu prazo de IR.
//
// Pode estar numa caixa (SavingsGoal) ou solto (GoalId nulo), e mudar de caixa sem perder nada. Não guarda rendimento, saldo,
// alíquota nem regime tributário: tudo isso se calcula do aporte e das taxas. Excluir é soft delete (CLAUDE.md, regra 7).
public sealed class FixedIncomeDeposit : Entity
{
    public const int NoteMaxLength = 200;

    private FixedIncomeDeposit() { }

    public Guid? GoalId { get; private set; }

    // A data da aplicação: a taxa do CDI desse dia já conta (docs/renda-fixa.md, 5.1).
    public DateOnly DepositDate { get; private set; }

    public long AmountCents { get; private set; }

    public FixedIncomeProductKind ProductKind { get; private set; }

    public FixedIncomeIndexKind IndexKind { get; private set; }

    // A taxa contratada, em E4, e o que ela é depende do índice: Cdi, o percentual do CDI (IndexPercent, 110% = 1.100.000); CdiPlus e
    // Prefixed, a taxa ao ano (AnnualRate, 14% = 140.000). Lida sempre pelo `Remuneration`, nunca solta.
    public long RateE4 { get; private set; }

    public Remuneration Remuneration => Remuneration.Of(IndexKind, RateE4);

    public DateOnly? MaturityDate { get; private set; }

    public DepositLiquidity Liquidity { get; private set; }

    public string? Note { get; private set; }

    // O par da transferência que financiou o aporte (TransferPairId das duas pontas), quando o dinheiro saiu de uma conta pelo
    // app (FixedIncomeFunding). Nulo: o aporte só é acompanhado, sem mexer em saldo. Não é chave estrangeira: o par são duas
    // linhas de transação, e elas podem ser excluídas à parte.
    public Guid? TransferPairId { get; private set; }

    // Escolhido pela tela ao abrir o formulário: um segundo envio do mesmo pedido (toque duplo, rede que repete) devolve o
    // aporte que já existe em vez de criar outro. Único por pessoa.
    public Guid ClientRequestId { get; private init; }

    public static Result<FixedIncomeDeposit> Create(
        Guid userId, Guid? goalId, DateOnly depositDate, long amountCents, FixedIncomeProductKind productKind,
        FixedIncomeIndexKind indexKind, long rateE4, DateOnly? maturityDate, DepositLiquidity liquidity, string? note,
        Guid clientRequestId, DateOnly today)
    {
        if (clientRequestId == Guid.Empty)
            return new Error(ErrorType.Validation, "O pedido veio sem identificador.");

        var deposit = new FixedIncomeDeposit { UserId = userId, GoalId = goalId, ClientRequestId = clientRequestId };
        var error = deposit.Update(depositDate, amountCents, productKind, indexKind, rateE4, maturityDate, liquidity, note, today);
        return error is null ? deposit : error;
    }

    // Troca o contrato inteiro. Recusada, o aporte fica como estava. A caixa muda por MoveTo, não por aqui.
    public Error? Update(
        DateOnly depositDate, long amountCents, FixedIncomeProductKind productKind, FixedIncomeIndexKind indexKind,
        long rateE4, DateOnly? maturityDate, DepositLiquidity liquidity, string? note, DateOnly today)
    {
        note = string.IsNullOrWhiteSpace(note) ? null : note.Trim();

        if (amountCents < 1 || amountCents > FixedIncomeEngine.MaxPrincipalCents)
            return Invalid("O valor do aporte precisa ser maior que zero e caber no limite.");
        if (depositDate > today)
            return Invalid("A data do aporte não pode estar no futuro.");
        if (maturityDate is { } maturity && maturity <= depositDate)
            return Invalid("O vencimento precisa ser depois da data do aporte.");
        if (!Enum.IsDefined(productKind) || !Enum.IsDefined(indexKind) || !Enum.IsDefined(liquidity))
            return Invalid("Produto, índice ou liquidez inválidos.");
        if (!Remuneration.Of(indexKind, rateE4).IsInRange)
            return Invalid(indexKind == FixedIncomeIndexKind.Cdi
                ? "O percentual do CDI precisa estar entre 0,01% e 500%."
                : "A taxa ao ano precisa estar entre 0,01% e 100%.");
        if (note is { Length: > NoteMaxLength })
            return Invalid($"A observação pode ter até {NoteMaxLength} caracteres.");

        DepositDate = depositDate;
        AmountCents = amountCents;
        ProductKind = productKind;
        IndexKind = indexKind;
        RateE4 = rateE4;
        MaturityDate = maturityDate;
        Liquidity = liquidity;
        Note = note;
        return null;
    }

    public void LinkFunding(Guid transferPairId)
    {
        if (transferPairId == Guid.Empty)
            throw new ArgumentException("O par da transferência está vazio.", nameof(transferPairId));

        TransferPairId = transferPairId;
    }

    // As pontas da transferência já não existem (foram apagadas de vez): o aporte volta a ser só acompanhado.
    public void Unlink() => TransferPairId = null;

    // Muda de caixa (ou sai de todas, com nulo) sem tocar no contrato: data, valor, percentual e rendimento ficam iguais.
    public void MoveTo(Guid? goalId) => GoalId = goalId;

    // Desfaz o soft delete.
    public void Restore() => ClearDeletion();

    private static Error Invalid(string message) => new(ErrorType.Validation, message);
}
