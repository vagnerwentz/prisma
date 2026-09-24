// Chaves do TanStack Query compartilhadas entre telas: um lançamento no cartão muda o total da
// fatura, e editar a fatura muda o vencimento dos lançamentos.
export const transactionsKey = ['transactions'] as const
export const statementsKey = ['statements'] as const
