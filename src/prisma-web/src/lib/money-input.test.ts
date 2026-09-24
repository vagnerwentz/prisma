import { describe, expect, it } from 'vitest'
import { describeInstallments, formatCents, parseCentsInput, splitCents } from './money'

const R$ = 'R$ '

// Campo de valor no estilo dos apps de banco: os dígitos entram pela direita, sem vírgula.
describe('parseCentsInput', () => {
  it.each([
    ['4590', 4590],
    ['R$ 45,90', 4590],
    ['R$ 45,9', 459], // apagar o último dígito de "R$ 45,90"
    ['R$ 1.234,56', 123456],
    ['0001', 1],
    ['', 0],
    ['abc', 0],
  ])('"%s" → %i centavos', (text, expected) => {
    expect(parseCentsInput(text)).toBe(expected)
  })

  it('limita a 13 dígitos (R$ 99 bilhões), sempre um inteiro seguro', () => {
    expect(parseCentsInput('12345678901234567890')).toBe(1234567890123)
  })

  it('digitar sobre o valor formatado acrescenta o dígito à direita', () => {
    expect(parseCentsInput(formatCents(4590) + '1')).toBe(45901)
  })
})

// Mesma regra de Money.SplitInto no backend (CLAUDE.md, regra 2): o resto vai para as primeiras
// parcelas e a soma é sempre o total. Exemplos de docs/fase-1.md, seção 4.
describe('splitCents', () => {
  it.each([
    [10000, 3, [3334, 3333, 3333]],
    [1, 2, [1, 0]],
    [0, 5, [0, 0, 0, 0, 0]],
    [12990, 1, [12990]],
  ])('%i em %ix → %o', (total, parts, expected) => {
    expect(splitCents(total, parts)).toEqual(expected)
  })

  it('a soma das partes é sempre o total', () => {
    for (let total = 0; total <= 5_000; total += 13) {
      for (let parts = 1; parts <= 24; parts++) {
        const split = splitCents(total, parts)
        expect(split).toHaveLength(parts)
        expect(split.reduce((a, b) => a + b, 0)).toBe(total)
        expect(Math.max(...split) - Math.min(...split)).toBeLessThanOrEqual(1)
      }
    }
  })

  it('recusa menos de uma parte', () => {
    expect(() => splitCents(100, 0)).toThrow()
  })
})

describe('describeInstallments', () => {
  it.each([
    [100000, 10, `10x de ${R$}100,00`],
    [100005, 10, `5x de ${R$}100,01 e 5x de ${R$}100,00`],
    [10000, 3, `1x de ${R$}33,34 e 2x de ${R$}33,33`],
    [4590, 1, `à vista`],
  ])('%i em %ix → %s', (total, parts, expected) => {
    expect(describeInstallments(total, parts)).toBe(expected)
  })
})
