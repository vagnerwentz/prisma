import { keepPreviousData, useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { api, ApiError, unwrap, type Schemas } from '@/lib/api'
import { invalidateMoney } from '@/features/transactions/queries'
import { holdingsKey, payoutsKey, positionsKey, tradeNotesKey } from '@/lib/queryKeys'
import { useDebouncedValue } from '@/lib/useDebouncedValue'

// Busca de ativos enquanto se digita: espera a pessoa parar um instante e mantém o resultado anterior na
// tela até o novo chegar (sem nada com cara de carregando). O catálogo muda uma vez por dia, então a mesma
// busca vale por 10 minutos.
export function useAssetSearch(text: string) {
  const term = useDebouncedValue(text.trim(), 200)
  return useQuery({
    queryKey: ['assets', 'search', term],
    enabled: term !== '',
    staleTime: 10 * 60 * 1000,
    placeholderData: keepPreviousData,
    queryFn: async ({ signal }) => unwrap(await api.GET('/assets', { params: { query: { q: term } }, signal })),
  })
}

// A carteira: os ativos que a pessoa tem (docs/investimentos.md, etapa 5a).
export type Holding = Schemas['HoldingResponse']

export function useHoldings() {
  return useQuery({
    queryKey: holdingsKey,
    queryFn: async () => unwrap(await api.GET('/holdings')),
  })
}

// Pôr de novo um ativo tirado devolve o mesmo registro: é também o "Desfazer".
export function useAddHolding() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: async (assetId: string) => unwrap(await api.POST('/holdings', { body: { assetId } })),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: holdingsKey }),
  })
}

export function useRemoveHolding() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: async (id: string) => {
      const { error, response } = await api.DELETE('/holdings/{id}', { params: { path: { id } } })
      if (!response.ok) throw new ApiError(response.status, error)
    },
    onSuccess: () => queryClient.invalidateQueries({ queryKey: holdingsKey }),
  })
}

// Proventos (etapa 5b). Excluir e desfazer usam os de lançamento (useDeleteTransaction, useRestoreTransaction).
export function usePayouts() {
  return useQuery({
    queryKey: payoutsKey,
    queryFn: async () => unwrap(await api.GET('/payouts')),
  })
}

export type PayoutRequest = Schemas['SavePayoutRequest']

// Lançar ou editar. Provento é dinheiro: recarrega saldos, lista e resumo, e a carteira (o ativo entra nela).
export function useSavePayout() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: async ({ id, body }: { id?: string; body: PayoutRequest }) =>
      id
        ? unwrap(await api.PUT('/payouts/{id}', { params: { path: { id } }, body }))
        : unwrap(await api.POST('/payouts', { body })),
    onSuccess: () => invalidateMoney(queryClient),
  })
}

// Notas de corretagem e carteira (docs/investimentos.md, I.7b2). A carteira é calculada pela API a cada leitura.
export function useTradeNotes(take?: number) {
  return useQuery({
    queryKey: [...tradeNotesKey, 'list', take ?? 'all'],
    queryFn: async () => unwrap(await api.GET('/trade-notes', { params: { query: { take } } })),
  })
}

export function useTradeNote(id: string | null) {
  return useQuery({
    queryKey: [...tradeNotesKey, 'detail', id],
    enabled: id !== null,
    queryFn: async () => unwrap(await api.GET('/trade-notes/{id}', { params: { path: { id: id! } } })),
  })
}

export function usePositions() {
  return useQuery({
    queryKey: positionsKey,
    queryFn: async () => unwrap(await api.GET('/positions')),
  })
}

export function usePositionHistory(assetId: string | null) {
  return useQuery({
    queryKey: [...positionsKey, 'history', assetId],
    enabled: assetId !== null,
    queryFn: async () => unwrap(await api.GET('/positions/{assetId}', { params: { path: { assetId: assetId! } } })),
  })
}

// A nota muda as notas, a carteira calculada e a lista de ativos (a nota põe os ativos nela). Saldos, não.
function invalidateTrades(queryClient: ReturnType<typeof useQueryClient>) {
  return Promise.all([
    queryClient.invalidateQueries({ queryKey: tradeNotesKey }),
    queryClient.invalidateQueries({ queryKey: positionsKey }),
    queryClient.invalidateQueries({ queryKey: holdingsKey }),
  ])
}

export type TradeNoteBody = Schemas['SaveTradeNoteRequest']

export function useSaveTradeNote() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: async ({ id, body }: { id?: string; body: TradeNoteBody }) =>
      id
        ? unwrap(await api.PUT('/trade-notes/{id}', { params: { path: { id } }, body }))
        : unwrap(await api.POST('/trade-notes', { body })),
    onSuccess: () => invalidateTrades(queryClient),
  })
}

export function useDeleteTradeNote() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: async (id: string) => {
      const { error, response } = await api.DELETE('/trade-notes/{id}', { params: { path: { id } } })
      if (!response.ok) throw new ApiError(response.status, error)
    },
    onSuccess: () => invalidateTrades(queryClient),
  })
}

export function useRestoreTradeNote() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: async (id: string) => unwrap(await api.POST('/trade-notes/{id}/restore', { params: { path: { id } } })),
    onSuccess: () => invalidateTrades(queryClient),
  })
}
