import { describe, expect, it } from 'vitest'
import { hiddenRefundsNotice, shareOf } from './categories'

// docs/fase-2.md, 2.2: participação sobre o total de despesas do mês, arredondada para inteiro.
describe('shareOf', () => {
  it('rounds the example of October (R$ 4.000,00 in expenses)', () => {
    expect(shareOf(250000, 400000)).toBe(63)
    expect(shareOf(140000, 400000)).toBe(35)
    expect(shareOf(10000, 400000)).toBe(3)
  })

  it('is 100 for the only category and 0 without expenses', () => {
    expect(shareOf(30000, 30000)).toBe(100)
    expect(shareOf(0, 0)).toBe(0)
  })

  it('never shows 0% for a category that has some expense', () => {
    expect(shareOf(1, 400000)).toBe(1)
  })
})

// docs/fase-2.md, 2.5, regra 12: categoria que o estorno deixou zerada ou negativa some da lista, e
// a lista avisa quanto de estorno sumiu com ela. A participação é sobre as categorias exibidas.
describe('hiddenRefundsNotice', () => {
  it('tells how much refund went with the hidden categories', () => {
    expect(hiddenRefundsNotice(15000)).toBe('R$\u00a0150,00 de estornos em categorias sem gastos no mês')
  })

  it('says nothing when nothing was hidden', () => {
    expect(hiddenRefundsNotice(0)).toBeNull()
  })
})

describe('shareOf over the shown categories', () => {
  it('is 100 for the only category left in November of the example', () => {
    expect(shareOf(16000, 16000)).toBe(100)
  })
})
