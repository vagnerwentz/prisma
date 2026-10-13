import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { api, ApiError, unwrap } from '@/lib/api'
import { eventReviewsKey } from '@/lib/queryKeys'

// Num módulo próprio porque o sino, que está em toda tela (pacote principal), lê a lista: assim ele não leva junto as
// outras consultas de investimentos.
// Conferir eventos (I.7c2): o que pede conferência, e o "Confere", o "Não confere" e o "Desfazer". O sino também lê
// esta lista (uma linha para todos os eventos).
export function useEventReviews() {
  return useQuery({
    queryKey: eventReviewsKey,
    queryFn: async () => unwrap(await api.GET('/event-reviews')),
  })
}

export function useConfirmEvents() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: async (eventIds: string[]) => {
      const { error, response } = await api.POST('/event-reviews/confirm', { body: { eventIds } })
      if (!response.ok) throw new ApiError(response.status, error)
    },
    onSuccess: () => queryClient.invalidateQueries({ queryKey: eventReviewsKey }),
  })
}

export function useDisputeEvent() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: async ({ eventId, reportedQuantity }: { eventId: string; reportedQuantity: number | null }) => {
      const { error, response } = await api.POST('/event-reviews/{eventId}/dispute', {
        params: { path: { eventId } },
        body: { reportedQuantity },
      })
      if (!response.ok) throw new ApiError(response.status, error)
    },
    onSuccess: () => queryClient.invalidateQueries({ queryKey: eventReviewsKey }),
  })
}

export function useUndoEventReviews() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: async (eventIds: string[]) => {
      for (const eventId of eventIds) {
        const { error, response } = await api.DELETE('/event-reviews/{eventId}', { params: { path: { eventId } } })
        if (!response.ok) throw new ApiError(response.status, error)
      }
    },
    onSuccess: () => queryClient.invalidateQueries({ queryKey: eventReviewsKey }),
  })
}
