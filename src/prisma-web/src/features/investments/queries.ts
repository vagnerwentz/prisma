import { keepPreviousData, useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { api, ApiError, unwrap, type Schemas } from '@/lib/api'
import { invalidateMoney } from '@/features/transactions/queries'
import { holdingsKey, payoutsKey } from '@/lib/queryKeys'
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
