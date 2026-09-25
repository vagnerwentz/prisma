import { useQuery } from '@tanstack/react-query'
import { api, unwrap, type Schemas } from '@/lib/api'
import { dashboardKey } from '@/lib/queryKeys'

export type MonthlySummary = Schemas['GetMonthlySummaryResponse']
export type CategoryTotal = Schemas['ListCategoryTotalsResponse']

// Receitas, despesas, sobra e investido do mês pela data de caixa (docs/fase-2.md, 2.1).
export function useMonthlySummary(month: string) {
  return useQuery({
    queryKey: [...dashboardKey, 'summary', month],
    queryFn: async () => unwrap(await api.GET('/dashboard/summary', { params: { query: { month } } })),
  })
}

// Despesas do mês pela categoria raiz, maior primeiro; sem categoria vem com id e nome nulos.
export function useCategoryTotals(month: string) {
  return useQuery({
    queryKey: [...dashboardKey, 'categories', month],
    queryFn: async () => unwrap(await api.GET('/dashboard/categories', { params: { query: { month } } })),
  })
}

// Os 6 meses que terminam no escolhido, mais antigo primeiro (docs/fase-2.md, 2.3).
export function useMonthlyHistory(month: string) {
  return useQuery({
    queryKey: [...dashboardKey, 'history', month],
    queryFn: async () => unwrap(await api.GET('/dashboard/history', { params: { query: { month } } })),
  })
}
