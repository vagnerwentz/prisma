export type TimelineTransaction = {
  id: string
  purchaseDate: string
  amountCents: number
  installmentPurchaseId?: string | null
}

export type TimelineEntry<T extends TimelineTransaction> =
  | { kind: 'single'; transaction: T }
  | { kind: 'purchase'; purchaseId: string; installments: T[]; totalCents: number }

export type TimelineDay<T extends TimelineTransaction> = { date: string; entries: TimelineEntry<T>[] }

// Agrupa a lista da API (já ordenada, mais recentes primeiro) por dia da compra. As parcelas de
// uma compra têm a mesma PurchaseDate (docs/fase-1.md, 2.2) e viram uma entrada só.
export function buildTimeline<T extends TimelineTransaction>(transactions: T[]): TimelineDay<T>[] {
  const days: TimelineDay<T>[] = []
  const purchases = new Map<string, Extract<TimelineEntry<T>, { kind: 'purchase' }>>()

  for (const transaction of transactions) {
    let day = days.at(-1)
    if (day?.date !== transaction.purchaseDate) {
      day = { date: transaction.purchaseDate, entries: [] }
      days.push(day)
    }

    const purchaseId = transaction.installmentPurchaseId
    if (!purchaseId) {
      day.entries.push({ kind: 'single', transaction })
      continue
    }

    const existing = purchases.get(purchaseId)
    if (existing) {
      existing.installments.push(transaction)
      existing.totalCents += transaction.amountCents
    } else {
      const entry = { kind: 'purchase' as const, purchaseId, installments: [transaction], totalCents: transaction.amountCents }
      purchases.set(purchaseId, entry)
      day.entries.push(entry)
    }
  }

  return days
}
