// Chaves do TanStack Query compartilhadas entre telas: um lançamento no cartão muda o total da
// fatura, e editar a fatura muda o vencimento dos lançamentos. Os saldos mudam com qualquer lançamento.
export const transactionsKey = ['transactions'] as const
export const statementsKey = ['statements'] as const
export const balancesKey = ['accounts', 'balances'] as const
export const dashboardKey = ['dashboard'] as const
