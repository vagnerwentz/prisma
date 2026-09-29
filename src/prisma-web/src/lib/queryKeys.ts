// Chaves do TanStack Query compartilhadas entre telas: um lançamento no cartão muda o total da
// fatura, e editar a fatura muda o vencimento dos lançamentos. Os saldos mudam com qualquer lançamento.
export const transactionsKey = ['transactions'] as const
export const statementsKey = ['statements'] as const
export const balancesKey = ['accounts', 'balances'] as const
export const dashboardKey = ['dashboard'] as const
// Lançamentos que se repetem: criar ou editar uma série gera lançamentos, e lançar pode criar uma série.
export const recurrencesKey = ['recurrences'] as const
// Usuário da sessão: null quando não há sessão.
export const meKey = ['auth', 'me'] as const
