import { keepPreviousData, useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { ApiError, api, unwrap } from '@/lib/api'
import { adminEmailsKey, adminMarketKey, adminPeopleKey, fixedIncomeKey } from '@/lib/queryKeys'

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

// ---- Disparos de e-mail (docs/admin-email.md, 3.3 e 3.4) ----

export function useEmailTemplates() {
  return useQuery({
    queryKey: [...adminEmailsKey, 'templates'],
    queryFn: async () => unwrap(await api.GET('/admin/emails/templates')),
  })
}

// A contagem do público de um modelo para N dias. Sem modelo ou sem N válido não consulta; trocar o número mantém a contagem
// anterior na tela, esmaecida (placeholderData), em vez de piscar o carregamento.
export function useEmailAudience(template: string | null, days: number | null) {
  return useQuery({
    queryKey: [...adminEmailsKey, 'audience', template, days],
    queryFn: async () =>
      unwrap(await api.GET('/admin/emails/audience', { params: { query: { template: template!, days: days! } } })),
    enabled: template !== null && days !== null,
    placeholderData: keepPreviousData,
  })
}

// O e-mail com dados de exemplo. É um POST sem efeito (só renderiza), e o texto muda só com o deploy: vale por um bom tempo.
export function useEmailPreview(template: string | null) {
  return useQuery({
    queryKey: [...adminEmailsKey, 'preview', template],
    queryFn: async () => unwrap(await api.POST('/admin/emails/preview', { body: { template } })),
    enabled: template !== null,
    staleTime: 5 * 60_000,
  })
}

export function useEmailCampaigns(limit = 10) {
  return useQuery({
    queryKey: [...adminEmailsKey, 'campaigns', limit],
    queryFn: async () => unwrap(await api.GET('/admin/emails/campaigns', { params: { query: { limit } } })),
  })
}

// O teste responde 204, sem corpo: o `unwrap` leria isso como falha.
export function useSendEmailTest() {
  return useMutation({
    mutationFn: async (template: string) => {
      const result = await api.POST('/admin/emails/test', { body: { template } })
      if (!result.response.ok) throw new ApiError(result.response.status, result.error)
    },
  })
}

// Depois de qualquer disparo, certo ou recusado, a contagem e o histórico recarregam: um disparo muda o público (quem recebeu sai dele),
// e uma recusa por contagem velha pede a contagem nova.
export function useSendEmailCampaign() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: async (body: { template: string; days: number; expectedCount: number; clientRequestId: string }) =>
      unwrap(await api.POST('/admin/emails/send', { body })),
    onSettled: () => queryClient.invalidateQueries({ queryKey: adminEmailsKey }),
  })
}
