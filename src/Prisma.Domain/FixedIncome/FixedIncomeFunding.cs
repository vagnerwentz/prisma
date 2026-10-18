using Prisma.Domain.Accounts;
using Prisma.Domain.Transactions;

namespace Prisma.Domain.FixedIncome;

// O aporte que sai de uma conta (docs/renda-fixa.md, D3). Quem lança o investimento diz de que conta o dinheiro saiu e em que
// conta de investimento entrou, e o app faz a transferência junto: o saldo da conta cai, o "Investido" do Resumo sobe, e nada
// vira receita nem despesa (CLAUDE.md, regra 5). Na data do aporte, no valor do aporte.
//
// A conta de investimento guarda só o principal aportado: o rendimento aparece nos objetivos, não no saldo da conta.
public static class FixedIncomeFunding
{
    public const string Description = "Aporte em renda fixa";

    public static Result<TransferLegs> Create(
        Guid userId, Account from, Account to, long amountCents, DateOnly date, PaymentMethod method)
    {
        if (to.Type != AccountType.Investment)
            return new Error(
                ErrorType.Validation, "O dinheiro do aporte entra numa conta de investimento.", "FundingNeedsInvestmentAccount");
        if (from.Type is not (AccountType.Checking or AccountType.Cash))
            return new Error(
                ErrorType.Validation, "O dinheiro do aporte sai de uma conta corrente ou da carteira.", "FundingNeedsPaymentAccount");

        return Transfer.Create(userId, from, to, amountCents, date, method, Description);
    }
}
