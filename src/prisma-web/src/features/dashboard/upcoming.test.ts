import { describe, expect, it } from 'vitest'
import { balanceInAccounts, describeDue } from './upcoming'

type Kind = 'Checking' | 'CreditCard' | 'Cash' | 'Investment'
const account = (id: string, type: Kind, isActive = true) => ({ id, type, isActive })
const balance = (accountId: string, balanceCents: number | null) => ({ accountId, balanceCents })

// docs/fase-2.md, 2.4: soma dos saldos atuais das contas ativas que não são cartão.
describe('balanceInAccounts', () => {
  it('adds up active accounts and leaves out cards and inactive accounts', () => {
    const accounts = [
      account('itau', 'Checking'),
      account('carteira', 'Cash'),
      account('tesouro', 'Investment'),
      account('visa', 'CreditCard'),
      account('antiga', 'Checking', false),
    ]
    const balances = new Map(
      [
        balance('itau', 365410),
        balance('carteira', 25000),
        balance('tesouro', -1000),
        balance('visa', null),
        balance('antiga', 99900),
      ].map((b) => [b.accountId, b]),
    )

    expect(balanceInAccounts(accounts, balances)).toEqual({ cents: 389410, count: 3 })
  })

  it('is null without accounts to add up', () => {
    expect(balanceInAccounts([account('visa', 'CreditCard')], new Map())).toBeNull()
  })

  it('counts an account without balance yet as zero', () => {
    expect(balanceInAccounts([account('itau', 'Checking')], new Map())).toEqual({ cents: 0, count: 1 })
  })
})

describe('describeDue', () => {
  it('says today and tomorrow, otherwise the date', () => {
    expect(describeDue('2026-10-15', '2026-10-15')).toBe('Vence hoje')
    expect(describeDue('2026-10-16', '2026-10-15')).toBe('Vence amanhã')
    expect(describeDue('2026-11-05', '2026-10-15')).toBe('Vence 05/11')
  })

  it('shows the year when it is not this year', () => {
    expect(describeDue('2027-01-05', '2026-12-20')).toBe('Vence 05/01/2027')
  })
})
