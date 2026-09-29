import { describe, expect, it } from 'vitest'
import { barWidths, committedBars, inheritedShare, lastInstallmentText, projectedText, shortMonth } from './commitments'

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

// docs/fase-2.md, 2.14, regra 11: a parte prevista das séries, mais clara, depois do que já existe.
describe('committedBars', () => {
  it('scales both parts by the largest month, existing plus projected', () => {
    // Hoje 20/10: novembro com a farmácia (R$ 50) e R$ 1.400 previstos; dezembro só com os previstos.
    expect(
      committedBars([
        { expenseCents: 5000, projectedExpenseCents: 140000 },
        { expenseCents: 0, projectedExpenseCents: 140000 },
        { expenseCents: 0, projectedExpenseCents: 0 },
      ]),
    ).toEqual([
      { solid: 3, projected: 97 },
      { solid: 0, projected: 97 },
      { solid: 0, projected: 0 },
    ])
  })

  it('keeps the bars of before when nothing repeats', () => {
    expect(
      committedBars([
        { expenseCents: 133000, projectedExpenseCents: 0 },
        { expenseCents: 1000, projectedExpenseCents: 0 },
      ]),
    ).toEqual([
      { solid: 100, projected: 0 },
      { solid: 3, projected: 0 },
    ])
  })
})

describe('projectedText', () => {
  it('says how much is expected', () => expect(projectedText(140000)).toBe('+ R$\u00a01.400,00 previstos'))
})
