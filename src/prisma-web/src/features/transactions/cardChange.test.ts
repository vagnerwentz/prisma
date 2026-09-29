import { describe, expect, it } from 'vitest'
import { cardChangeText, cardChoices } from './cardChange'

// docs/fase-2.md, 2.13: trocar o cartão de uma compra.
describe('cardChoices', () => {
  const accounts = [
    { id: 'itau', type: 'Checking' as const, isActive: true },
    { id: 'visa', type: 'CreditCard' as const, isActive: true },
    { id: 'master', type: 'CreditCard' as const, isActive: true },
    { id: 'antigo', type: 'CreditCard' as const, isActive: false },
    { id: 'tesouro', type: 'Investment' as const, isActive: true },
  ]

  it('oferece só os cartões ativos, na ordem das contas', () => {
    expect(cardChoices(accounts, 'visa').map((a) => a.id)).toEqual(['visa', 'master'])
  })

  it('mantém o cartão atual da compra mesmo inativo', () => {
    expect(cardChoices(accounts, 'antigo').map((a) => a.id)).toEqual(['visa', 'master', 'antigo'])
  })

  it('nunca oferece conta que não é cartão', () => {
    expect(cardChoices(accounts, 'itau').map((a) => a.id)).toEqual(['visa', 'master'])
  })
})

describe('cardChangeText', () => {
  // A fatura é a do mês em que vence, como no "fatura de <mês>" da lista.
  it('diz o cartão novo e o mês da fatura', () => {
    expect(cardChangeText('Master', '2026-11-12')).toBe('Agora no Master, na fatura de novembro.')
  })

  it('vale na virada do ano', () => {
    expect(cardChangeText('Nubank', '2027-01-05')).toBe('Agora no Nubank, na fatura de janeiro.')
  })
})
