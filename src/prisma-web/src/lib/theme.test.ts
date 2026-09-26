import { describe, expect, it } from 'vitest'
import { parseChoice, resolveTheme, revealRadius, toggledChoice } from './theme'

describe('parseChoice', () => {
  it('aceita só claro e escuro; o resto é seguir o sistema', () => {
    expect(parseChoice('light')).toBe('light')
    expect(parseChoice('dark')).toBe('dark')
    expect(parseChoice(null)).toBe('system')
    expect(parseChoice('azul')).toBe('system')
  })
})

describe('resolveTheme', () => {
  it('a escolha manual vence o sistema', () => {
    expect(resolveTheme('light', true)).toBe('light')
    expect(resolveTheme('dark', false)).toBe('dark')
  })

  it('sem escolha, segue o sistema', () => {
    expect(resolveTheme('system', true)).toBe('dark')
    expect(resolveTheme('system', false)).toBe('light')
  })
})

describe('toggledChoice', () => {
  it('o botão inverte o tema que está na tela', () => {
    expect(toggledChoice('dark')).toBe('light')
    expect(toggledChoice('light')).toBe('dark')
  })
})

describe('revealRadius', () => {
  it('alcança o canto mais distante do ponto de origem', () => {
    // Botão no canto superior direito de uma tela 390 × 844: o canto mais longe é o inferior esquerdo.
    expect(revealRadius(350, 28, 390, 844)).toBeCloseTo(Math.hypot(350, 816))
    expect(revealRadius(0, 0, 300, 400)).toBe(500)
  })
})
