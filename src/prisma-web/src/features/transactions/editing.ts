import type { TimelineDay, TimelineEntry, TimelineTransaction } from './timeline'

type Editable = TimelineTransaction & { statementId?: string | null }

// Escopo da edição (docs/fase-1.md, 2.2): fora do cartão tudo muda; na compra à vista no cartão,
// valor, descrição e categoria; na compra parcelada, a compra inteira (total e parcelas).
export type EditKind = 'simple' | 'card' | 'purchase'

export function editKind<T extends Editable>(entry: TimelineEntry<T>): EditKind {
  if (entry.kind === 'purchase') return 'purchase'
  return entry.transaction.statementId ? 'card' : 'simple'
}

// Identifica o lançamento entre recargas da lista: id da transação ou da compra parcelada.
export function entryKey<T extends TimelineTransaction>(entry: TimelineEntry<T>): string {
  return entry.kind === 'single' ? entry.transaction.id : entry.purchaseId
}

export function findEntry<T extends TimelineTransaction>(days: TimelineDay<T>[], key: string): TimelineEntry<T> | undefined {
  for (const day of days) for (const entry of day.entries) if (entryKey(entry) === key) return entry
  return undefined
}
