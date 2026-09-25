import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { api, ApiError, unwrap, type Schemas } from '@/lib/api'
import { balancesKey, dashboardKey, statementsKey, transactionsKey } from '@/lib/queryKeys'

export type Transaction = Schemas['TransactionResponse']

// Período pela data da compra (docs/fase-1.md): a lista mostra o que aconteceu em cada dia.
export function useTransactions(range: { from: string; to: string }) {
  return useQuery({
    queryKey: [...transactionsKey, range],
    queryFn: async () => unwrap(await api.GET('/transactions', { params: { query: range } })),
  })
}

export type NewTransaction = Schemas['CreateTransactionRequest']

export function useCreateTransaction() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: async (body: NewTransaction) => unwrap(await api.POST('/transactions', { body })),
    onSuccess: () => invalidateMoney(queryClient),
  })
}

// Lançamentos, totais das faturas, saldos das contas e o resumo andam juntos.
function invalidateMoney(queryClient: ReturnType<typeof useQueryClient>) {
  return Promise.all([
    queryClient.invalidateQueries({ queryKey: transactionsKey }),
    queryClient.invalidateQueries({ queryKey: statementsKey }),
    queryClient.invalidateQueries({ queryKey: balancesKey }),
    queryClient.invalidateQueries({ queryKey: dashboardKey }),
  ])
}

export type TransactionChanges = Schemas['UpdateTransactionRequest']
export type PurchaseChanges = Schemas['UpdateInstallmentPurchaseRequest']

// Toda mutação recarrega a lista (é ela que o painel do lançamento lê) e as faturas.
function useInvalidatingMutation<TInput, TOutput>(mutationFn: (input: TInput) => Promise<TOutput>) {
  const queryClient = useQueryClient()
  return useMutation({ mutationFn, onSuccess: () => invalidateMoney(queryClient) })
}

// PATCH /transactions/{id}: um lançamento, ou uma parcela isolada (docs/fase-1.md, 2.2).
export const useUpdateTransaction = () =>
  useInvalidatingMutation(async ({ id, body }: { id: string; body: TransactionChanges }) =>
    unwrap(await api.PATCH('/transactions/{id}', { params: { path: { id } }, body })),
  )

// PATCH /installment-purchases/{id}: a compra inteira, redistribuindo as parcelas.
export const useUpdatePurchase = () =>
  useInvalidatingMutation(async ({ id, body }: { id: string; body: PurchaseChanges }) =>
    unwrap(await api.PATCH('/installment-purchases/{id}', { params: { path: { id } }, body })),
  )

// Exclusão é soft delete; restaurar é o "Desfazer" do aviso.
export const useDeleteTransaction = () =>
  useInvalidatingMutation(async (id: string) => {
    const { error, response } = await api.DELETE('/transactions/{id}', { params: { path: { id } } })
    if (!response.ok) throw new ApiError(response.status, error)
  })

export const useDeletePurchase = () =>
  useInvalidatingMutation(async (id: string) => {
    const { error, response } = await api.DELETE('/installment-purchases/{id}', { params: { path: { id } } })
    if (!response.ok) throw new ApiError(response.status, error)
  })

export const useRestoreTransaction = () =>
  useInvalidatingMutation(async (id: string) =>
    unwrap(await api.POST('/transactions/{id}/restore', { params: { path: { id } } })),
  )

export const useRestorePurchase = () =>
  useInvalidatingMutation(async (id: string) =>
    unwrap(await api.POST('/installment-purchases/{id}/restore', { params: { path: { id } } })),
  )

export type NewTransfer = Schemas['CreateTransferRequest']
export type StatementPayment = Schemas['PayStatementRequest']

// Transferência entre contas: duas pontas, fora de receita e despesa (docs/fase-1.md, 2.3).
export const useCreateTransfer = () =>
  useInvalidatingMutation(async (body: NewTransfer) => unwrap(await api.POST('/transfers', { body })))

// Paga o total da fatura; ela passa a "Paga".
export const usePayStatement = () =>
  useInvalidatingMutation(async ({ id, body }: { id: string; body: StatementPayment }) =>
    unwrap(await api.POST('/statements/{id}/pay', { params: { path: { id } }, body })),
  )
