import { describe, expect, it } from 'vitest'
import { findBrand, readableTextOn } from './merchants'

describe('findBrand', () => {
  it.each([
    ['Uber', 'Uber', 'logo'],
    ['UBER *TRIP', 'Uber', 'logo'],
    ['Uber Eats', 'Uber Eats', 'logo'], // mais específico vence
    ['iFood', 'iFood', 'logo'],
    ['IFD*IFOOD', 'iFood', 'logo'],
    ['Netflix.com', 'Netflix', 'logo'],
    ['Spotify Premium', 'Spotify', 'logo'],
    ['Nubank', 'Nubank', 'logo'],
    ['Apple Music', 'Apple Music', 'logo'],
    ['Amazon Prime', 'Prime Video', 'monogram'],
    ['Amazon', 'Amazon', 'monogram'],
    ['Itaú', 'Itaú', 'monogram'],
    ['itau personnalite', 'Itaú', 'monogram'], // sem acento e em minúsculas
    ['Mercado Livre', 'Mercado Livre', 'monogram'],
    ['Pão de Açúcar', 'Pão de Açúcar', 'monogram'],
    ['Smart Fit', 'Smart Fit', 'monogram'],
  ])('"%s" → %s (%s)', (text, name, kind) => {
    const brand = findBrand(text)
    expect(brand?.name).toBe(name)
    expect(brand?.kind).toBe(kind)
  })

  it.each(['Padaria', 'Aluguel', 'Superuberização', 'Maxi mercado', '', null, undefined])(
    '"%s" não é marca (palavra inteira, sem falso positivo)',
    (text) => {
      expect(findBrand(text)).toBeNull()
    },
  )

  it('logo traz o desenho SVG e a cor da marca', () => {
    const uber = findBrand('Uber')
    expect(uber?.kind).toBe('logo')
    if (uber?.kind !== 'logo') return
    expect(uber.path.length).toBeGreaterThan(20)
    expect(uber.color).toMatch(/^#[0-9A-F]{6}$/i)
  })
})

describe('readableTextOn', () => {
  it.each([
    ['#000000', 'light'],
    ['#EC7000', 'light'], // laranja Itaú
    ['#FFE600', 'dark'], // amarelo Mercado Livre
    ['#FFFFFF', 'dark'],
  ])('%s → texto %s', (hex, expected) => {
    expect(readableTextOn(hex)).toBe(expected)
  })
})
