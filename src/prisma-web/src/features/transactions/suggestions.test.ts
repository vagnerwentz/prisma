import { describe, expect, it } from 'vitest'
import { fold, frecency, recentSuggestions, suggest, suggestionFill, type DescriptionSuggestion } from './suggestions'

// Etapa 2.15 (docs/fase-2.md, 2.8), com o vocabulário do exemplo. Hoje é 25/09/2026.
const today = '2026-09-25'
const item = (description: string, overrides: Partial<DescriptionSuggestion> = {}): DescriptionSuggestion => ({
  description,
  type: 'Expense',
  categoryId: 'food',
  accountId: 'nubank',
  method: 'Credit',
  lastAmountCents: 1000,
  count: 1,
  lastUsedOn: '2026-09-20',
  ...overrides,
})

const vocabulary: DescriptionSuggestion[] = [
  item('iFood', { lastAmountCents: 4290, count: 2, lastUsedOn: '2026-09-20' }),
  item('Farmácia São João', {
    categoryId: 'health',
    accountId: 'itau',
    method: 'Pix',
    lastAmountCents: 6480,
    count: 2,
    lastUsedOn: '2026-09-05',
  }),
  item('Salário', {
    type: 'Income',
    categoryId: 'salary',
    accountId: 'itau',
    method: 'Pix',
    lastAmountCents: 820000,
    lastUsedOn: '2026-09-05',
  }),
  item('Notebook', { categoryId: 'shopping', lastAmountCents: 600000, lastUsedOn: '2026-03-15' }),
  item('Mercado Pão de Açúcar', { accountId: 'itau', method: 'Debit', count: 6, lastUsedOn: '2026-09-18' }),
]

const titles = (query: string, type: 'Expense' | 'Income' = 'Expense') =>
  suggest(vocabulary, query, type, today).map((m) => m.suggestion.description)

describe('fold', () => {
  it('tira acento e maiúscula sem mudar o tamanho, para o destaque cair no lugar certo', () => {
    expect(fold('Farmácia São João')).toBe('farmacia sao joao')
    expect(fold('Farmácia São João')).toHaveLength('Farmácia São João'.length)
  })
})

describe('suggest', () => {
  it('o exemplo da especificação', () => {
    expect(titles('if')).toEqual(['iFood'])
    expect(titles('sao')).toEqual(['Farmácia São João'])
    expect(titles('note')).toEqual(['Notebook'])
    // Os dois só têm o trecho; o Mercado, mais usado, vem antes.
    expect(titles('ar')).toEqual(['Mercado Pão de Açúcar', 'Farmácia São João'])
    expect(titles('sa', 'Income')).toEqual(['Salário'])
  })

  it('início da descrição, depois início de palavra, depois trecho', () => {
    const words = [item('Padaria'), item('Pão da Padaria'), item('Empada')]
    expect(suggest(words, 'pad', 'Expense', today).map((m) => m.suggestion.description)).toEqual([
      'Padaria',
      'Pão da Padaria',
      'Empada',
    ])
  })

  it('trecho no meio só a partir de 2 letras', () => {
    expect(titles('o')).toEqual([])
    expect(titles('ç')).toEqual([])
  })

  it('marca o trecho encontrado na grafia original', () => {
    const [match] = suggest(vocabulary, 'acu', 'Expense', today)
    expect(match.suggestion.description.slice(match.start, match.start + match.length)).toBe('Açú')
  })

  it('no máximo 5, com desempate pelo uso', () => {
    const many = ['Loja A', 'Loja B', 'Loja C', 'Loja D', 'Loja E', 'Loja F'].map((d, i) => item(d, { count: i + 1 }))
    expect(suggest(many, 'loja', 'Expense', today).map((m) => m.suggestion.description)).toEqual([
      'Loja F',
      'Loja E',
      'Loja D',
      'Loja C',
      'Loja B',
    ])
  })

  it('estorno e transferência não sugerem', () => {
    expect(suggest(vocabulary, 'if', 'Refund', today)).toEqual([])
  })
})

describe('frecency', () => {
  it('pesa o uso pela recência: até 30 dias, 2; até 90, 1; mais, 0,5', () => {
    expect(frecency(item('a', { count: 3, lastUsedOn: '2026-08-26' }), today)).toBe(6)
    expect(frecency(item('a', { count: 3, lastUsedOn: '2026-06-27' }), today)).toBe(3)
    expect(frecency(item('a', { count: 3, lastUsedOn: '2026-06-26' }), today)).toBe(1.5)
  })
})

describe('recentSuggestions', () => {
  it('as 6 de maior uso do tipo, com o campo vazio', () => {
    expect(recentSuggestions(vocabulary, 'Expense', today).map((s) => s.description)).toEqual([
      'Mercado Pão de Açúcar',
      'iFood',
      'Farmácia São João',
      'Notebook',
    ])
    expect(recentSuggestions(vocabulary, 'Income', today).map((s) => s.description)).toEqual(['Salário'])
  })
})

describe('suggestionFill', () => {
  const accounts = [
    { id: 'itau', type: 'Checking' as const },
    { id: 'nubank', type: 'CreditCard' as const },
  ]
  const exists = (id: string) => ['food', 'health', 'shopping'].includes(id)
  const farmacia = vocabulary[1]

  it('preenche descrição, categoria, conta e meio quando nada foi tocado', () => {
    expect(suggestionFill(farmacia, { accountTouched: false, categoryTouched: false }, accounts, exists)).toEqual({
      description: 'Farmácia São João',
      categoryId: 'health',
      accountId: 'itau',
      method: 'Pix',
      keptAccount: false,
      keptCategory: false,
    })
  })

  it('nunca desfaz conta nem categoria que a pessoa escolheu', () => {
    expect(suggestionFill(farmacia, { accountTouched: true, categoryTouched: true }, accounts, exists)).toEqual({
      description: 'Farmácia São João',
      keptAccount: true,
      keptCategory: true,
    })
  })

  it('ignora conta e categoria que não existem mais', () => {
    const gone = { ...farmacia, accountId: 'excluida', categoryId: 'apagada' }
    expect(suggestionFill(gone, { accountTouched: false, categoryTouched: false }, accounts, exists)).toEqual({
      description: 'Farmácia São João',
      keptAccount: false,
      keptCategory: false,
    })
  })

  it('no cartão o meio é sempre crédito', () => {
    const odd = { ...farmacia, accountId: 'nubank', method: 'Pix' as const }
    expect(suggestionFill(odd, { accountTouched: false, categoryTouched: false }, accounts, exists).method).toBe('Credit')
  })
})
