import { useQuery } from '@tanstack/react-query'
import { api, unwrap, type Schemas } from '@/lib/api'
import { dashboardKey } from '@/lib/queryKeys'

export type MonthlySummary = Schemas['GetMonthlySummaryResponse']

// Receitas, despesas, sobra e investido do mês pela data de caixa (docs/fase-2.md, 2.1).
export function useMonthlySummary(month: string) {
  return useQuery({
    queryKey: [...dashboardKey, 'summary', month],
    queryFn: async () => unwrap(await api.GET('/dashboard/summary', { params: { query: { month } } })),
  })
}
