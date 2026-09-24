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
    expect(days[0].entries.map((e) => (e.kind === 'single' ? e.transaction.id : e.kind))).toEqual(['a', 'b'])
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

// Etapa 1.10 (docs/fase-1.md, 2.3): as duas pontas de uma transferência viram uma linha, com o
// valor transferido (não a soma das pontas).
describe('buildTimeline com transferências', () => {
  const leg = (id: string, direction: 'Out' | 'In', pair = 'pix'): TimelineTransaction => ({
    id,
    purchaseDate: '2026-04-10',
    amountCents: 20000,
    transferPairId: pair,
    transferDirection: direction,
  })

  it('junta as duas pontas numa entrada, com origem e destino', () => {
    const [day] = buildTimeline([leg('in', 'In'), tx('café', '2026-04-10', 500), leg('out', 'Out')])

    expect(day.entries.map((e) => e.kind)).toEqual(['transfer', 'single'])
    const transfer = day.entries[0]
    if (transfer.kind !== 'transfer') return
    expect(transfer.pairId).toBe('pix')
    expect(transfer.out?.id).toBe('out')
    expect(transfer.in?.id).toBe('in')
    expect(transfer.amountCents).toBe(20000)
  })

  it('transferências diferentes no mesmo dia ficam separadas', () => {
    const [day] = buildTimeline([leg('a', 'Out', 'x'), leg('b', 'In', 'x'), leg('c', 'Out', 'y'), leg('d', 'In', 'y')])

    expect(day.entries).toHaveLength(2)
  })
})
