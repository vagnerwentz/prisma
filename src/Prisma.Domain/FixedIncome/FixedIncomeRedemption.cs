using Prisma.Domain.FixedIncome.Coverage;

namespace Prisma.Domain.FixedIncome;

// O que o motor precisa de um resgate para calcular o que sobra: o dia e o principal que saiu (docs/renda-fixa-resgate.md, 3).
public readonly record struct RedemptionStep(DateOnly Date, Money Principal);

// De onde veio o imposto do resgate: calculado pelo app (IOF e IR separados, com a regra que valeu) ou informado pela pessoa a
// partir do extrato do banco (só o total: não dá para separar).
public enum RedemptionTaxSource { Estimated, Informed }

// Um resgate, total ou parcial, de um aporte (docs/renda-fixa-resgate.md, 2 e 3). É um fato realizado: o principal que saiu, o
// rendimento e o imposto são calculados UMA vez, na gravação, e ficam congelados, porque o resgate seguinte do mesmo aporte parte
// do que sobrou deste. O saldo que resta continua calculado (FixedIncomeEngine), nunca gravado.
//
// O valor é o bruto, como no banco. O principal e o rendimento saem na proporção do saldo na data; a faixa de IR é a do aporte
// (prazo da data do aporte até a do resgate), e o imposto incide sobre o rendimento do pedaço resgatado. Se a pessoa informa o
// líquido do extrato, ele vale e o imposto é a diferença. Excluir é soft delete (CLAUDE.md, regra 7).
public sealed class FixedIncomeRedemption : Entity
{
    private FixedIncomeRedemption() { }

    public Guid DepositId { get; private set; }

    public DateOnly RedemptionDate { get; private set; }

    // O que saiu do saldo, bruto: PrincipalCents + YieldCents.
    public long GrossCents { get; private set; }

    public long PrincipalCents { get; private set; }

    public long YieldCents { get; private set; }

    // Nulos quando o líquido foi informado (RedemptionTaxSource.Informed): o extrato diz só o total.
    public long? IofCents { get; private set; }

    public long? IrCents { get; private set; }

    public long TaxCents { get; private set; }

    public long NetCents { get; private set; }

    public RedemptionTaxSource TaxSource { get; private set; }

    // A regra de FixedIncomeTax que valeu; nulo quando o imposto foi informado.
    public string? TaxRuleVersion { get; private set; }

    // Os lançamentos nas contas, quando o resgate caiu numa conta pelo app (FixedIncomeRedemptionFunding): o par da transferência do
    // principal e a receita do rendimento líquido. Nulos: o resgate só foi registrado. Sem chave estrangeira, como no aporte: são linhas
    // de transação, e podem ser excluídas à parte.
    public Guid? TransferPairId { get; private set; }

    public Guid? IncomeTransactionId { get; private set; }

    // Escolhido pela tela ao abrir o formulário: um segundo envio do mesmo pedido devolve o resgate que já existe. Único por pessoa.
    public Guid ClientRequestId { get; private init; }

    // O saldo do aporte na data de um resgate, com os resgates anteriores já descontados (o principal que sobra e o saldo bruto dele).
    // Recusa a data que não serve: futura, antes do aporte, antes do último resgate, ou sem as taxas do CDI até o dia útil anterior (sem
    // elas o saldo estaria subestimado e o resgate sairia errado; o pré-fixado não usa a série).
    public static Result<FixedIncomeBalance> BalanceOn(
        FixedIncomeDeposit deposit, IReadOnlyList<FixedIncomeRedemption> previous, RateHistory rates, DateOnly date, DateOnly today)
    {
        if (date > today)
            return new Error(ErrorType.Validation, "A data do resgate não pode estar no futuro.");
        if (date < deposit.DepositDate)
            return new Error(ErrorType.Validation, "A data do resgate é anterior à data do aporte.");
        if (previous.Count > 0 && previous.Max(r => r.RedemptionDate) is var last && date < last)
            return new Error(
                ErrorType.Validation, $"O resgate não pode ser anterior ao último resgate desse aporte ({last:dd/MM/yyyy}).");
        if (deposit.Remuneration.UsesCdi && (rates.LastDate is not { } through || through < ReferenceRateCoverage.ExpectedLastDate(date)))
            return new Error(ErrorType.Validation, "As taxas do CDI ainda não chegaram até essa data.", "RatesUnavailable");

        var steps = previous.Select(r => new RedemptionStep(r.RedemptionDate, new Money(r.PrincipalCents))).ToList();
        return FixedIncomeEngine.Balance(new Money(deposit.AmountCents), deposit.DepositDate, deposit.Remuneration, rates, date, steps);
    }

