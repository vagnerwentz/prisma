import { keepPreviousData, useQuery } from '@tanstack/react-query'
import { api, unwrap, type Schemas } from '@/lib/api'
import { dashboardKey } from '@/lib/queryKeys'

export type MonthlySummary = Schemas['GetMonthlySummaryResponse']
export type CategoryTotal = Schemas['ListCategoryTotalsItem']

// As consultas por mês mantêm o mês anterior na tela enquanto o novo carrega (placeholderData): trocar
// de mês é a ação mais frequente, e o esqueleto piscando a cada toque fazia a página pular. A tela
// esmaece o que é do mês anterior (StaleFade).

// Receitas, despesas, sobra e investido do mês pela data de caixa (docs/fase-2.md, 2.1).
export function useMonthlySummary(month: string) {
  return useQuery({
    queryKey: [...dashboardKey, 'summary', month],
    placeholderData: keepPreviousData,
    queryFn: async () => unwrap(await api.GET('/dashboard/summary', { params: { query: { month } } })),
  })
}

// Despesas do mês pela categoria raiz, estornos abatidos, maior primeiro; sem categoria vem com id e
// nome nulos. hiddenRefundCents: estorno que sumiu com categorias zeradas (docs/fase-2.md, 2.5).
export function useCategoryTotals(month: string) {
  return useQuery({
    queryKey: [...dashboardKey, 'categories', month],
    placeholderData: keepPreviousData,
    queryFn: async () => unwrap(await api.GET('/dashboard/categories', { params: { query: { month } } })),
  })
}

// Os 6 meses que terminam no escolhido, mais antigo primeiro (docs/fase-2.md, 2.3).
export function useMonthlyHistory(month: string) {
  return useQuery({
    queryKey: [...dashboardKey, 'history', month],
    placeholderData: keepPreviousData,
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
    placeholderData: keepPreviousData,
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

export type SpendingVariation = Schemas['GetSpendingVariationResponse']

// Por que o gasto mudou: a diferença do "Saiu" para o mês anterior e os motivos, que somam
// exatamente a diferença (docs/fase-2.md, 2.7).
export function useSpendingVariation(month: string) {
  return useQuery({
    queryKey: [...dashboardKey, 'variation', month],
    placeholderData: keepPreviousData,
    queryFn: async () => unwrap(await api.GET('/dashboard/variation', { params: { query: { month } } })),
  })
}
