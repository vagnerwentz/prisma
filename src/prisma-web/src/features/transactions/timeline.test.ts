import { describe, expect, it } from 'vitest'
import { buildTimeline, type TimelineTransaction } from './timeline'

const tx = (id: string, purchaseDate: string, amountCents: number, installmentPurchaseId?: string): TimelineTransaction => ({
  id,
  purchaseDate,
  amountCents,
  installmentPurchaseId: installmentPurchaseId ?? null,
})

describe('buildTimeline', () => {
  it('agrupa por dia mantendo a ordem da API (mais recentes primeiro)', () => {
    const days = buildTimeline([tx('a', '2026-03-12', 100), tx('b', '2026-03-12', 200), tx('c', '2026-03-10', 300)])

    expect(days.map((d) => d.date)).toEqual(['2026-03-12', '2026-03-10'])
    expect(days[0].entries.map((e) => (e.kind === 'single' ? e.transaction.id : e.purchaseId))).toEqual(['a', 'b'])
  })

  // As parcelas compartilham a data da compra (docs/fase-1.md, 2.2): viram uma linha só.
  it('junta as parcelas de uma compra numa entrada com o total exato', () => {
    const installments = [10001, 10001, 10001, 10001, 10001, 10000, 10000, 10000, 10000, 10000].map((cents, i) =>
      tx(`p${i + 1}`, '2026-03-10', cents, 'notebook'),
    )

    const [day] = buildTimeline([tx('pão', '2026-03-10', 459), ...installments])

    expect(day.entries).toHaveLength(2)
    const purchase = day.entries[1]
    expect(purchase.kind).toBe('purchase')
    if (purchase.kind !== 'purchase') return
    expect(purchase.installments).toHaveLength(10)
    expect(purchase.totalCents).toBe(100005)
  })

  it('compras parceladas diferentes no mesmo dia ficam separadas', () => {
    const [day] = buildTimeline([
      tx('a1', '2026-03-10', 500, 'a'),
      tx('b1', '2026-03-10', 700, 'b'),
      tx('a2', '2026-03-10', 500, 'a'),
    ])

    expect(day.entries.map((e) => (e.kind === 'purchase' ? [e.purchaseId, e.totalCents] : null))).toEqual([
      ['a', 1000],
      ['b', 700],
    ])
  })

  it('lista vazia não tem dias', () => {
    expect(buildTimeline([])).toEqual([])
  })
})
