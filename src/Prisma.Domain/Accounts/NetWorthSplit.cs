namespace Prisma.Domain.Accounts;

// Uma conta como o patrimônio a enxerga: o tipo, se está ativa, se a pessoa acompanha o caixa dela (só conta de investimento) e o que
// importa para o caixa livre. FundedPrincipalCents: o principal que sobra dos aportes de renda fixa que saíram desta conta pelo app
// (com a transferência viva, depois dos resgates); ele está no saldo da conta e também em "Investido", então não pode contar duas vezes.
public readonly record struct NetWorthAccount(
    AccountType Type, bool IsActive, bool TracksCash, long BalanceCents, long FundedPrincipalCents)
{
    // O caixa livre da corretora: o saldo menos o que já virou renda fixa (que está em "Investido"). Pode ser negativo.
    public long FreeCashCents => BalanceCents - FundedPrincipalCents;
}

// O patrimônio em três partes (I.9, docs/patrimonio.md): o dinheiro livre, o investido e o caixa das corretoras. O "Investido" vem dos
// ativos (a carteira e o rendimento da renda fixa), seja qual for o histórico de caixa; a conta de investimento só entra se a pessoa
// acompanha o caixa dela. O saldo de nenhuma conta muda: isto é só leitura.
public sealed record NetWorthSplit(long AvailableCents, long InvestedCents, long BrokerCashCents, long TotalCents)
{
    // investedCents: o que a carteira tem de hoje (principal que sobra da renda fixa e ações ao custo, `GET /portfolio`);
    // yieldCents: o rendimento bruto da renda fixa.
    public static NetWorthSplit Of(IEnumerable<NetWorthAccount> accounts, long investedCents, long yieldCents)
    {
        long available = 0, brokerCash = 0;
        foreach (var account in accounts.Where(a => a.IsActive))
        {
            switch (account.Type)
            {
                case AccountType.Checking or AccountType.Cash:
                    available += account.BalanceCents;
                    break;
                // O caixa livre pode ficar negativo (histórico incompleto): aparece como é, nunca escondido.
                case AccountType.Investment when account.TracksCash:
                    brokerCash += account.FreeCashCents;
                    break;
            }
        }

        var invested = investedCents + yieldCents;
        return new NetWorthSplit(available, invested, brokerCash, available + invested + brokerCash);
    }
}
