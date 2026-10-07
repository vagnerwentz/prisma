// Chaves do TanStack Query compartilhadas entre telas: um lançamento no cartão muda o total da
// fatura, e editar a fatura muda o vencimento dos lançamentos. Os saldos mudam com qualquer lançamento.
export const transactionsKey = ['transactions'] as const
export const statementsKey = ['statements'] as const
export const balancesKey = ['accounts', 'balances'] as const
export const dashboardKey = ['dashboard'] as const
// Lançamentos que se repetem: criar ou editar uma série gera lançamentos, e lançar pode criar uma série.
export const recurrencesKey = ['recurrences'] as const
// Ativos da carteira (docs/investimentos.md, etapa 5a).
export const holdingsKey = ['holdings'] as const
// Proventos (etapa 5b): mudam com qualquer lançamento (excluir pela lista, excluir a conta).
export const payoutsKey = ['payouts'] as const
// Notas de corretagem e a carteira calculada delas (docs/investimentos.md, I.7b2). Não são dinheiro: não mexem em
// saldos nem no resumo, só umas nas outras e na carteira.
export const tradeNotesKey = ['trade-notes'] as const
export const positionsKey = ['positions'] as const
// Usuário da sessão: null quando não há sessão.
export const meKey = ['auth', 'me'] as const
