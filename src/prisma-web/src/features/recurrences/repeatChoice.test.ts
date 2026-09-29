import { describe, expect, it } from 'vitest'
import { canRepeat, noRepeat, repeatRequest, repeatToSend } from './repeatChoice'

describe('repeatRequest', () => {
  it('sends nothing when it does not repeat', () => expect(repeatRequest(noRepeat)).toBeNull())

  it('sends the frequency and the end', () =>
    expect(repeatRequest({ frequency: 'Monthly', endDate: '2026-11-30' })).toEqual({
      frequency: 'Monthly',
      endDate: '2026-11-30',
    }))

  it('asks for the end date chosen but not filled', () =>
    expect(repeatRequest({ frequency: 'Weekly', endDate: '' })).toBe('missing-end'))
})

// O que o Novo lançamento manda: a escolha fica guardada, mas só vai enquanto a linha "se repete" aparece
// (não em estorno nem em compra parcelada; docs/fase-2.md, 2.14, Tela).
describe('repeatToSend', () => {
  it('knows when the row shows', () => {
    expect(canRepeat({ type: 'Expense', isCard: true, installments: 1 })).toBe(true)
    expect(canRepeat({ type: 'Expense', isCard: true, installments: 2 })).toBe(false)
    expect(canRepeat({ type: 'Refund', isCard: false, installments: 1 })).toBe(false)
  })

  const monthly = { frequency: 'Monthly' as const, endDate: null }

  it('sends the series of a purchase paid at once', () =>
    expect(repeatToSend({ type: 'Expense', isCard: true, installments: 1 }, monthly)).toEqual({
      frequency: 'Monthly',
      endDate: null,
    }))

  it('drops the series when the purchase becomes installments', () =>
    expect(repeatToSend({ type: 'Expense', isCard: true, installments: 3 }, monthly)).toBeNull())

  it('drops the series of a refund', () =>
    expect(repeatToSend({ type: 'Refund', isCard: false, installments: 1 }, monthly)).toBeNull())

  it('ignores installments off the card', () =>
    expect(repeatToSend({ type: 'Income', isCard: false, installments: 3 }, monthly)).toEqual({
      frequency: 'Monthly',
      endDate: null,
    }))

  it('still asks for the end date', () =>
    expect(repeatToSend({ type: 'Expense', isCard: false, installments: 1 }, { frequency: 'Weekly', endDate: '' })).toBe(
      'missing-end',
    ))
})
