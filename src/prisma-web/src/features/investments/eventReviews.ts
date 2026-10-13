import type { Schemas } from '@/lib/api'
import { formatShortDate } from '@/lib/dates'
import { formatCents } from '@/lib/money'
import { unitsLabel } from './trades'

// Conferir eventos (docs/investimentos.md, I.7c2): o que cada evento fez com a quantidade da pessoa, como a tela diz.
export type EventReview = Schemas['ListEventReviewsEventReviewItem']

// "SNAG11 · desdobramento 1:10", "BBAS3 · grupamento 10:1", "PETZ3 → AUAU3 · conversão", "BCFF11 → BTHF11 · liquidação".
export function eventReviewTitle(e: EventReview): string {
  switch (e.kind) {
    case 'Split':
      // A proporção presa à palavra (espaço que não quebra): em 320 px, "1:10" não fica sozinho na linha.
      return `${e.symbol} · ${e.toQuantity >= e.fromQuantity ? 'desdobramento' : 'grupamento'}\u00a0${e.fromQuantity}:${e.toQuantity}`
    case 'Conversion':
      return `${e.symbol} → ${e.targetSymbol} · conversão`
    default:
      return `${e.symbol} → ${e.targetSymbol} · liquidação`
  }
}

// "01/08/2023 · 19 → 190 cotas", "02/01/2026 · 50 ações → 50 AUAU3", "16/12/2024 · 896 cotas → 716 BTHF11 a R$ 10,25".
export function eventReviewDetail(e: EventReview): string {
  const kind = e.assetKind ?? 'Unknown'
  const date = formatShortDate(e.date)
  if (e.kind === 'Split') return `${date} · ${e.quantityBefore.toLocaleString('pt-BR')} → ${unitsLabel(kind, e.quantityAfter)}`
  const price = e.targetUnitPriceCents === null ? '' : ` a ${formatCents(e.targetUnitPriceCents)}`
  return `${date} · ${unitsLabel(kind, e.quantityBefore)} → ${e.quantityAfter.toLocaleString('pt-BR')} ${e.targetSymbol}${price}`
}

// "Conferir todos" só quando há mais de um: com um, o próprio "Confere" basta.
export const showsConfirmAll = (count: number) => count > 1

// O aviso depois de conferir: "SNAG11 conferido", "3 eventos conferidos".
export const confirmedToast = (items: EventReview[]) =>
  items.length === 1 ? `${items[0].symbol} conferido` : `${items.length} eventos conferidos`

// A quantidade da corretora no "Não confere": só dígitos, até o teto da API; vazio é "não sei".
export function parseReportedQuantity(text: string): number | null {
  const digits = text.replace(/\D/g, '')
  if (digits === '') return null
  return Math.min(Number(digits), 100_000_000)
}
