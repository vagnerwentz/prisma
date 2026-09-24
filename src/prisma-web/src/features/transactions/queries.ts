import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { api, ApiError, unwrap, type Schemas } from '@/lib/api'

export type Transaction = Schemas['TransactionResponse']

export const transactionsKey = ['transactions'] as const

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
    onSuccess: () => queryClient.invalidateQueries({ queryKey: transactionsKey }),
  })
}

export type TransactionChanges = Schemas['UpdateTransactionRequest']
export type PurchaseChanges = Schemas['UpdateInstallmentPurchaseRequest']

// Toda mutação recarrega a lista; é ela que o painel do lançamento lê.
function useInvalidatingMutation<TInput, TOutput>(mutationFn: (input: TInput) => Promise<TOutput>) {
  const queryClient = useQueryClient()
  return useMutation({ mutationFn, onSuccess: () => queryClient.invalidateQueries({ queryKey: transactionsKey }) })
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
