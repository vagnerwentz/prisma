import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { invalidateMoney } from '@/features/transactions/queries'
import { api, ApiError, unwrap, type Schemas } from '@/lib/api'
import { recurrencesKey } from '@/lib/queryKeys'

// Lançamentos que se repetem (docs/fase-2.md, 2.14).
export type Recurrence = Schemas['RecurrenceResponse']
export type RecurrenceRequest = Schemas['RecurrenceRequest']

export function useRecurrences(enabled = true) {
  return useQuery({
    queryKey: recurrencesKey,
    enabled,
    queryFn: async () => unwrap(await api.GET('/recurrences')),
  })
}

// Um lançamento que já existe passa a se repetir; o que já venceu desde ele é lançado na hora.
export function useStartRecurrence() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: async ({ transactionId, body }: { transactionId: string; body: RecurrenceRequest }) =>
      unwrap(await api.POST('/transactions/{id}/recurrence', { params: { path: { id: transactionId } }, body })),
    onSuccess: () => invalidateMoney(queryClient),
  })
}

export function useEndRecurrence() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: async (id: string) => unwrap(await api.POST('/recurrences/{id}/end', { params: { path: { id } } })),
    onSuccess: () => invalidateMoney(queryClient),
  })
}

// Vale do próximo em diante; o que venceu desde o último lançamento é gerado na hora.
export function useUpdateRecurrence() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: async ({ id, body }: { id: string; body: Schemas['UpdateRecurrenceRequest'] }) =>
      unwrap(await api.PATCH('/recurrences/{id}', { params: { path: { id } }, body })),
    onSuccess: () => invalidateMoney(queryClient),
  })
}

export type PendingLaunch = Schemas['PendingLaunch']

// A cobrança pendente vai para a fatura seguinte à paga (presa) ou para a própria, com o pagamento desfeito.
export function useLaunchPending() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: async ({ id, where }: { id: string; where: PendingLaunch }) =>
      unwrap(await api.POST('/recurrences/pendings/{id}/launch', { params: { path: { id } }, body: { where } })),
    onSuccess: () => invalidateMoney(queryClient),
  })
}

export function useDiscardPending() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: async (id: string) => {
      const { error, response } = await api.DELETE('/recurrences/pendings/{id}', { params: { path: { id } } })
      if (!response.ok) throw new ApiError(response.status, error)
    },
    onSuccess: () => invalidateMoney(queryClient),
  })
}
