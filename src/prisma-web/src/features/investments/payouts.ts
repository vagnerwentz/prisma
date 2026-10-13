import type { Schemas } from '@/lib/api'
import { groupOf, groupOrder, type PortfolioGroup } from './portfolio'

// Proventos (docs/investimentos.md, etapa 5b): somas e agrupamentos da tela, a partir da lista da API.
export type Payout = Schemas['PayoutResponse']
export type PayoutKind = NonNullable<Schemas['PayoutKind']>

export const payoutKindLabels: Record<PayoutKind, string> = {
  Dividend: 'Dividendo',
  InterestOnEquity: 'JCP',
  FundIncome: 'Rendimento',
}

export const payoutKinds: PayoutKind[] = ['Dividend', 'InterestOnEquity', 'FundIncome']

// O tipo que o form sugere: fundo paga rendimento; ação, unit e BDR, dividendo.
export function suggestedKind(assetKind: Schemas['AssetKind']): PayoutKind {
  return assetKind === 'Stock' || assetKind === 'Unit' || assetKind === 'Bdr' || assetKind === 'Unknown'
    ? 'Dividend'
    : 'FundIncome'
}

// Soma do ano pela data do pagamento ("2026-09-30" → 2026).
export function yearTotal(payouts: Payout[], year: number): number {
  const prefix = `${year}-`
  return payouts.reduce((sum, p) => (p.date.startsWith(prefix) ? sum + p.amountCents : sum), 0)
}

// Quanto cada ativo já pagou, desde sempre.
export function totalsByAsset(payouts: Payout[]): Map<string, number> {
  const totals = new Map<string, number>()
  for (const p of payouts) totals.set(p.assetId, (totals.get(p.assetId) ?? 0) + p.amountCents)
  return totals
}

export type PayoutDay = { date: string; items: Payout[]; totalCents: number }

// Os proventos por dia de pagamento, do mais recente ao mais antigo (a API já manda nessa ordem).
export function groupByDay(payouts: Payout[]): PayoutDay[] {
  const days: PayoutDay[] = []
  for (const p of payouts) {
    let day = days.at(-1)
    if (day?.date !== p.date) {
      day = { date: p.date, items: [], totalCents: 0 }
      days.push(day)
    }
    day.items.push(p)
    day.totalCents += p.amountCents
  }
  return days
}

type AccountOption = { id: string; type: Schemas['AccountType']; isActive: boolean }

// Onde o provento cai: contas ativas, menos cartão de crédito.
export function payoutAccounts<T extends AccountOption>(accounts: T[]): T[] {
  return accounts.filter((a) => a.isActive && a.type !== 'CreditCard')
}

// A conta já escolhida no form: a do último provento (quem recebe no Itaú recebe sempre no Itaú); senão a
// primeira conta de investimento; senão a primeira que serve.
export function defaultPayoutAccount<T extends AccountOption>(accounts: T[], payouts: Payout[]): T | undefined {
  const options = payoutAccounts(accounts)
  const last = payouts[0] && options.find((a) => a.id === payouts[0].accountId)
  return last ?? options.find((a) => a.type === 'Investment') ?? options[0]
}

// Filtros da aba Proventos (docs/investimentos.md, I.7e): tipo de provento e grupo do ativo, um de cada (null: todos).
export type PayoutFilter = { kind: PayoutKind | null; group: PortfolioGroup | null }

export function filterPayouts(payouts: Payout[], filter: PayoutFilter): Payout[] {
  return payouts.filter(
    (p) => (filter.kind === null || p.kind === filter.kind) && (filter.group === null || groupOf(p.assetKind) === filter.group),
  )
}

export const inYear = (payouts: Payout[], year: number) => payouts.filter((p) => p.date.startsWith(`${year}-`))

// Recebido até hoje e a receber (data futura), somados.
export function receivedSplit(payouts: Payout[], today: string): { receivedCents: number; upcomingCents: number } {
  let receivedCents = 0
  let upcomingCents = 0
  for (const p of payouts) {
    if (p.date <= today) receivedCents += p.amountCents
    else upcomingCents += p.amountCents
  }
  return { receivedCents, upcomingCents }
}

export type MonthBar = { month: number; receivedCents: number; upcomingCents: number }

// Os 12 meses do ano, cada um com o recebido e o a receber.
export function monthBars(payouts: Payout[], year: number, today: string): MonthBar[] {
  const bars = Array.from({ length: 12 }, (_, i) => ({ month: i + 1, receivedCents: 0, upcomingCents: 0 }))
  for (const p of inYear(payouts, year)) {
    const bar = bars[Number(p.date.slice(5, 7)) - 1]
    if (p.date <= today) bar.receivedCents += p.amountCents
    else bar.upcomingCents += p.amountCents
  }
  return bars
}

// Um total por ano, de todos os anos dados (inclusive os sem provento), em ordem.
export function yearBars(payouts: Payout[], years: number[]): { year: number; cents: number }[] {
  return years.map((year) => ({ year, cents: yearTotal(payouts, year) }))
}

export type PayoutRank = {
  assetId: string
  symbol: string
  assetName: string
  assetKind: Payout['assetKind']
  assetHasLogo: boolean
  cents: number
  share: number
}

// Quem mais pagou, do maior ao menor (empate: pelo código), com a parte de cada um no total (0 a 1).
export function rankByAsset(payouts: Payout[]): PayoutRank[] {
  const byAsset = new Map<string, PayoutRank>()
  let total = 0
  for (const p of payouts) {
    total += p.amountCents
    const rank = byAsset.get(p.assetId)
    if (rank) rank.cents += p.amountCents
    else
      byAsset.set(p.assetId, {
        assetId: p.assetId,
        symbol: p.symbol,
        assetName: p.assetName,
        assetKind: p.assetKind,
        assetHasLogo: p.assetHasLogo,
        cents: p.amountCents,
        share: 0,
      })
  }
  return [...byAsset.values()]
    .map((r) => ({ ...r, share: total === 0 ? 0 : r.cents / total }))
    .sort((a, b) => b.cents - a.cents || a.symbol.localeCompare(b.symbol))
}

// Os grupos que pagaram algo, na ordem dos grupos: só eles viram filtro.
export function payingGroups(payouts: Payout[]): PortfolioGroup[] {
  const present = new Set(payouts.map((p) => groupOf(p.assetKind)))
  return groupOrder.filter((g) => present.has(g))
}

export const monthInitials = ['J', 'F', 'M', 'A', 'M', 'J', 'J', 'A', 'S', 'O', 'N', 'D']

// Média por mês do que já caiu: num ano passado, ÷ 12; no corrente, ÷ os meses até o atual (inclusive).
export function monthlyAverage(bars: MonthBar[], year: number, today: string): number {
  const months = year < Number(today.slice(0, 4)) ? 12 : Number(today.slice(5, 7))
  return Math.round(bars.reduce((sum, b) => sum + b.receivedCents, 0) / months)
}

// O valor curto em cima da coluna do ano: "1,2 mil", "380" (reais, sem centavos).
export function shortReais(cents: number): string {
  const reais = cents / 100
  if (reais >= 1000) return `${(reais / 1000).toLocaleString('pt-BR', { maximumFractionDigits: 1 })} mil`
  return Math.round(reais).toLocaleString('pt-BR')
}
