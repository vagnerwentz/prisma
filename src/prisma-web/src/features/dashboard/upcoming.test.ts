import { describe, expect, it } from 'vitest'
import { balanceInAccounts, describeDue, splitBalances } from './upcoming'

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

// docs/renda-fixa.md, D20: o "Em contas" separa o dinheiro livre (corrente e carteira) do investido (contas de investimento, com o
// rendimento bruto da renda fixa que entrou por elas). O saldo da conta continua sendo só o principal.
describe('splitBalances', () => {
  const balances = (pairs: [string, number | null][]) => new Map(pairs.map(([accountId, balanceCents]) => [accountId, { accountId, balanceCents }]))

  it('splits what is free from what is invested, adding the yield only to the investment account it came through', () => {
    const accounts = [account('itau', 'Checking'), account('ion', 'Investment'), account('inter', 'Investment'), account('visa', 'CreditCard')]
    const split = splitBalances(
      accounts,
      balances([['itau', 723_015], ['ion', 1_000_000], ['inter', 50_000], ['visa', null]]),
      [{ accountId: 'ion', grossYieldCents: 12_345 }],
    )

    expect(split).toEqual({
      availableCents: 723_015,
      availableCount: 1,
      investedCents: 1_062_345,
      investedCount: 2,
      yieldCents: 12_345,
      totalCents: 1_785_360,
    })
  })

  it('has no invested part without an investment account', () => {
    const split = splitBalances([account('itau', 'Checking'), account('carteira', 'Cash')], balances([['itau', 100], ['carteira', 50]]), [])

    expect(split).toEqual({ availableCents: 150, availableCount: 2, investedCents: 0, investedCount: 0, yieldCents: 0, totalCents: 150 })
  })

  it('leaves out inactive accounts and the yield of an account that is not an active investment account', () => {
    const accounts = [account('itau', 'Checking'), account('ion', 'Investment', false), account('inter', 'Investment')]
    const split = splitBalances(
      accounts,
      balances([['itau', 100], ['ion', 900], ['inter', 200]]),
      [{ accountId: 'ion', grossYieldCents: 50 }, { accountId: 'itau', grossYieldCents: 70 }, { accountId: 'inter', grossYieldCents: 5 }],
    )

    expect(split).toMatchObject({ availableCents: 100, investedCents: 205, investedCount: 1, yieldCents: 5, totalCents: 305 })
  })

  it('keeps a negative free balance as it is and does not hide it in the total', () => {
    const split = splitBalances([account('itau', 'Checking'), account('ion', 'Investment')], balances([['itau', -300], ['ion', 1000]]), [])

    expect(split).toMatchObject({ availableCents: -300, investedCents: 1000, totalCents: 700 })
  })

  it('is null without accounts to add up', () => {
    expect(splitBalances([account('visa', 'CreditCard')], new Map(), [])).toBeNull()
  })

  it('treats only an investment account as invested: a free account with a yield entry gets none', () => {
    const split = splitBalances([account('itau', 'Checking')], balances([['itau', 10]]), [{ accountId: 'itau', grossYieldCents: 99 }])

    expect(split).toMatchObject({ availableCents: 10, investedCents: 0, yieldCents: 0 })
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
