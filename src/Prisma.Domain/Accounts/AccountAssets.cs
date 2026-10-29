using Prisma.Domain.Transactions;

namespace Prisma.Domain.Accounts;

// O que a conta pôs em ativos (I.8, docs/investimentos.md, regra 6): as compras das notas vinculadas a ela, menos as
// vendas, só das liquidações que já ocorreram (data de caixa até hoje). Nunca abaixo de zero: venda com lucro de uma
// nota vinculada contra compra antiga sem vínculo daria negativo, e dinheiro "negativo em ativos" não existe. É uma
// aproximação ao custo; o saldo da conta não muda, isto é só leitura para o "Investido".
public static class AccountAssets
{
    public static long Of(IEnumerable<BalanceEntry> entries)
    {
        long bought = 0, sold = 0;
        foreach (var entry in entries.Where(e => e.Type == TransactionType.TradeSettlement && e.IsSettled))
        {
            if (entry.Direction == TransferDirection.Out)
                bought += entry.AmountCents;
            else if (entry.Direction == TransferDirection.In)
                sold += entry.AmountCents;
            else
                throw new ArgumentException("Liquidação sem direção.", nameof(entries));
        }

        return Math.Max(0, bought - sold);
    }
}
