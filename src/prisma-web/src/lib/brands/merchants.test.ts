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
    ['Itaú', 'Itaú', 'image'],
    ['itau personnalite', 'Itaú', 'image'], // sem acento e em minúsculas
    ['Íon Itaú', 'Íon', 'image'], // a corretora do Itaú vem antes do banco
    ['Netflix (cartão Itaú)', 'Netflix', 'logo'], // estabelecimento antes do banco
    ['Banco do Brasil', 'Banco do Brasil', 'image'],
    ['BB', 'Banco do Brasil', 'image'],
    ['Caixa Econômica Federal', 'Caixa', 'image'],
    ['BTG Pactual', 'BTG Pactual', 'image'],
    ['XP Investimentos', 'XP', 'image'],
    ['TED Rico Investimentos', 'Rico', 'image'],
    ['Rico', 'Rico', 'image'], // conta chamada só assim
    ['Ágora', 'Ágora', 'image'],
    ['Clear', 'Clear', 'image'],
    ['Modal Mais', 'Modalmais', 'image'],
    ['Inter', 'Inter', 'monogram'],
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

  // Nome de corretora que também é palavra comum: só a conta chamada assim, nunca no meio da frase.
  it.each(['Pagamento agora', 'Pastel do Rico', 'Clear skin', 'Safra de café', 'Loja Ion Store'])(
    '"%s" não é corretora (palavra comum só vale como nome inteiro)',
    (text) => {
      expect(findBrand(text)).toBeNull()
    },
  )

  it('ícone de instituição aponta para o arquivo SVG', () => {
    const itau = findBrand('Itaú')
    expect(itau?.kind).toBe('image')
    if (itau?.kind !== 'image') return
    expect(itau.src).toMatch(/itau.*\.svg|^data:image\/svg\+xml/)
  })

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
