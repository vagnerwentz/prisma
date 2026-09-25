import { describe, expect, it } from 'vitest'
import { withMonth } from './monthParam'

describe('withMonth', () => {
  it('leva o mês escolhido para a outra tela', () => {
    expect(withMonth('/analise', '2026-09')).toBe('/analise?mes=2026-09')
    expect(withMonth('/', '2026-09')).toBe('/?mes=2026-09')
  })

  it('sem mês, ou com mês inválido, abre no mês de hoje', () => {
    expect(withMonth('/analise', null)).toBe('/analise')
    expect(withMonth('/analise', '2026-13')).toBe('/analise')
    expect(withMonth('/analise', 'setembro')).toBe('/analise')
  })
})
