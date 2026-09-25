using Prisma.Domain.Transactions;

namespace Prisma.Domain.Accounts;

// Soma já agrupada das transações de uma conta. IsSettled: SettlementDate até hoje.
public readonly record struct BalanceEntry(TransactionType Type, TransferDirection? Direction, long AmountCents, bool IsSettled);

// Saldo de conta corrente, carteira ou investimento (docs/fase-1.md, 2.5). O atual conta até hoje;
// o previsto inclui os lançamentos com data de caixa futura.
public sealed record AccountBalance(long CurrentCents, long ProjectedCents)
{
    public static AccountBalance Of(long initialBalanceCents, IEnumerable<BalanceEntry> entries)
    {
        long current = initialBalanceCents, projected = initialBalanceCents;
        foreach (var entry in entries)
        {
            var change = SignOf(entry.Type, entry.Direction) * entry.AmountCents;
            projected += change;
            if (entry.IsSettled) current += change;
        }
        return new AccountBalance(current, projected);
    }

    // Receita, estorno e transferência recebida entram; despesa e transferência enviada (inclusive o
    // pagamento de fatura) saem.
    private static long SignOf(TransactionType type, TransferDirection? direction) => type switch
    {
        TransactionType.Income => 1,
        TransactionType.Refund => 1, // o dinheiro volta (docs/fase-2.md, 2.5, regra 10)
        TransactionType.Expense => -1,
        TransactionType.Transfer => direction switch
        {
            TransferDirection.In => 1,
            TransferDirection.Out => -1,
            _ => throw new ArgumentException("Transferência sem direção.", nameof(direction)),
        },
        _ => throw new ArgumentOutOfRangeException(nameof(type)),
    };
}

// Cartão não tem saldo: tem o que falta pagar (faturas não pagas, inclusive parcelas futuras) e,
// com limite informado, o limite disponível. Pode ficar negativo: o Prisma registra, não bloqueia.
public sealed record CreditCardBalance(long OwedCents, long? AvailableCreditCents)
{
    public static CreditCardBalance Of(long? creditLimitCents, long owedCents) =>
        new(owedCents, creditLimitCents - owedCents);

    // Falta pagar = soma das faturas não pagas, cada uma contada a partir de zero: a fatura com saldo
    // a favor (total negativo) não passa o crédito para as outras nem aumenta o limite
    // (docs/fase-2.md, 2.5, regras 9 e 10).
    public static CreditCardBalance OfStatements(long? creditLimitCents, IEnumerable<long> unpaidStatementTotals) =>
        Of(creditLimitCents, unpaidStatementTotals.Sum(total => Math.Max(total, 0)));
}
