import { describe, expect, it } from 'vitest'
import { formatCents } from './money'

// Espaço inseparável entre "R$" e o número, para não quebrar linha no meio do valor.
const R$ = 'R$ '

describe('formatCents', () => {
  it.each([
    [12990, `${R$}129,90`], // exemplo do CLAUDE.md: R$ 129,90 é 12990
    [0, `${R$}0,00`],
    [1, `${R$}0,01`],
    [10, `${R$}0,10`],
    [100000, `${R$}1.000,00`],
    [123456789, `${R$}1.234.567,89`],
    [-25000, `-${R$}250,00`],
    [-1, `-${R$}0,01`],
  ])('%i centavos → %s', (cents, expected) => {
    expect(formatCents(cents)).toBe(expected)
  })

  // Sem divisão por 100 em ponto flutuante: o maior inteiro seguro sai exato.
  it('formata o maior inteiro seguro sem perder centavo', () => {
    expect(formatCents(Number.MAX_SAFE_INTEGER)).toBe(`${R$}90.071.992.547.409,91`)
  })

  it('só os dígitos do texto formatado reconstroem o valor, para qualquer valor', () => {
    for (let cents = 0; cents <= 200_000; cents += 7) {
      expect(Number(formatCents(cents).replace(/\D/g, ''))).toBe(cents)
    }
  })

  it('recusa valor que não seja inteiro de centavos', () => {
    expect(() => formatCents(12.5)).toThrow()
    expect(() => formatCents(Number.NaN)).toThrow()
  })
})
