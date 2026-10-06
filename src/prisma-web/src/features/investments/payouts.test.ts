import { describe, expect, it } from 'vitest'
import { defaultPayoutAccount, groupByDay, suggestedKind, totalsByAsset, yearTotal, type Payout } from './payouts'

const payout = (over: Partial<Payout>): Payout => ({
  id: crypto.randomUUID(),
  accountId: 'ion',
  assetId: 'bbas',
  symbol: 'BBAS3',
  assetName: 'BCO BRASIL S.A.',
  assetKind: 'Stock',
  kind: 'Dividend',
  amountCents: 1000,
  date: '2026-09-30',
  assetHasLogo: false,
  ...over,
})

describe('yearTotal', () => {
  it('soma só o ano pedido, pela data do pagamento', () => {
    const payouts = [
      payout({ amountCents: 4120, date: '2026-09-30' }),
      payout({ amountCents: 980, date: '2026-01-02' }),
      payout({ amountCents: 777, date: '2025-12-31' }),
    ]
    expect(yearTotal(payouts, 2026)).toBe(5100)
    expect(yearTotal(payouts, 2025)).toBe(777)
    expect(yearTotal([], 2026)).toBe(0)
  })
})

describe('totalsByAsset', () => {
  it('soma tudo o que cada ativo pagou', () => {
    const totals = totalsByAsset([
      payout({ assetId: 'bbas', amountCents: 4120 }),
      payout({ assetId: 'mxrf', amountCents: 980 }),
      payout({ assetId: 'bbas', amountCents: 3579, date: '2025-06-30' }),
    ])
    expect(totals.get('bbas')).toBe(7699)
    expect(totals.get('mxrf')).toBe(980)
    expect(totals.get('itsa')).toBeUndefined()
  })
})

describe('groupByDay', () => {
  it('junta os proventos do mesmo dia, mantendo a ordem', () => {
    const days = groupByDay([
      payout({ symbol: 'BBAS3', amountCents: 4120, date: '2026-09-30' }),
      payout({ symbol: 'ITSA4', amountCents: 1500, date: '2026-09-30' }),
      payout({ symbol: 'MXRF11', amountCents: 980, date: '2026-09-15' }),
    ])
    expect(days.map((d) => [d.date, d.items.map((i) => i.symbol), d.totalCents])).toEqual([
      ['2026-09-30', ['BBAS3', 'ITSA4'], 5620],
      ['2026-09-15', ['MXRF11'], 980],
    ])
  })
})

describe('suggestedKind', () => {
  it('fundo paga rendimento; ação, unit e BDR, dividendo', () => {
    expect(suggestedKind('Fii')).toBe('FundIncome')
    expect(suggestedKind('Etf')).toBe('FundIncome')
    expect(suggestedKind('FiAgro')).toBe('FundIncome')
    expect(suggestedKind('OtherFund')).toBe('FundIncome')
    expect(suggestedKind('Stock')).toBe('Dividend')
    expect(suggestedKind('Unit')).toBe('Dividend')
    expect(suggestedKind('Bdr')).toBe('Dividend')
  })
})

describe('defaultPayoutAccount', () => {
  const itau = { id: 'itau', type: 'Checking' as const, isActive: true }
  const ion = { id: 'ion', type: 'Investment' as const, isActive: true }
  const visa = { id: 'visa', type: 'CreditCard' as const, isActive: true }
  const old = { id: 'old', type: 'Investment' as const, isActive: false }

  it('repete a conta do último provento', () => {
    expect(defaultPayoutAccount([ion, itau], [payout({ accountId: 'itau' })])?.id).toBe('itau')
  })

  it('sem provento, prefere conta de investimento', () => {
    expect(defaultPayoutAccount([visa, itau, ion], [])?.id).toBe('ion')
  })

  it('sem conta de investimento, a primeira que serve; nunca cartão nem conta inativa', () => {
    expect(defaultPayoutAccount([visa, old, itau], [])?.id).toBe('itau')
    expect(defaultPayoutAccount([visa, old], [payout({ accountId: 'old' })])).toBeUndefined()
  })
})
