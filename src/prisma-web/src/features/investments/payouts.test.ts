import { describe, expect, it } from 'vitest'
import {
  defaultPayoutAccount,
  filterPayouts,
  groupByDay,
  inYear,
  monthBars,
  monthlyAverage,
  payingGroups,
  rankByAsset,
  receivedSplit,
  shortReais,
  suggestedKind,
  totalsByAsset,
  yearBars,
  yearTotal,
  type Payout,
} from './payouts'

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

// docs/investimentos.md, "I.7e": a aba Proventos, com filtros por tipo de provento e por grupo do ativo.
describe('aba Proventos', () => {
  const mxrf = { assetId: 'mxrf', symbol: 'MXRF11', assetName: 'Maxi Renda', assetKind: 'Fii' as const, kind: 'FundIncome' as const }
  const taee = { assetId: 'taee', symbol: 'TAEE11', assetName: 'Taesa', assetKind: 'Unit' as const }
  const list = [
    payout({ ...mxrf, amountCents: 1000, date: '2026-11-14' }),
    payout({ ...mxrf, amountCents: 1000, date: '2026-10-14' }),
    payout({ amountCents: 3000, date: '2026-09-30', kind: 'InterestOnEquity' }),
    payout({ ...taee, amountCents: 500, date: '2026-09-02' }),
    payout({ ...mxrf, amountCents: 900, date: '2025-12-12' }),
  ]

  it('filtra por tipo de provento e por grupo; unit conta como ação', () => {
    expect(filterPayouts(list, { kind: 'FundIncome', group: null }).map((p) => p.amountCents)).toEqual([1000, 1000, 900])
    expect(filterPayouts(list, { kind: null, group: 'Stocks' }).map((p) => p.symbol)).toEqual(['BBAS3', 'TAEE11'])
    expect(filterPayouts(list, { kind: 'Dividend', group: 'Stocks' }).map((p) => p.symbol)).toEqual(['TAEE11'])
    expect(filterPayouts(list, { kind: null, group: null })).toHaveLength(5)
  })

  it('separa o recebido até hoje do que ainda vai cair', () => {
    expect(receivedSplit(inYear(list, 2026), '2026-10-14')).toEqual({ receivedCents: 4500, upcomingCents: 1000 })
  })

  it('12 barras do ano, cada mês com o recebido e o a receber', () => {
    const bars = monthBars(list, 2026, '2026-10-09')
    expect(bars).toHaveLength(12)
    expect(bars[8]).toEqual({ month: 9, receivedCents: 3500, upcomingCents: 0 })
    expect(bars[9]).toEqual({ month: 10, receivedCents: 0, upcomingCents: 1000 })
    expect(bars[10]).toEqual({ month: 11, receivedCents: 0, upcomingCents: 1000 })
    expect(bars[11]).toEqual({ month: 12, receivedCents: 0, upcomingCents: 0 })
  })

  it('um total por ano, também o ano sem provento', () => {
    expect(yearBars(list, [2024, 2025, 2026])).toEqual([
      { year: 2024, cents: 0 },
      { year: 2025, cents: 900 },
      { year: 2026, cents: 5500 },
    ])
  })

  it('quem mais pagou primeiro, com a parte no total; empate pelo código', () => {
    const ranks = rankByAsset([...inYear(list, 2026), payout({ assetId: 'aaaa', symbol: 'AAAA3', amountCents: 500, date: '2026-01-01' })])
    expect(ranks.map((r) => [r.symbol, r.cents])).toEqual([
      ['BBAS3', 3000],
      ['MXRF11', 2000],
      ['AAAA3', 500],
      ['TAEE11', 500],
    ])
    expect(ranks[0].share).toBeCloseTo(0.5)
    expect(rankByAsset([])).toEqual([])
  })

  it('só os grupos que pagaram viram filtro, na ordem dos grupos', () => {
    expect(payingGroups(list)).toEqual(['Stocks', 'Fiis'])
    expect(payingGroups([])).toEqual([])
  })

  it('média por mês do que já caiu: ano passado ÷ 12, o corrente ÷ meses até agora', () => {
    expect(monthlyAverage(monthBars(list, 2026, '2026-10-09'), 2026, '2026-10-09')).toBe(350)
    expect(monthlyAverage(monthBars(list, 2025, '2026-10-09'), 2025, '2026-10-09')).toBe(75)
  })

  it.each([
    [0, '0'],
    [38049, '380'],
    [99999, '1.000'],
    [123456, '1,2 mil'],
    [1250000, '12,5 mil'],
  ])('valor curto da coluna: %i → %s', (cents, expected) => {
    expect(shortReais(cents)).toBe(expected)
  })
})
