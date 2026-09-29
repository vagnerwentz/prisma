import { formatMonth, monthOf } from '@/lib/dates'
import { formatCents } from '@/lib/money'

// Compromissos herdados (docs/fase-2.md, 2.6).

// Parte herdada do "Saiu", em % inteiro. Sem "decidido no mês" (estornos deixaram o "Saiu" abaixo
// do herdado), não há divisão a mostrar.
export function inheritedShare(inheritedCents: number, decidedInMonthCents: number | null): number | null {
  if (decidedInMonthCents === null) return null
  const expense = inheritedCents + decidedInMonthCents
  return expense > 0 ? Math.round((inheritedCents / expense) * 100) : null
}

// Largura de cada barra em % do maior mês. Um mês pequeno fica com um mínimo visível; zero e
// negativo (estornos maiores que as compras), sem barra.
export function barWidths(values: number[]): number[] {
  const largest = Math.max(0, ...values)
  if (largest === 0) return values.map(() => 0)
  return values.map((v) => (v > 0 ? Math.max(3, Math.round((v / largest) * 100)) : 0))
}

// A barra do "Já comprometido" com a parte prevista das séries (docs/fase-2.md, 2.14, regra 11): o que já
// existe, e depois o previsto, mais claro. As duas em % do maior mês somando as partes; parte pequena com
// um mínimo visível.
export function committedBars(months: { expenseCents: number; projectedExpenseCents: number }[]) {
  const largest = Math.max(0, ...months.map((m) => Math.max(0, m.expenseCents) + m.projectedExpenseCents))
  const width = (cents: number) => (largest > 0 && cents > 0 ? Math.max(3, Math.round((cents / largest) * 100)) : 0)
  return months.map((m) => ({ solid: width(m.expenseCents), projected: width(m.projectedExpenseCents) }))
}

export const projectedText = (cents: number) => `+ ${formatCents(cents)} previstos`

const monthShort = new Intl.DateTimeFormat('pt-BR', { month: 'short', timeZone: 'UTC' })

// "2026-11" → "nov/26": seis meses podem atravessar o ano.
export function shortMonth(month: string): string {
  const { year, month: m } = monthOf(`${month}-01`)
  const name = monthShort.format(new Date(Date.UTC(year, m - 1, 1))).replace('.', '')
  return `${name}/${String(year).slice(2)}`
}

export function lastInstallmentText(month: string): string {
  return `A última parcela vence em ${formatMonth(monthOf(`${month}-01`)).toLowerCase()}`
}
