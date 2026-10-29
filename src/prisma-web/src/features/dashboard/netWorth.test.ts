import { describe, expect, it } from 'vitest'
import { freeCashOf, hasInvestments, investedNote, tracksAnyCash, type NetWorth } from './netWorth'

const net = (over: Partial<NetWorth> = {}): NetWorth => ({
  availableCents: 425_030,
  availableCount: 1,
  investedCents: 0,
  yieldCents: 0,
  brokerCashCents: 0,
  totalCents: 425_030,
  brokers: [],
  ...over,
})

// docs/patrimonio.md, I.9: o Investido vem dos ativos; o caixa das corretoras só aparece para quem o acompanha.
describe('hasInvestments', () => {
  it('is false without fixed income, stocks or investment accounts: the summary keeps the plain balance', () => {
    expect(hasInvestments(net())).toBe(false)
  })

  it('is true when there is anything invested, even with no investment account', () => {
    expect(hasInvestments(net({ investedCents: 1_000_000 }))).toBe(true)
  })

  it('is true when there is an investment account, even before anything is invested', () => {
    expect(hasInvestments(net({ brokers: [{ accountId: 'ion', tracksCash: false, freeCashCents: null }] }))).toBe(true)
  })
})

describe('tracksAnyCash', () => {
  it('shows the broker cash line only when some account tracks its cash', () => {
    expect(tracksAnyCash(net({ brokers: [{ accountId: 'ion', tracksCash: false, freeCashCents: null }] }))).toBe(false)
    expect(tracksAnyCash(net({ brokers: [{ accountId: 'ion', tracksCash: true, freeCashCents: 250_000 }] }))).toBe(true)
  })
})

describe('investedNote', () => {
  it('says where the Invested comes from, and counts the yield apart', () => {
    expect(investedNote({ yieldCents: 0 })).toBe('Renda fixa e ações')
    expect(investedNote({ yieldCents: 40_700 })).toBe('Inclui R$ 407,00 de rendimento')
  })
})

describe('freeCashOf', () => {
  const brokers = [
    { accountId: 'ion', tracksCash: true, freeCashCents: 250_000 },
    { accountId: 'xp', tracksCash: false, freeCashCents: null },
  ]

  it('gives the free cash of an account that tracks it, negative included', () => {
    expect(freeCashOf(net({ brokers }), 'ion')).toBe(250_000)
    expect(freeCashOf(net({ brokers: [{ accountId: 'ion', tracksCash: true, freeCashCents: -553_059 }] }), 'ion')).toBe(-553_059)
  })

  it('is null for an account that does not track it, and for one that is not an investment account', () => {
    expect(freeCashOf(net({ brokers }), 'xp')).toBeNull()
    expect(freeCashOf(net({ brokers }), 'itau')).toBeNull()
  })

  // A tela não depende de a API calar: conta que não acompanha o caixa nunca mostra caixa livre.
  it('ignores a value that comes for an account that does not track its cash', () => {
    expect(freeCashOf(net({ brokers: [{ accountId: 'xp', tracksCash: false, freeCashCents: 999 }] }), 'xp')).toBeNull()
  })
})
