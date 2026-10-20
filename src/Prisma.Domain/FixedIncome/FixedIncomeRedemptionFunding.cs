using Prisma.Domain.Accounts;
using Prisma.Domain.Categories;
using Prisma.Domain.Transactions;

namespace Prisma.Domain.FixedIncome;

// O que o resgate faz nas contas (docs/renda-fixa-resgate.md, 5). A conta de investimento guarda só o principal aportado, então volta
// só o principal, como transferência (nunca receita nem despesa: CLAUDE.md, regra 5). O rendimento líquido, que é dinheiro novo, entra na
// conta de destino como receita. O imposto não é despesa: já vem abatido do valor. Os dois, na data do resgate.
//
// Principal zero (resgate só de rendimento) não tem transferência; rendimento líquido zero (imposto = todo o rendimento) não tem receita.
public sealed record RedemptionLegs(TransferLegs? Transfer, Transaction? Income);

public static class FixedIncomeRedemptionFunding
{
    public const string TransferDescription = "Resgate de renda fixa";

    public const string IncomeDescription = "Rendimento de renda fixa";

    // `investment` é a conta de investimento que recebeu o aporte; `destination`, a conta corrente ou a carteira onde o dinheiro caiu.
    // Liga os lançamentos ao resgate (`TransferPairId` e `IncomeTransactionId`).
    public static Result<RedemptionLegs> Create(
        Guid userId, Account investment, Account destination, FixedIncomeRedemption redemption, Category? incomeCategory)
    {
        if (investment.Type != AccountType.Investment)
            return new Error(
                ErrorType.Validation, "O principal do resgate sai de uma conta de investimento.", "RedemptionNeedsInvestmentAccount");
        if (destination.Type is not (AccountType.Checking or AccountType.Cash))
            return new Error(
                ErrorType.Validation, "O dinheiro do resgate cai numa conta corrente ou na carteira.", "RedemptionNeedsPaymentAccount");

        TransferLegs? transfer = null;
        if (redemption.PrincipalCents > 0)
        {
            var legs = Transfer.Create(
                userId, investment, destination, redemption.PrincipalCents, redemption.RedemptionDate, PaymentMethod.Ted,
                TransferDescription);
            if (!legs.IsSuccess)
                return legs.Error;
            transfer = legs.Value;
        }

        Transaction? income = null;
        var netYield = redemption.NetCents - redemption.PrincipalCents;
        if (netYield > 0)
        {
            var created = Transaction.CreateSimple(
                userId, destination, TransactionType.Income, netYield, redemption.RedemptionDate, incomeCategory, PaymentMethod.Ted,
                IncomeDescription);
            if (!created.IsSuccess)
                return created.Error;
            income = created.Value;
        }

        redemption.LinkFunding(transfer?.Out.TransferPairId, income?.Id);
        return new RedemptionLegs(transfer, income);
    }
}
