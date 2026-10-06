export type TimelineTransaction = {
  id: string
  purchaseDate: string
  amountCents: number
  installmentPurchaseId?: string | null
  installmentNumber?: number | null
  transferPairId?: string | null
  transferDirection?: 'Out' | 'In' | null
  accountId?: string
  assetId?: string | null
}

export type TimelineEntry<T extends TimelineTransaction> =
  | { kind: 'single'; transaction: T }
  | { kind: 'purchase'; purchaseId: string; installments: T[]; totalCents: number }
  | { kind: 'transfer'; pairId: string; out?: T; in?: T; amountCents: number }
  // Proventos do mesmo dia na mesma conta (docs/investimentos.md, etapa 5b): uma linha que abre os itens.
  | { kind: 'payouts'; key: string; accountId: string; items: T[]; totalCents: number }

export type TimelineDay<T extends TimelineTransaction> = { date: string; entries: TimelineEntry<T>[] }

// Agrupa a lista da API (já ordenada, mais recentes primeiro) por dia da compra. As parcelas de
// uma compra têm a mesma PurchaseDate (docs/fase-1.md, 2.2) e viram uma entrada só; as duas pontas
// de uma transferência também (2.3), com o valor transferido. As parcelas ficam em ordem (a API não as
// devolve assim): a primeira da entrada é a parcela 1, cuja fatura a linha mostra na compra movida. Os proventos
// do mesmo dia na mesma conta também viram uma entrada (quem tem 15 FIIs recebe 15 no mesmo dia).
export function buildTimeline<T extends TimelineTransaction>(transactions: T[]): TimelineDay<T>[] {
  const days: TimelineDay<T>[] = []
  const purchases = new Map<string, Extract<TimelineEntry<T>, { kind: 'purchase' }>>()
  const transfers = new Map<string, Extract<TimelineEntry<T>, { kind: 'transfer' }>>()

  for (const transaction of transactions) {
    let day = days.at(-1)
    if (day?.date !== transaction.purchaseDate) {
      day = { date: transaction.purchaseDate, entries: [] }
      days.push(day)
    }

    const pairId = transaction.transferPairId
    if (pairId) {
      const side = transaction.transferDirection === 'Out' ? 'out' : 'in'
      const existing = transfers.get(pairId)
      if (existing) existing[side] = transaction
      else {
        const entry = { kind: 'transfer' as const, pairId, [side]: transaction, amountCents: transaction.amountCents }
        transfers.set(pairId, entry)
        day.entries.push(entry)
      }
      continue
    }

    if (transaction.assetId) {
      const accountId = transaction.accountId ?? ''
      const key = `payouts:${transaction.purchaseDate}:${accountId}`
      const existing = day.entries.find((e) => e.kind === 'payouts' && e.key === key)
      if (existing?.kind === 'payouts') {
        existing.items.push(transaction)
        existing.totalCents += transaction.amountCents
      } else day.entries.push({ kind: 'payouts', key, accountId, items: [transaction], totalCents: transaction.amountCents })
      continue
    }

    const purchaseId = transaction.installmentPurchaseId
    if (!purchaseId) {
      day.entries.push({ kind: 'single', transaction })
      continue
    }

    const existing = purchases.get(purchaseId)
    if (existing) {
      existing.installments.push(transaction)
      existing.installments.sort((a, b) => (a.installmentNumber ?? 0) - (b.installmentNumber ?? 0))
      existing.totalCents += transaction.amountCents
    } else {
      const entry = { kind: 'purchase' as const, purchaseId, installments: [transaction], totalCents: transaction.amountCents }
      purchases.set(purchaseId, entry)
      day.entries.push(entry)
    }
  }

  return days
}
