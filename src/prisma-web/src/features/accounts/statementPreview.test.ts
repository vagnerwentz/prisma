import { describe, expect, it } from 'vitest'
import { previewTexts, shouldPreview } from './statementPreview'

// Prévia do ajuste de datas (docs/fase-2.md, 2.10): a lista das compras que mudariam de fatura, antes
// de salvar, e o botão dizendo o que vai acontecer.

// O formatCents usa espaço não separável depois do "R$".
const R$ = 'R$\u00a0'

const original = { closingDate: '2026-10-26', dueDate: '2026-11-05' }

describe('shouldPreview', () => {
  it('pede a prévia quando as datas mudaram e fazem sentido', () => {
    expect(shouldPreview(original, '2026-10-27', '2026-11-05')).toBe(true)
    expect(shouldPreview(original, '2026-10-26', '2026-11-04')).toBe(true)
  })

  it('não pede com as datas de sempre, vazias ou com vencimento antes do fechamento', () => {
    expect(shouldPreview(original, '2026-10-26', '2026-11-05')).toBe(false)
    expect(shouldPreview(original, '', '2026-11-05')).toBe(false)
    expect(shouldPreview(original, '2026-10-26', '')).toBe(false)
    expect(shouldPreview(original, '2026-11-06', '2026-11-05')).toBe(false)
  })
})

describe('previewTexts', () => {
  it('título da lista pela quantidade', () => {
    expect(previewTexts.heading(0)).toBe('Nenhuma compra muda de fatura.')
    expect(previewTexts.heading(1)).toBe('1 compra muda de fatura')
    expect(previewTexts.heading(3)).toBe('3 compras mudam de fatura')
  })

  it('cada compra diz a data e de qual fatura para qual', () => {
    expect(previewTexts.route({ purchaseDate: '2026-10-27', fromReference: '2026-12', toReference: '2026-11' })).toBe(
      '27/10 · dezembro → novembro',
    )
  })

  it('parcelada mostra o número de parcelas junto do valor', () => {
    expect(previewTexts.amount({ type: 'Expense', amountCents: 9000, installmentCount: 3 })).toBe(`${R$}90,00 em 3x`)
    expect(previewTexts.amount({ type: 'Expense', amountCents: 4590, installmentCount: 1 })).toBe(`${R$}45,90`)
    expect(previewTexts.amount({ type: 'Refund', amountCents: 1000, installmentCount: 1 })).toBe(`estorno ${R$}10,00`)
  })

  it('botão diz o que vai acontecer', () => {
    expect(previewTexts.save(undefined)).toBe('Salvar datas')
    expect(previewTexts.save(0)).toBe('Salvar datas')
    expect(previewTexts.save(1)).toBe('Salvar e mover 1 compra')
    expect(previewTexts.save(2)).toBe('Salvar e mover 2 compras')
  })
})
