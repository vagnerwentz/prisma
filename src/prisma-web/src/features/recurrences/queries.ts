import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { invalidateMoney } from '@/features/transactions/queries'
import { api, ApiError, unwrap, type Schemas } from '@/lib/api'
import { recurrencesKey, transactionsKey } from '@/lib/queryKeys'

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

// O sino (docs/fase-2.md, 2.15, D4): os débitos automáticos com o valor estimado, o mais antigo primeiro. Fica
// sob transactionsKey: lançar, editar, excluir ou conferir o atualiza (invalidateMoney).
export const toConfirmKey = [...transactionsKey, 'to-confirm'] as const

// O sino fica montado no cabeçalho o tempo todo, e o débito nasce sozinho (a tarefa roda de hora em hora): sem
// buscar de novo ao voltar para o app, o débito da madrugada só apareceria depois de recarregar a página. Só esta
// consulta volta ao servidor ao ganhar o foco (as outras não: queryClient.ts), e só se tiver mais de um minuto.
export function useToConfirm() {
  return useQuery({
    queryKey: toConfirmKey,
    queryFn: async () => unwrap(await api.GET('/transactions/to-confirm')),
    refetchOnWindowFocus: true,
  })
}

// Conferir (regra 6): sem valor, a estimativa vira o valor; com valor, ele substitui a estimativa.
export function useConfirmAmount() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: async ({ id, amountCents }: { id: string; amountCents: number | null }) =>
      unwrap(await api.POST('/transactions/{id}/confirm-amount', { params: { path: { id } }, body: { amountCents } })),
    onSuccess: () => invalidateMoney(queryClient),
  })
}
