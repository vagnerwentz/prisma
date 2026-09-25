import { describe, expect, it } from 'vitest'
import { barWidths, inheritedShare, lastInstallmentText, shortMonth } from './commitments'

// Etapa 2.8 (docs/fase-2.md, 2.6), com os números do exemplo.
describe('inheritedShare', () => {
  it('é a parte herdada do "Saiu", em % inteiro', () => {
    expect(inheritedShare(110000, 45000)).toBe(71)
    expect(inheritedShare(110000, 0)).toBe(100)
  })

  it('não existe quando os estornos deixam o "Saiu" abaixo do herdado', () => {
    expect(inheritedShare(100000, null)).toBeNull()
  })
})

describe('barWidths', () => {
  it('é relativa ao maior mês, e um mês pequeno não vira um fio', () => {
    expect(barWidths([133000, 120000, 100000, 100000, 40000, 40000])).toEqual([100, 90, 75, 75, 30, 30])
    expect(barWidths([133000, 0, 1000])).toEqual([100, 0, 3])
  })

  it('sem nada positivo, nenhuma barra', () => {
    expect(barWidths([0, -5000])).toEqual([0, 0])
  })
})

describe('textos', () => {
  it('mês curto com o ano, porque os seis meses atravessam a virada', () => {
    expect(shortMonth('2026-11')).toBe('nov/26')
    expect(shortMonth('2027-03')).toBe('mar/27')
  })

  it('última parcela', () => {
    expect(lastInstallmentText('2027-04')).toBe('A última parcela vence em abril de 2027')
  })
})
