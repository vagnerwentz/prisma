import type { Schemas } from '@/lib/api'
import { formatMonth, monthOf } from '@/lib/dates'
import { formatCents } from '@/lib/money'
import { expenseChange } from './history'

export type VariationReason = Schemas['GetSpendingVariationReason']

// Por que o gasto mudou (docs/fase-2.md, 2.7). O backend calcula os motivos; aqui só se redigem.

// "2026-08" → "agosto", para o meio da frase.
export function monthNameOf(month: string): string {
  return formatMonth(monthOf(`${month}-01`))
    .split(' de ')[0]
    .toLowerCase()
}

// Variação sempre com sinal, e o "−" tipográfico, como nos lançamentos.
export function signedChange(cents: number): string {
  return `${cents < 0 ? '−' : '+'}${formatCents(Math.abs(cents))}`
}

// "Gastou 6% a mais que em agosto (+R$ 150,00)". A porcentagem é a do comparativo (2.3): sem gasto
// no mês anterior, só o valor. Sem gasto nos dois meses, nada a dizer.
export function variationHeadline(expenseCents: number, previousExpenseCents: number, previousMonthName: string): string | null {
  const diff = expenseCents - previousExpenseCents
  if (expenseCents === 0 && previousExpenseCents === 0) return null
  if (diff === 0) return `Gastou o mesmo que em ${previousMonthName}`

  const direction = diff > 0 ? 'mais' : 'menos'
  const change = expenseChange(expenseCents, previousExpenseCents)
  if (!change) return `Gastou ${formatCents(Math.abs(diff))} a ${direction} que em ${previousMonthName}`
  if (change.direction === 'same') return `Gastou quase o mesmo que em ${previousMonthName} (${signedChange(diff)})`
  return `Gastou ${change.percent}% a ${direction} que em ${previousMonthName} (${signedChange(diff)})`
}

export function reasonTitle(reason: VariationReason): string {
  if (reason.kind === 'Inherited') return 'Parcelas de compras anteriores'
  if (reason.kind === 'OtherCategories') return 'Outras categorias'
  return reason.name ?? 'Sem categoria'
}

// Até dois nomes; o resto vira "e mais N".
function names(items: { description: string }[]): string {
  const shown = items.slice(0, 2).map((i) => i.description || 'Sem descrição')
  const rest = items.length - shown.length
  if (rest > 0) return `${shown.join(', ')} e mais ${rest}`
  return shown.join(' e ')
}

// Detalhe embaixo do motivo: o maior lançamento da categoria que subiu; nas parcelas, as compras que
// começaram a ser herdadas no mês e as que terminaram no anterior.
export function reasonDetail(reason: VariationReason, previousMonthName = ''): string | null {
  if (reason.kind === 'Category' && reason.largest) {
    const { description, amountCents } = reason.largest
    return `O maior foi ${description || reasonTitle(reason)}, ${formatCents(amountCents)}`
  }
  if (reason.kind !== 'Inherited') return null

  const parts: string[] = []
  if (reason.started.length > 0) parts.push(`${reason.started.length > 1 ? 'Começaram' : 'Começou'}: ${names(reason.started)}`)
  if (reason.ended.length > 0)
    parts.push(`${reason.ended.length > 1 ? 'Terminaram' : 'Terminou'} em ${previousMonthName}: ${names(reason.ended)}`)
  return parts.length > 0 ? parts.join(' · ') : null
}

// A linha do Resumo: o maior motivo que vai na mesma direção da diferença (um motivo contrário não
// explica o "a mais"). Os motivos já vêm do maior para o menor.
export function mainReason(reasons: VariationReason[], changeCents: number): string | null {
  if (changeCents === 0) return null
  const main = reasons.find((r) => Math.sign(r.changeCents) === Math.sign(changeCents))
  if (!main) return null

  const title =
    main.kind === 'Category' && main.name
      ? main.name
      : main.kind === 'Category'
        ? 'lançamentos sem categoria'
        : reasonTitle(main).toLowerCase()
  return `Principalmente ${title}, ${signedChange(main.changeCents)}`
}
