import { keepPreviousData, useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { api, ApiError, unwrap, type Schemas } from '@/lib/api'
import { balancesKey, dashboardKey, statementsKey, transactionsKey } from '@/lib/queryKeys'

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
    // Nome e ativo/inativo mudam as próximas faturas do Resumo.
    onSuccess: () =>
      Promise.all([
        queryClient.invalidateQueries({ queryKey: accountsKey }),
        queryClient.invalidateQueries({ queryKey: dashboardKey }),
      ]),
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

// Prévia do ajuste de datas (docs/fase-2.md, 2.10): as compras que mudariam de fatura, sem gravar nada.
// Fica sob statementsKey: salvar qualquer coisa que mexa em faturas a descarta. Recusa (400) não se repete.
export function useStatementDatesPreview(id: string, dates: { closingDate: string; dueDate: string } | null) {
  return useQuery({
    queryKey: [...statementsKey, 'date-preview', id, dates],
    enabled: dates !== null,
    placeholderData: keepPreviousData,
    retry: false,
    queryFn: async () =>
      unwrap(await api.POST('/statements/{id}/date-preview', { params: { path: { id } }, body: dates! })),
  })
}

// Editar as datas recalcula o vencimento das compras e pode movê-las de fatura (docs/fase-2.md, 2.9):
// recarrega faturas, lançamentos, saldos e o Resumo, que soma pela data de caixa.
export function useUpdateStatement() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: async ({ id, body }: { id: string; body: Schemas['UpdateStatementRequest'] }) =>
      unwrap(await api.PATCH('/statements/{id}', { params: { path: { id } }, body })),
    onSuccess: () =>
      Promise.all([
        queryClient.invalidateQueries({ queryKey: statementsKey }),
        queryClient.invalidateQueries({ queryKey: transactionsKey }),
        queryClient.invalidateQueries({ queryKey: balancesKey }),
        queryClient.invalidateQueries({ queryKey: dashboardKey }),
      ]),
  })
}
