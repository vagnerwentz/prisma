import type { TimelineDay, TimelineEntry, TimelineTransaction } from './timeline'

type Editable = TimelineTransaction & { statementId?: string | null }

// Escopo da edição (docs/fase-1.md, 2.2 e 2.3): fora do cartão tudo muda; na compra à vista no
// cartão, valor, data, descrição e categoria; na compra parcelada, a compra inteira. Transferência
// não é editada: exclui-se e lança-se de novo.
export type EditKind = 'simple' | 'card' | 'purchase' | 'transfer'

export function editKind<T extends Editable>(entry: TimelineEntry<T>): EditKind {
  if (entry.kind === 'purchase') return 'purchase'
  if (entry.kind === 'transfer') return 'transfer'
  return entry.transaction.statementId ? 'card' : 'simple'
}

// Identifica o lançamento entre recargas da lista: id da transação, da compra parcelada ou do par
// da transferência.
export function entryKey<T extends TimelineTransaction>(entry: TimelineEntry<T>): string {
  if (entry.kind === 'single') return entry.transaction.id
  return entry.kind === 'purchase' ? entry.purchaseId : entry.pairId
}

export function findEntry<T extends TimelineTransaction>(days: TimelineDay<T>[], key: string): TimelineEntry<T> | undefined {
  for (const day of days) for (const entry of day.entries) if (entryKey(entry) === key) return entry
  return undefined
}
