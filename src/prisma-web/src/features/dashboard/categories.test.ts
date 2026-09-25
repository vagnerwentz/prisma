import { describe, expect, it } from 'vitest'
import { shareOf } from './categories'

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
