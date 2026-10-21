namespace Prisma.Domain.FixedIncome;

// Um aporte que pode entrar no resgate em lote: o saldo bruto dele na data do resgate, com os resgates anteriores já descontados.
public readonly record struct AllocationCandidate(Guid DepositId, DateOnly DepositDate, long BalanceCents);

// Quanto do valor do lote sai de cada aporte (bruto).
public readonly record struct AllocationPart(Guid DepositId, long GrossCents);

// Um pedaço já calculado do lote, para repartir o líquido do extrato: o bruto, o rendimento que saiu e o imposto que o app estimou.
public readonly record struct NetLine(Guid DepositId, long GrossCents, long YieldCents, long EstimatedTaxCents);

// O líquido que cabe a um aporte (vira o `informedNetCents` do `FixedIncomeRedemption.Create`).
public readonly record struct NetPart(Guid DepositId, long NetCents);

// A divisão do resgate em lote (docs/renda-fixa-resgate.md, 15.3 e 15.5). Pura: não conhece banco, relógio nem imposto. Só decide quanto
// de cada aporte sai e como o líquido do extrato se reparte; principal, rendimento e imposto de cada pedaço continuam sendo do
// `FixedIncomeRedemption.Create`, para que a regra exista num lugar só.
public static class RedemptionAllocator
{
    public static long TotalBalance(IEnumerable<AllocationCandidate> candidates) =>
        candidates.Where(c => c.BalanceCents > 0).Sum(c => c.BalanceCents);

    // Do aporte mais antigo ao mais novo (empate: a ordem em que vieram), cada um sai por inteiro até acabar o valor; o último pode sair em
    // parte. Aporte sem saldo é pulado. A soma das partes é sempre `grossCents`. Quem tira um aporte da lista (a caixa desmarcada na
    // prévia) chama de novo sem ele: o valor se redistribui entre os que sobraram.
    public static Result<IReadOnlyList<AllocationPart>> Allocate(IReadOnlyList<AllocationCandidate> candidates, long grossCents)
    {
        if (grossCents < 1)
            return new Error(ErrorType.Validation, "O valor do resgate deve ser maior que zero.");

        var total = TotalBalance(candidates);
        if (total < 1)
            return new Error(ErrorType.Validation, "Nenhum dos aportes tem saldo para resgatar nessa data.");
        if (grossCents > total)
            return new Error(ErrorType.Validation, $"O valor do resgate passa do saldo dos aportes ({new Money(total)}).");

        var parts = new List<AllocationPart>();
        var left = grossCents;
        foreach (var candidate in candidates.Where(c => c.BalanceCents > 0).OrderBy(c => c.DepositDate))
        {
            if (left == 0)
                break;

            var take = Math.Min(left, candidate.BalanceCents);
            parts.Add(new AllocationPart(candidate.DepositId, take));
            left -= take;
        }

        return parts;
    }

    // O extrato do banco traz um líquido só, para o resgate inteiro. O imposto dele (bruto total − líquido) se reparte entre as linhas na
    // proporção do imposto que o app estimou para cada uma (sem estimativa nenhuma, pelo rendimento): o piso de cada parte, e os centavos
    // que sobram vão um a um da última linha para trás, só onde ainda cabe. O imposto de uma linha nunca passa do rendimento dela nem fica
    // negativo; se a divisão não couber nessas travas, o pedido é recusado. Devolve o líquido de cada linha, na mesma ordem.
    public static Result<IReadOnlyList<NetPart>> DistributeNet(IReadOnlyList<NetLine> lines, long netCents)
    {
        var gross = lines.Sum(l => l.GrossCents);
        var tax = gross - netCents;
        if (lines.Count == 0 || netCents < 1)
            return new Error(ErrorType.Validation, "O líquido informado não serve para esse resgate.");
        if (tax < 0)
            return new Error(ErrorType.Validation, $"O líquido informado não pode passar do valor resgatado ({new Money(gross)}).");

        var yield = lines.Sum(l => l.YieldCents);
        if (tax > yield)
            return new Error(
                ErrorType.Validation, $"O imposto do extrato ({new Money(tax)}) passa do rendimento resgatado ({new Money(yield)}).");

        // Pesos: o imposto estimado; se a estimativa é toda zero (LCI e LCA, por exemplo), o rendimento.
        var useEstimates = lines.Sum(l => l.EstimatedTaxCents) > 0;
        var weights = lines.Select(l => useEstimates ? l.EstimatedTaxCents : l.YieldCents).ToList();
        var weightTotal = weights.Sum();

        var shares = new long[lines.Count];
        if (tax > 0)
        {
            for (var i = 0; i < lines.Count; i++)
                shares[i] = (long)((Int128)tax * weights[i] / weightTotal);

            var left = tax - shares.Sum();
            while (left > 0)
            {
                var moved = false;
                for (var i = lines.Count - 1; i >= 0 && left > 0; i--)
                {
                    if (shares[i] >= lines[i].YieldCents)
                        continue;

                    shares[i]++;
                    left--;
                    moved = true;
                }

                if (!moved)
                    break;
            }

            // A estimativa maior que o rendimento de uma linha, ou o rendimento todo gasto: não cabe.
            if (left > 0 || Enumerable.Range(0, lines.Count).Any(i => shares[i] > lines[i].YieldCents))
                return new Error(
                    ErrorType.Validation, "O imposto do extrato não cabe na divisão entre os aportes. Resgate um por vez.");
        }

        IReadOnlyList<NetPart> nets = lines.Select((l, i) => new NetPart(l.DepositId, l.GrossCents - shares[i])).ToList();
        return Result<IReadOnlyList<NetPart>>.Success(nets);
    }
}
