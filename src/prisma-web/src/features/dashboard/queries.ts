import { useQuery } from '@tanstack/react-query'
import { api, unwrap, type Schemas } from '@/lib/api'
import { dashboardKey } from '@/lib/queryKeys'

export type MonthlySummary = Schemas['GetMonthlySummaryResponse']
export type CategoryTotal = Schemas['ListCategoryTotalsItem']

// Receitas, despesas, sobra e investido do mês pela data de caixa (docs/fase-2.md, 2.1).
export function useMonthlySummary(month: string) {
  return useQuery({
    queryKey: [...dashboardKey, 'summary', month],
    queryFn: async () => unwrap(await api.GET('/dashboard/summary', { params: { query: { month } } })),
  })
}

// Despesas do mês pela categoria raiz, estornos abatidos, maior primeiro; sem categoria vem com id e
// nome nulos. hiddenRefundCents: estorno que sumiu com categorias zeradas (docs/fase-2.md, 2.5).
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

export type UpcomingStatement = Schemas['ListUpcomingStatementsItem']

// A próxima fatura não paga de cada cartão ativo, olhando para hoje (docs/fase-2.md, 2.4).
export function useUpcomingStatements() {
  return useQuery({
    queryKey: [...dashboardKey, 'upcoming-statements'],
    queryFn: async () => unwrap(await api.GET('/dashboard/upcoming-statements')),
  })
}

export type InheritedInstallment = Schemas['GetInheritedInstallmentsItem']

// Parcelas de compras anteriores que vencem no mês, maior primeiro (docs/fase-2.md, 2.6).
export function useInheritedInstallments(month: string) {
  return useQuery({
    queryKey: [...dashboardKey, 'inherited', month],
    queryFn: async () => unwrap(await api.GET('/dashboard/inherited', { params: { query: { month } } })),
  })
}

// O "Saiu" já lançado dos 6 meses seguintes ao de hoje e o mês da última parcela (docs/fase-2.md, 2.6).
export function useCommittedMonths() {
  return useQuery({
    queryKey: [...dashboardKey, 'committed'],
    queryFn: async () => unwrap(await api.GET('/dashboard/committed')),
  })
}
