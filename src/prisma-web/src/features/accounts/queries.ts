import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { api, ApiError, unwrap, type Schemas } from '@/lib/api'
import { balancesKey, statementsKey, transactionsKey } from '@/lib/queryKeys'

export type Account = Schemas['AccountResponse']
export type NewAccount = Schemas['CreateAccountRequest']
export type AccountChanges = Schemas['UpdateAccountRequest']
export type Statement = Schemas['ListStatementsStatementResponse']
export type AccountBalance = Schemas['ListAccountBalancesResponse']

export const accountsKey = ['accounts'] as const

export function useAccounts() {
  return useQuery({
    queryKey: accountsKey,
    queryFn: async () => unwrap(await api.GET('/accounts')),
  })
}

// Saldo atual e previsto das contas; no cartão, o que falta pagar e o limite disponível
// (docs/fase-1.md, 2.5). Fica sob accountsKey: editar a conta (saldo inicial, limite) atualiza.
export function useAccountBalances() {
  return useQuery({
    queryKey: balancesKey,
    queryFn: async () => unwrap(await api.GET('/accounts/balances')),
    select: (balances) => new Map(balances.map((b) => [b.accountId, b])),
  })
}

export function useCreateAccount() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: async (body: NewAccount) => unwrap(await api.POST('/accounts', { body })),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: accountsKey }),
  })
}

// PATCH /accounts/{id}: todos os campos editáveis juntos; o tipo não muda.
export function useUpdateAccount() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: async ({ id, body }: { id: string; body: AccountChanges }) =>
      unwrap(await api.PATCH('/accounts/{id}', { params: { path: { id } }, body })),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: accountsKey }),
  })
}

// Só conta sem lançamentos pode ser excluída; a API responde 409 com a orientação em pt-BR.
export function useDeleteAccount() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: async (id: string) => {
      const { error, response } = await api.DELETE('/accounts/{id}', { params: { path: { id } } })
      if (!response.ok) throw new ApiError(response.status, error)
    },
    onSuccess: () => queryClient.invalidateQueries({ queryKey: accountsKey }),
  })
}

export function useStatements(accountId: string, enabled: boolean) {
  return useQuery({
    queryKey: [...statementsKey, accountId],
    enabled,
    queryFn: async () => unwrap(await api.GET('/accounts/{accountId}/statements', { params: { path: { accountId } } })),
  })
}

// Compras de uma fatura, para a tela da fatura.
export function useStatementTransactions(statementId: string) {
  return useQuery({
    queryKey: [...transactionsKey, { statementId }],
    queryFn: async () => unwrap(await api.GET('/transactions', { params: { query: { statementId } } })),
  })
}

// Editar as datas recalcula o vencimento das compras da fatura: recarrega faturas e lançamentos.
export function useUpdateStatement() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: async ({ id, body }: { id: string; body: Schemas['UpdateStatementRequest'] }) =>
      unwrap(await api.PATCH('/statements/{id}', { params: { path: { id } }, body })),
    onSuccess: () =>
      Promise.all([
        queryClient.invalidateQueries({ queryKey: statementsKey }),
        queryClient.invalidateQueries({ queryKey: transactionsKey }),
      ]),
  })
}