    // `previous` são os resgates já gravados (não excluídos) desse aporte, e `rates` o histórico do CDI.
    public static Result<FixedIncomeRedemption> Create(
        FixedIncomeDeposit deposit, IReadOnlyList<FixedIncomeRedemption> previous, RateHistory rates, DateOnly date,
        long grossCents, long? informedNetCents, Guid clientRequestId, DateOnly today)
    {
        if (clientRequestId == Guid.Empty)
            return new Error(ErrorType.Validation, "O pedido veio sem identificador.");

        var balance = BalanceOn(deposit, previous, rates, date, today);
        if (!balance.IsSuccess)
            return balance.Error;

        var remaining = balance.Value.Principal.Cents;
        var grossBalance = balance.Value.GrossBalance;
        if (remaining < 1)
            return new Error(ErrorType.Validation, "Esse aporte já foi resgatado por inteiro.");
        if (grossCents < 1 || grossCents > grossBalance.Cents)
            return new Error(ErrorType.Validation, $"O valor do resgate precisa estar entre R$ 0,01 e o saldo de {grossBalance}.");

        var principalOut = PrincipalOf(remaining, grossCents, grossBalance.Cents);
        var yield = grossCents - principalOut;

        var redemption = new FixedIncomeRedemption
        {
            UserId = deposit.UserId,
            DepositId = deposit.Id,
            RedemptionDate = date,
            GrossCents = grossCents,
            PrincipalCents = principalOut,
            YieldCents = yield,
            ClientRequestId = clientRequestId,
        };

        if (informedNetCents is { } net)
        {
            if (net < principalOut || net > grossCents)
                return new Error(
                    ErrorType.Validation,
                    $"O líquido informado precisa estar entre {new Money(principalOut)} e {new Money(grossCents)}.");

            redemption.TaxSource = RedemptionTaxSource.Informed;
            redemption.TaxCents = grossCents - net;
            redemption.NetCents = net;
            return redemption;
        }

        var tax = FixedIncomeTax.Estimate(deposit.ProductKind, deposit.DepositDate, date, new Money(yield));
        if (!tax.IsSuccess)
            return tax.Error;

        redemption.TaxSource = RedemptionTaxSource.Estimated;
        redemption.IofCents = tax.Value.IofCents;
        redemption.IrCents = tax.Value.IrCents;
        redemption.TaxCents = tax.Value.TotalCents;
        redemption.NetCents = grossCents - tax.Value.TotalCents;
        redemption.TaxRuleVersion = tax.Value.RuleVersion;
        return redemption;
    }

    internal void LinkFunding(Guid? transferPairId, Guid? incomeTransactionId)
    {
        TransferPairId = transferPairId;
        IncomeTransactionId = incomeTransactionId;
    }

    // As pontas já não existem (foram apagadas de vez): o resgate volta a ser só registro.
    public void Unlink() => LinkFunding(null, null);

    // A ordem em que os resgates de um aporte aconteceram: pela data e, no mesmo dia, pela gravação.
    public static IReadOnlyList<FixedIncomeRedemption> InOrder(IEnumerable<FixedIncomeRedemption> redemptions) =>
        redemptions.OrderBy(r => r.RedemptionDate).ThenBy(r => r.CreatedAt).ThenBy(r => r.Id).ToList();

    // Só o último resgate de um aporte se exclui (docs/renda-fixa-resgate.md, 3): os seguintes foram congelados em cima do que
    // sobrou dele. `live` são os resgates vivos do aporte, este incluído.
    public Error? CanBeDeleted(IEnumerable<FixedIncomeRedemption> live) =>
        InOrder(live) is [.., var last] && last.Id == Id
            ? null
            : new Error(
                ErrorType.Conflict, "Só o último resgate de um aporte pode ser excluído: os seguintes partem do que sobrou dele.",
                "RedemptionNotLast");

    // O "Desfazer": cada resgate foi congelado sobre o que os anteriores deixaram, então só volta se o aporte está como estava quando
    // ele foi gravado: nenhum resgate vivo criado depois dele, e nenhum criado antes que continue excluído. `siblings` são os outros
    // resgates do aporte, vivos e excluídos (este não entra).
    public Error? Restore(IEnumerable<FixedIncomeRedemption> siblings)
    {
        foreach (var other in siblings)
        {
            var newer = CreatedAfter(other, this);
            if ((newer && other.DeletedAt is null) || (!newer && other.DeletedAt is not null))
                return new Error(
                    ErrorType.Conflict, "O aporte mudou depois deste resgate, e ele não pode voltar.", "RedemptionCannotReturn");
        }

        ClearDeletion();
        return null;
    }

    private static bool CreatedAfter(FixedIncomeRedemption a, FixedIncomeRedemption b) =>
        a.CreatedAt != b.CreatedAt ? a.CreatedAt > b.CreatedAt : a.Id.CompareTo(b.Id) > 0;

    // O principal que sai com `gross` do saldo `balance`: a mesma fração do que sobra, meio centavo para cima, em Int128. Tudo
    // (gross = saldo) leva o que sobra inteiro, sem centavo perdido; parte nunca leva o principal todo, para o que resta não
    // virar rendimento sem principal (que sumiria do saldo).
    private static long PrincipalOf(long remaining, long gross, long balance)
    {
        if (gross == balance)
            return remaining;

        var exact = (long)(((Int128)remaining * gross * 2 + balance) / (2 * (Int128)balance));
        return Math.Min(exact, remaining - 1);
    }
}
