import { describe, expect, it } from 'vitest'
import { describeExpenseChange, expenseChange } from './history'

// docs/fase-2.md, 2.3: variação das despesas contra o mês anterior; sem despesa no anterior, nada.
describe('expenseChange', () => {
  it('compares with the previous month, rounded to an integer percent', () => {
    expect(expenseChange(400000, 300000)).toEqual({ percent: 33, direction: 'more' })
    expect(expenseChange(300000, 400000)).toEqual({ percent: 25, direction: 'less' })
    expect(expenseChange(0, 30000)).toEqual({ percent: 100, direction: 'less' })
  })

  // Mês só com estornos tem despesa negativa (docs/fase-2.md, 2.5): no máximo 100% a menos.
  it('never goes beyond 100% less when refunds make the month negative', () => {
    expect(expenseChange(-9000, 80000)).toEqual({ percent: 100, direction: 'less' })
  })

  it('is the same when the rounded change is zero', () => {
    expect(expenseChange(30000, 30000)).toEqual({ percent: 0, direction: 'same' })
    expect(expenseChange(100001, 100000)).toEqual({ percent: 0, direction: 'same' })
  })

  it('has no percentage when the previous month had no expense', () => {
    expect(expenseChange(30000, 0)).toBeNull()
    expect(expenseChange(0, 0)).toBeNull()
  })
})

describe('describeExpenseChange', () => {
  it('writes the sentence in pt-BR', () => {
    expect(describeExpenseChange({ percent: 33, direction: 'more' }, 'setembro')).toBe('Gastou 33% a mais que em setembro')
    expect(describeExpenseChange({ percent: 25, direction: 'less' }, 'setembro')).toBe('Gastou 25% a menos que em setembro')
    expect(describeExpenseChange({ percent: 0, direction: 'same' }, 'setembro')).toBe('Gastou o mesmo que em setembro')
  })
})
