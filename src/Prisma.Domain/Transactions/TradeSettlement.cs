using Prisma.Domain.Accounts;
using Prisma.Domain.Investments.Trades;

namespace Prisma.Domain.Transactions;

// A liquidação de uma nota de corretagem vinculada a uma conta (I.8, docs/investimentos.md): uma ponta só, pelo líquido
// da nota (TradeNote.NetCashCents). Não é receita, despesa nem aporte; só mexe no saldo e aparece no extrato.
public static class TradeSettlement
{
    public const string NotEditedByHand = "Altere ou exclua a nota de corretagem.";

    // Cartão não tem saldo para liquidar nota.
    public static Error? CheckAccount(Account account) =>
        account.Type == AccountType.CreditCard
            ? new Error(ErrorType.Validation, "O cartão de crédito não liquida nota de corretagem.")
            : null;

    // O que deve existir para a nota: nada (sem conta, ou líquido zero) ou a liquidação, criada ou atualizada a partir
    // da que já havia. Quem chama decide o que fazer com o nulo (apagar a antiga) e com a antiga excluída (restaurar).
    public static Transaction? Settle(TradeNote note, Account? account, Transaction? existing)
    {
        var net = note.NetCashCents();
        if (account is null || net == 0)
            return null;

        var settlement = existing ?? Transaction.CreateTradeSettlement(note.UserId);
        settlement.ApplyTradeSettlement(account, note.Id, net, note.Date, note.Broker);
        return settlement;
    }
}
