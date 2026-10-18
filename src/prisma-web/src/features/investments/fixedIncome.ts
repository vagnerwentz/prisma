import { addDays, formatShortDate } from '@/lib/dates'
import type { Schemas } from '@/lib/api'

// Renda fixa por objetivo (docs/renda-fixa.md, seção 7). Só bruto: imposto e líquido vêm na R.7. Aqui ficam as
// contas e os textos puros; a tela só desenha.
export type Goal = Schemas['GoalResponse']
export type Deposit = Schemas['DepositResponse']
export type Balances = Schemas['BalancesResponse']
export type RatesStatus = Schemas['RatesStatus']
export type GroupBalance = Schemas['GroupBalance']
export type DepositBalance = Schemas['DepositBalance']
export type EvolutionPoint = Schemas['EvolutionPoint']

// O percentual do CDI viaja em E4: 100% = 1_000_000 (a API aceita de 100 a 5_000_000).
export const percentScale = 10_000
export const defaultIndexPercentE4 = 100 * percentScale

export function percentToE4(percent: number): number {
  return Math.round(percent * percentScale)
}

export function percentFromE4(e4: number): number {
  return e4 / percentScale
}

// "100", "112,5" → 100, 112.5. Texto inválido ou fora de 0,01% a 500%: null.
export function parsePercent(text: string): number | null {
  const normalized = text.trim().replace('%', '').replace(/\s/g, '').replace(',', '.')
  if (!/^\d+(\.\d{1,4})?$/.test(normalized)) return null
  const value = Number(normalized)
  return value >= 0.01 && value <= 500 ? value : null
}

// 100 → "100%"; 112.5 → "112,5%".
export function formatPercent(percent: number): string {
  return `${percent.toLocaleString('pt-BR', { maximumFractionDigits: 4 })}%`
}

export function formatPercentE4(e4: number): string {
  return `${formatPercent(percentFromE4(e4))} do CDI`
}

// Quanto da meta já foi: 0 a 1, sem passar de 1. Sem meta: null (a barra nem aparece).
export function goalProgress(balanceCents: number, targetCents: number | null): number | null {
  if (targetCents === null || targetCents <= 0) return null
  return Math.min(1, Math.max(0, balanceCents / targetCents))
}

export function progressLabel(balanceCents: number, targetCents: number | null): string | null {
  const progress = goalProgress(balanceCents, targetCents)
  return progress === null ? null : `${Math.floor(progress * 100)}% da meta`
}

export type RatesNotice = { kind: 'unavailable' | 'stale' | 'incomplete'; text: string }

// Os avisos de taxa, cada um com texto próprio (seção 7.3). Sem taxa nenhuma, nada foi calculado; defasada, o saldo
// pode estar abaixo do real; incompleta, faltam dias no meio.
export function ratesNotice(rates: RatesStatus): RatesNotice | null {
  if (!rates.available || !rates.through)
    return {
      kind: 'unavailable',
      text: 'Ainda não temos as taxas do CDI. Os valores abaixo são só o que você aportou.',
    }
  if (!rates.complete)
    return {
      kind: 'incomplete',
      text: 'Faltam taxas do CDI em alguns dias. Nesses dias o saldo entra pelo valor aportado.',
    }
  if (rates.stale)
    return {
      kind: 'stale',
      text: `As taxas do CDI estão sem atualizar desde ${formatShortDate(rates.through)}. O saldo pode estar abaixo do real.`,
    }
  return null
}

// A nota que fica sempre visível (seção 7.3).
export function ratesFootnote(rates: RatesStatus): string {
  return rates.through
    ? `Cálculo pelo CDI publicado pelo Banco Central até ${formatShortDate(rates.through)}. Estimativa; o extrato do banco manda.`
    : 'Cálculo pelo CDI publicado pelo Banco Central. Estimativa; o extrato do banco manda.'
}

export function depositCountLabel(count: number): string {
  return count === 1 ? '1 aporte' : `${count} aportes`
}

// O aporte sem taxa não some: entra pelo principal e avisa (decisão do dono).
export function depositUnavailableNote(deposit: DepositBalance): string | null {
  if (deposit.unavailable === null) return null
  return deposit.unavailable === 'BeforeFirstRate'
    ? 'Aporte antes da primeira taxa que temos: entra pelo valor aportado.'
    : 'Sem taxa do CDI para este aporte: entra pelo valor aportado.'
}

export type Period = '1M' | '3M' | '6M' | 'ALL'
export const periods: Period[] = ['1M', '3M', '6M', 'ALL']
export const periodLabels: Record<Period, string> = { '1M': '1M', '3M': '3M', '6M': '6M', ALL: 'Tudo' }

// O recorte do gráfico: de quando até hoje, e de quanto em quanto tempo mostrar um ponto.
export function periodRange(period: Period, today: string, firstDeposit: string | null): { from: string; step: 'day' | 'week' | 'month' } {
  const days = { '1M': 31, '3M': 92, '6M': 183, ALL: 0 }[period]
  if (period === 'ALL') {
    const from = firstDeposit ?? today
    const span = daysBetween(from, today)
    return { from, step: span > 365 ? 'month' : span > 120 ? 'week' : 'day' }
  }
  return { from: addDays(today, -days), step: period === '6M' ? 'week' : 'day' }
}

function daysBetween(from: string, to: string): number {
  return Math.round((Date.parse(`${to}T00:00:00Z`) - Date.parse(`${from}T00:00:00Z`)) / 86_400_000)
}

// "all", "none" ou o id do objetivo, como a API entende.
export type GoalScope = 'all' | 'none' | string

// Quem o endereço chama de objetivo: ?objetivo=<id> ou ?objetivo=sem (os aportes soltos).
export const looseGoalParam = 'sem'
