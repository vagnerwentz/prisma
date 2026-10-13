import { describe, expect, it } from 'vitest'
import { allocateCents, averageCostCents, daysBetween, statementForPurchase } from './demoMath'

// As demonstrações da página usam as regras do app: os valores esperados saem do domínio (StatementCalculator,
// Money.Allocate, AssetPosition.AverageCostCents), não do código abaixo.
describe('statementForPurchase', () => {
  // Cartão que fecha dia 25 e vence dia 5: o vencimento cai no mês seguinte ao fechamento.
  it('puts the day before closing in the closing statement', () => {
    expect(statementForPurchase('2026-10-24', 25, 5)).toEqual({ closingDate: '2026-10-25', dueDate: '2026-11-05' })
  })

  it('still puts the closing day in the closing statement', () => {
    expect(statementForPurchase('2026-10-25', 25, 5)).toEqual({ closingDate: '2026-10-25', dueDate: '2026-11-05' })
  })

  it('moves the day after closing to the next statement', () => {
    expect(statementForPurchase('2026-10-26', 25, 5)).toEqual({ closingDate: '2026-11-25', dueDate: '2026-12-05' })
  })

  it('keeps the due date in the closing month when it comes after closing', () => {
    expect(statementForPurchase('2026-10-02', 3, 10)).toEqual({ closingDate: '2026-10-03', dueDate: '2026-10-10' })
    expect(statementForPurchase('2026-10-04', 3, 10)).toEqual({ closingDate: '2026-11-03', dueDate: '2026-11-10' })
  })

  it('moves the due date to the next month when it is the closing day itself', () => {
    expect(statementForPurchase('2026-10-08', 10, 10)).toEqual({ closingDate: '2026-10-10', dueDate: '2026-11-10' })
  })

  // Cartão que fecha dia 31: em mês de 30 dias e em fevereiro, fecha no último dia.
  it('closes on the last day of a short month', () => {
    expect(statementForPurchase('2026-09-30', 31, 10)).toEqual({ closingDate: '2026-09-30', dueDate: '2026-10-10' })
    expect(statementForPurchase('2027-02-28', 31, 10)).toEqual({ closingDate: '2027-02-28', dueDate: '2027-03-10' })
    expect(statementForPurchase('2027-03-01', 31, 10)).toEqual({ closingDate: '2027-03-31', dueDate: '2027-04-10' })
  })

  it('crosses the year', () => {
    expect(statementForPurchase('2026-12-26', 25, 5)).toEqual({ closingDate: '2027-01-25', dueDate: '2027-02-05' })
  })
})

describe('daysBetween', () => {
  it('counts calendar days', () => {
    expect(daysBetween('2026-10-26', '2026-12-05')).toBe(40)
    expect(daysBetween('2026-10-24', '2026-11-05')).toBe(12)
    expect(daysBetween('2027-02-28', '2027-03-01')).toBe(1)
  })
})

describe('allocateCents', () => {
  it('splits by weight and gives the leftover cents to the largest remainders', () => {
    expect(allocateCents(100, [1, 1, 1])).toEqual([34, 33, 33])
    expect(allocateCents(96, [189960, 196800, 176500, 193600])).toEqual([24, 25, 22, 25])
  })

  it('always adds up to the total', () => {
    let seed = 7
    const random = (max: number) => {
      seed = (seed * 1103515245 + 12345) % 2147483648
      return seed % max
    }
    for (let i = 0; i < 2000; i++) {
      const total = random(100000)
      const weights = Array.from({ length: 1 + random(8) }, () => random(1000000))
      if (!weights.some((w) => w > 0)) weights[0] = 1
      const parts = allocateCents(total, weights)
      expect(parts.reduce((a, b) => a + b, 0)).toBe(total)
      expect(parts.every((p) => p >= 0)).toBe(true)
    }
  })
})

describe('averageCostCents', () => {
  it('rounds half up', () => {
    expect(averageCostCents(189984, 12)).toBe(15832) // 15832,0
    expect(averageCostCents(25, 2)).toBe(13) // 12,5 → 13
    expect(averageCostCents(24, 5)).toBe(5) // 4,8 → 5
    expect(averageCostCents(22, 5)).toBe(4) // 4,4 → 4
  })
})
