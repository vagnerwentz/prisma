using Prisma.Domain.Accounts;
using Prisma.Domain.Statements;

namespace Prisma.Domain.Transactions;

// Recálculo único da fatura das compras de um cartão (docs/fase-2.md, 2.9, etapa 2.20). A previsão
// vem do mesmo StatementCalculator da criação, com as datas gravadas (editadas inclusive)
// prevalecendo; o recálculo acrescenta a política: fatura paga é intocável, e compra presa fica. Cada compra anda
// inteira (à vista, a transação; parcelada, todas as parcelas juntas), e só a fatura e o caixa
// mudam: a data da compra, nunca. Estorno no cartão segue a data dele; pagamento de fatura não se move.
// Ao fim, toda compra do cartão tem o caixa no vencimento da sua fatura (CLAUDE.md, regra 4).
public sealed record StatementReconciliation(IReadOnlyList<Transaction> Moved, IReadOnlyList<Statement> Opened)
{
    // statements: todas as faturas do cartão. transactions: as transações ativas do cartão.
    public static StatementReconciliation Reconcile(
        Account card, IReadOnlyCollection<Statement> statements, IEnumerable<Transaction> transactions)
    {
        if (card.Type != AccountType.CreditCard)
            throw new ArgumentException("O recálculo de faturas é só para cartão de crédito.", nameof(card));

        var cardTransactions = transactions.ToList();
        if (cardTransactions.Any(t => t.AccountId != card.Id))
            throw new ArgumentException("Todas as transações devem ser do cartão recalculado.", nameof(transactions));

        var byId = statements.ToDictionary(s => s.Id);
        var byReference = statements.ToDictionary(s => s.Reference);
        var existingDates = statements.Select(s => s.Dates).ToList();
        var moved = new List<Transaction>();
        var opened = new List<Statement>();

        var purchases = cardTransactions
            .Where(t => t.StatementId is not null && t.Type != TransactionType.Transfer)
            .GroupBy(t => t.InstallmentPurchaseId ?? t.Id);

        foreach (var purchase in purchases)
        {
            var items = purchase.ToList();
            if (items.Any(t => !byId.ContainsKey(t.StatementId!.Value)))
                throw new ArgumentException("Faltam faturas do cartão para recalcular.", nameof(statements));

            if (items.Any(t => byId[t.StatementId!.Value].IsPaid))
                continue;

            // Compra presa pela pessoa (regra 2) fica onde está, acompanhando o vencimento da fatura.
            if (items.Any(t => t.StatementPinned))
            {
                foreach (var pinned in items)
                    pinned.SettleOn(byId[pinned.StatementId!.Value]);
                continue;
            }

            var date = items[0].PurchaseDate;
            var targets = items.ToDictionary(t => t, t => StatementCalculator.ForInstallment(
                date, t.InstallmentNumber ?? 1, card.ClosingDay!.Value, card.DueDay!.Value, existingDates));

            // Destino pago: a compra fica onde está, e nenhuma fatura é aberta à toa.
            if (targets.Values.Any(d => byReference.TryGetValue(d.Reference, out var s) && s.IsPaid))
                continue;

            foreach (var (transaction, dates) in targets)
            {
                if (!byReference.TryGetValue(dates.Reference, out var target))
                {
                    target = Statement.Open(card.UserId, card.Id, dates);
                    byReference.Add(dates.Reference, target);
                    byId.Add(target.Id, target);
                    opened.Add(target);
                }

                // Quem fica na mesma fatura acompanha o vencimento dela (que pode ter sido editado).
                if (transaction.StatementId == target.Id)
                {
                    transaction.SettleOn(target);
                    continue;
                }

                transaction.MoveTo(target, transaction.PurchaseDate);
                moved.Add(transaction);
            }
        }

        return new StatementReconciliation(moved, opened);
    }
}
