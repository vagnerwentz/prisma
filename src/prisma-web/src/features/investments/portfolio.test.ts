import { describe, expect, it } from 'vitest'
import {
  asOfLabel,
  formatWeight,
  groupColors,
  groupLabels,
  groupOf,
  groupOrder,
  holdingsWithoutNotes,
  prismBands,
  readView,
  splitAssets,
  viewSearch,
  type PortfolioAsset,
  type PortfolioSlice,
} from './portfolio'
import { signedCents } from './holdings'

// docs/investimentos.md, "I.7e": a carteira em um ano, o espectro e o endereço da tela.
const slice = (group: PortfolioSlice['group'], investedCents: number): PortfolioSlice => ({
  group,
  investedCents,
  weightBasisPoints: 0,
  assetCount: 1,
  payoutsCents: 0,
  realizedCents: 0,
})

describe('grupos', () => {
  it.each([
    ['Stock', 'Stocks'],
    ['Unit', 'Stocks'],
    ['Fii', 'Fiis'],
    ['Etf', 'Etfs'],
    ['Bdr', 'Bdrs'],
    ['FiInfra', 'OtherFunds'],
    ['FiAgro', 'OtherFunds'],
    ['Fip', 'OtherFunds'],
    ['Fidc', 'OtherFunds'],
    ['OtherFund', 'OtherFunds'],
    ['Unknown', 'Other'],
  ] as const)('%s → %s, como o domínio', (kind, group) => {
    expect(groupOf(kind)).toBe(group)
  })

  it('todo grupo tem rótulo e uma cor própria', () => {
    expect(groupOrder.map((g) => groupLabels[g])).toEqual(['Ações', 'FIIs', 'ETFs', 'BDRs', 'Outros fundos', 'Outros'])
    expect(new Set(groupOrder.map((g) => groupColors[g])).size).toBe(groupOrder.length)
  })
})

describe('formatWeight', () => {
  it.each([
    [10000, '100%'],
    [6000, '60%'],
    [3333, '33,3%'],
    [5, '0,1%'],
    [4, '< 0,1%'],
    [0, '0%'],
  ])('%i → %s', (bp, expected) => {
    expect(formatWeight(bp)).toBe(expected)
  })
})

describe('prismBands', () => {
  it('espessura proporcional ao custo, com o vão entre as faixas', () => {
    const bands = prismBands([slice('Fiis', 6000), slice('Stocks', 3000), slice('Etfs', 1000)], 108, 4, 2)
    expect(bands.map((b) => [b.group, b.y0, b.y1])).toEqual([
      ['Fiis', 0, 60],
      ['Stocks', 64, 94],
      ['Etfs', 98, 108],
    ])
  })

  it('a faixa pequena fica na mínima, e o que ela ganha sai das grandes', () => {
    const bands = prismBands([slice('Fiis', 9990), slice('Stocks', 9), slice('Bdrs', 1)], 100, 0, 6)
    const thickness = bands.map((b) => b.y1 - b.y0)
    expect(thickness[1]).toBe(6)
    expect(thickness[2]).toBe(6)
    expect(thickness[0]).toBeCloseTo(88)
    expect(bands.at(-1)!.y1).toBeCloseTo(100)
  })

  it('grupo sem custo não vira faixa; carteira vazia não tem faixa', () => {
    expect(prismBands([slice('Fiis', 100), slice('Stocks', 0)], 50, 4, 2).map((b) => b.group)).toEqual(['Fiis'])
    expect(prismBands([], 50, 4, 2)).toEqual([])
  })

  it('grupos demais para a altura: a mínima cede e nada passa da borda', () => {
    const bands = prismBands(
      groupOrder.map((g) => slice(g, g === 'Fiis' ? 1_000_000 : 1)),
      12,
      0,
      6,
    )
    expect(bands.at(-1)!.y1).toBeCloseTo(12)
    expect(bands.every((b) => b.y1 - b.y0 >= 2 - 1e-9)).toBe(true)
  })
})

describe('endereço da tela', () => {
  it('lê a aba e o ano; o que falta ou não vale cai no padrão', () => {
    expect(readView(new URLSearchParams('aba=proventos&ano=2020'), 2026)).toEqual({ tab: 'proventos', year: 2020 })
    expect(readView(new URLSearchParams(''), 2026)).toEqual({ tab: 'carteira', year: 2026 })
    expect(readView(new URLSearchParams('aba=xyz&ano=2031'), 2026)).toEqual({ tab: 'carteira', year: 2026 })
    expect(readView(new URLSearchParams('ano=20x0'), 2026).year).toBe(2026)
  })

  it('escreve só o que difere do padrão', () => {
    expect(viewSearch('carteira', 2026, 2026)).toBe('')
    expect(viewSearch('notas', 2026, 2026)).toBe('?aba=notas')
    expect(viewSearch('proventos', 2020, 2026)).toBe('?aba=proventos&ano=2020')
  })
})

describe('rótulos', () => {
  it('a data da foto: hoje, ou o fim do ano', () => {
    expect(asOfLabel('2026-10-09', '2026-10-09')).toBe('hoje')
    expect(asOfLabel('2020-12-31', '2026-10-09')).toBe('em 31/12/2020')
  })
})

describe('splitAssets', () => {
  const asset = (symbol: string, group: PortfolioAsset['group'], quantity: number) =>
    ({ assetId: symbol, symbol, group, quantity }) as PortfolioAsset

  it('separa o que tem quantidade do que saiu, e filtra pelo grupo', () => {
    const list = [asset('MXRF11', 'Fiis', 10), asset('ITUB3', 'Stocks', 5), asset('BBAS3', 'Stocks', 0)]
    expect(splitAssets(list, null).held.map((a) => a.symbol)).toEqual(['MXRF11', 'ITUB3'])
    expect(splitAssets(list, null).gone.map((a) => a.symbol)).toEqual(['BBAS3'])
    expect(splitAssets(list, 'Stocks')).toEqual({ held: [list[1]], gone: [list[2]] })
  })
})

describe('signedCents', () => {
  it('resultado com sinal, em tinta', () => {
    expect(signedCents(50000)).toBe('+R$ 500,00')
    expect(signedCents(-500)).toBe('−R$ 5,00')
    expect(signedCents(0)).toBe('+R$ 0,00')
  })
})

describe('holdingsWithoutNotes', () => {
  const holding = (assetId: string) => ({ id: `h-${assetId}`, assetId })

  it('só o que nunca teve nota nem evento e não está na lista do ano', () => {
    const holdings = [holding('MXRF11'), holding('BCFF11'), holding('IRDM11'), holding('CARE11'), holding('VISC11')]
    const listed = new Set(['MXRF11'])
    // BCFF11 e IRDM11 viraram outros em anos passados; CARE11 foi vendido: têm histórico.
    const tracked = new Set(['MXRF11', 'BCFF11', 'IRDM11', 'CARE11'])

    expect(holdingsWithoutNotes(holdings, listed, tracked).map((h) => h.assetId)).toEqual(['VISC11'])
  })
})
