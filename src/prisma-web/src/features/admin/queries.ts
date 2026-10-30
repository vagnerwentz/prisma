import { keepPreviousData, useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { api, unwrap } from '@/lib/api'
import { adminMarketKey, adminPeopleKey, fixedIncomeKey } from '@/lib/queryKeys'

export function useMarketData() {
  return useQuery({
    queryKey: adminMarketKey,
    queryFn: async () => unwrap(await api.GET('/admin/market-data')),
  })
}

// A contagem de pessoas para N dias (docs/admin-email.md, 3.1). Sem N válido não consulta. Trocar o número mantém a contagem
// anterior na tela, esmaecida (placeholderData), em vez de piscar o carregamento.
export function useUserSummary(days: number | null) {
  return useQuery({
    queryKey: [...adminPeopleKey, days],
    queryFn: async () => unwrap(await api.GET('/admin/users/summary', { params: { query: { days: days! } } })),
    enabled: days !== null,
    placeholderData: keepPreviousData,
  })
}

export function useSyncRuns(series: string, limit = 20) {
  return useQuery({
    queryKey: [...adminMarketKey, 'runs', series, limit],
    queryFn: async () =>
      unwrap(await api.GET('/admin/market-data/{series}/runs', { params: { path: { series }, query: { limit } } })),
  })
}

// Depois de qualquer disparo, certo ou errado, o estado e a lista recarregam: a execução que falhou também fica gravada. Se
// entrou taxa, os saldos da renda fixa mudam.
function useMarketMutation<TInput, TOutput>(mutationFn: (input: TInput) => Promise<TOutput>) {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn,
    onSettled: async (_data, error) => {
      await queryClient.invalidateQueries({ queryKey: adminMarketKey })
      if (!error) await queryClient.invalidateQueries({ queryKey: fixedIncomeKey })
    },
  })
}

export const useSyncMarket = () =>
  useMarketMutation(async (series: string) =>
    unwrap(await api.POST('/admin/market-data/{series}/sync', { params: { path: { series } } })),
  )

export const useRefreshMarket = () =>
  useMarketMutation(async ({ series, from, to }: { series: string; from: string; to: string }) =>
    unwrap(await api.POST('/admin/market-data/{series}/refresh', { params: { path: { series } }, body: { from, to } })),
  )
