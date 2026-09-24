import { describe, expect, it } from 'vitest'
import { editKind, entryKey, findEntry } from './editing'
import { buildTimeline } from './timeline'

const base = { purchaseDate: '2026-03-10', amountCents: 1000, statementId: null, installmentPurchaseId: null, installmentNumber: null }

describe('editKind', () => {
  // docs/fase-1.md, 2.2: o escopo da edição depende de onde o lançamento está.
  it('fora do cartão, edita tudo', () => {
    const [day] = buildTimeline([{ ...base, id: 'a' }])
    expect(editKind(day.entries[0])).toBe('simple')
  })

  it('compra à vista no cartão edita valor, descrição e categoria', () => {
    const [day] = buildTimeline([{ ...base, id: 'a', statementId: 's1' }])
    expect(editKind(day.entries[0])).toBe('card')
  })

  it('compra parcelada edita a compra inteira', () => {
    const [day] = buildTimeline([
      { ...base, id: 'a', statementId: 's1', installmentPurchaseId: 'p', installmentNumber: 1 },
      { ...base, id: 'b', statementId: 's2', installmentPurchaseId: 'p', installmentNumber: 2 },
    ])
    expect(editKind(day.entries[0])).toBe('purchase')
  })

  // Uma edição pode reduzir a compra a 1 parcela: continua sendo compra parcelada.
  it('compra reduzida a uma parcela continua editada como compra', () => {
    const [day] = buildTimeline([{ ...base, id: 'a', statementId: 's1', installmentPurchaseId: 'p', installmentNumber: 1 }])
    expect(editKind(day.entries[0])).toBe('purchase')
  })
})

describe('findEntry', () => {
  const days = buildTimeline([
    { ...base, id: 'a', purchaseDate: '2026-03-12' },
    { ...base, id: 'b', statementId: 's1', installmentPurchaseId: 'p', installmentNumber: 1 },
    { ...base, id: 'c', statementId: 's2', installmentPurchaseId: 'p', installmentNumber: 2 },
  ])

  it('acha o lançamento simples pelo id e a compra pelo id da compra', () => {
    expect(findEntry(days, 'a')).toMatchObject({ kind: 'single' })
    expect(findEntry(days, 'p')).toMatchObject({ kind: 'purchase', totalCents: 2000 })
  })

  it('devolve undefined quando o lançamento saiu da lista (excluído ou mudou de mês)', () => {
    expect(findEntry(days, 'x')).toBeUndefined()
  })

  it('entryKey e findEntry são inversos', () => {
    for (const entry of days.flatMap((d) => d.entries)) expect(findEntry(days, entryKey(entry))).toBe(entry)
  })
})
