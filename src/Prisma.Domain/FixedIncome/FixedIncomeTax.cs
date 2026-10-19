namespace Prisma.Domain.FixedIncome;

// O imposto estimado de um aporte se ele fosse resgatado numa data (docs/renda-fixa.md, 6.4 e 6.5). É estimativa de resgate, não imposto
// devido hoje: no CDB o imposto só é retido no resgate ou no vencimento.
//
// CDB: IOF + IR regressivo, abaixo. LCI e LCA (6.5): isentas de IR para pessoa física, sem tabela (Lei 11.033/2004, art. 3º, II e IV). IOF:
// a LCA tem alíquota zero (Decreto 6.306/2007, art. 32, § 2º, V); a LCI não é citada ali, então vale a tabela do CDB abaixo de 30 dias.
// Como o prazo mínimo das letras é de meses (Resolução CMN 5.215/2025), o IOF da LCI quase nunca aparece.
//
//   IOF  só abaixo de 30 dias corridos, pela tabela do Decreto 6.306/2007 (96 % do rendimento no dia 1, caindo até 0 % no dia 30).
//   IR   regressivo (Lei 11.033/2004, art. 1º): até 180 dias 22,5 %, de 181 a 360 dias 20 %, de 361 a 720 dias 17,5 % e acima
//        de 720 dias 15 %, sobre o rendimento já líquido do IOF.
//
// Prazo é a diferença em dias corridos entre a data do resgate e a do aporte: a faixa muda no dia seguinte ao limite (180 dias
// ainda é 22,5 %; 181 é 20 %). Cada aporte tem a sua faixa, e o total de um grupo é a soma dos impostos de cada um. IOF e IR
// são arredondados em centavos, meio centavo para cima; a diferença de centavos para o extrato do banco é esperada.
//
// A regra é versionada por data: o resgate estimado escolhe a regra vigente nele e o resultado diz qual foi (RuleVersion). Se a lei
// mudar, entra uma regra nova com a data de início e as antigas continuam valendo para as datas delas.
public sealed record TaxEstimate(long IofCents, long IrCents, int IrRateBasisPoints, int HoldingDays, string RuleVersion)
{
    public long TotalCents => IofCents + IrCents;
}

public static class FixedIncomeTax
{
    public const string CurrentRuleVersion = "cdb-lei-11033-2005";

    public const string LciRuleVersion = "lci-lei-11033-2005";

    public const string LcaRuleVersion = "lca-lei-11033-2005";

    private sealed record Rule(string Version, DateOnly EffectiveFrom, IReadOnlyList<(int UpToDays, int RateBasisPoints)> IrBrackets,
        IReadOnlyList<int> IofPercentByDay);

    // IOF, % do rendimento por dia de prazo; o índice 0 é o dia 1. A partir do dia 30, zero.
    private static readonly int[] IofTable =
    [
        96, 93, 90, 86, 83, 80, 76, 73, 70, 66,
        63, 60, 56, 53, 50, 46, 43, 40, 36, 33,
        30, 26, 23, 20, 16, 13, 10, 6, 3,
    ];

    // Do mais novo para o mais antigo: vale a primeira regra cuja data de início não passa da data do resgate. Uma lista por produto.
    private static readonly Rule[] CdbRules =
    [
        new(CurrentRuleVersion, new DateOnly(2005, 1, 1), [(180, 2_250), (360, 2_000), (720, 1_750), (int.MaxValue, 1_500)], IofTable),
    ];

    // Isenta de IR em qualquer prazo (faixa única de 0 %); IOF pela tabela.
    private static readonly Rule[] LciRules = [new(LciRuleVersion, new DateOnly(2005, 1, 1), [(int.MaxValue, 0)], IofTable)];

    // Isenta de IR e de IOF: sem tabela de IOF.
    private static readonly Rule[] LcaRules = [new(LcaRuleVersion, new DateOnly(2005, 1, 1), [(int.MaxValue, 0)], [])];

    private static Rule[]? RulesFor(FixedIncomeProductKind product) => product switch
    {
        FixedIncomeProductKind.Cdb => CdbRules,
        FixedIncomeProductKind.Lci => LciRules,
        FixedIncomeProductKind.Lca => LcaRules,
        _ => null,
    };

    public static Result<TaxEstimate> Estimate(
        FixedIncomeProductKind product, DateOnly depositDate, DateOnly redemptionDate, Money grossYield)
    {
        if (redemptionDate < depositDate)
            return new Error(ErrorType.Validation, "A data do resgate é anterior à data do aporte.");

        if (RulesFor(product) is not { } rules)
            return new Error(ErrorType.Validation, "Não há regra de imposto para esse produto.");

        var rule = rules.FirstOrDefault(r => r.EffectiveFrom <= redemptionDate);
        if (rule is null)
            return new Error(ErrorType.Validation, "Não há regra de imposto para essa data.");

        var days = redemptionDate.DayNumber - depositDate.DayNumber;
        var rate = rule.IrBrackets.First(b => days <= b.UpToDays).RateBasisPoints;
        if (grossYield.Cents <= 0)
            return new TaxEstimate(0, 0, rate, days, rule.Version);

        // Sem tabela (LCA), sem IOF; com ela, o dia do aporte conta como o primeiro.
        var iofPercent = rule.IofPercentByDay.Count == 0 ? 0
            : days < 1 ? rule.IofPercentByDay[0]
            : days <= rule.IofPercentByDay.Count ? rule.IofPercentByDay[days - 1] : 0;
        var iof = RoundHalfUp(grossYield.Cents, iofPercent, 100);
        var ir = RoundHalfUp(grossYield.Cents - iof, rate, 10_000);

        return new TaxEstimate(iof, ir, rate, days, rule.Version);
    }

    // Rendimento e saldo líquidos do imposto estimado.
    public static (Money NetYield, Money NetBalance) Net(Money grossBalance, Money grossYield, TaxEstimate tax) =>
        (new Money(grossYield.Cents - tax.TotalCents), new Money(grossBalance.Cents - tax.TotalCents));

    // value × numerator / denominator, com meio para cima, em Int128: rendimento até R$ 1 bilhão × 10.000 não estoura.
    private static long RoundHalfUp(long value, int numerator, int denominator) =>
        (long)(((Int128)value * numerator + denominator / 2) / denominator);
}
