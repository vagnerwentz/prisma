import { describe, expect, it } from 'vitest'
import {
  assetStatus,
  brokerageKindLabel,
  brokerageKindOptions,
  brokerageKindsError,
  brokerChoices,
  defaultBrokerageKinds,
  draftErrors,
  eventDetail,
  liquidationLine,
  noteTitle,
  noteTotals,
  notesTotals,
  parseQuantity,
  priceParts,
  showsBrokerageKinds,
  stepTitle,
  type PositionStep,
  unitsLabel,
  type DraftLine,
  type TradeNote,
} from './trades'

const line = (over: Partial<TradeNote['trades'][number]>): TradeNote['trades'][number] => ({
  assetId: 'itub',
  symbol: 'ITUB3',
  assetName: 'ITAU UNIBANCO',
  assetKind: 'Stock',
  assetHasLogo: false,
  side: 'Buy',
  quantity: 10,
  unitPriceCents: 2000,
  grossCents: 20000,
  costsCents: 0,
  ...over,
})

describe('brokerChoices', () => {
  it('puts the last used broker first and keeps the list without repeating it', () => {
    const choices = brokerChoices('XP')
    expect(choices[0]).toBe('XP')
    expect(choices.filter((b) => b === 'XP')).toHaveLength(1)
    expect(choices).toContain('Íon')
  })

  it('keeps a typed broker that is not in the list', () => {
    expect(brokerChoices('Corretora do Bairro')[0]).toBe('Corretora do Bairro')
  })

  it('starts with Íon when nothing was used yet', () => {
    expect(brokerChoices(null)[0]).toBe('Íon')
  })
})

describe('unitsLabel', () => {
  it.each([
    ['Stock', 80, '80 ações'],
    ['Stock', 1, '1 ação'],
    ['Unit', 3, '3 ações'],
    ['Fii', 10, '10 cotas'],
    ['Etf', 1, '1 cota'],
    ['Bdr', 5, '5 BDRs'],
    ['Bdr', 1, '1 BDR'],
    ['Stock', 12000, '12.000 ações'],
  ] as const)('%s × %i → %s', (kind, quantity, expected) => {
    expect(unitsLabel(kind, quantity)).toBe(expected)
  })
})

describe('priceParts', () => {
  it('shows both average prices', () => {
    expect(priceParts({ averagePriceCents: 2004, averageCostCents: 2020 })).toEqual([
      'PM R$\u00a020,04',
      'com custos R$\u00a020,20',
    ])
  })

  it('is empty without a position', () => {
    expect(priceParts({ averagePriceCents: null, averageCostCents: null })).toBeNull()
  })
})

describe('noteTotals', () => {
  it('adds buys and sells separately', () => {
    expect(
      noteTotals([
        { side: 'Buy', quantity: 50, unitPriceCents: 2000 },
        { side: 'Buy', quantity: 30, unitPriceCents: 2010 },
        { side: 'Sell', quantity: 10, unitPriceCents: 3000 },
      ]),
    ).toEqual({ buyCents: 160300, sellCents: 30000 })
  })
})

describe('notesTotals', () => {
  it('soma o comprado e o vendido de todas as notas', () => {
    expect(
      notesTotals([
        { trades: [{ side: 'Buy', quantity: 50, unitPriceCents: 2000 }] },
        {
          trades: [
            { side: 'Sell', quantity: 10, unitPriceCents: 3000 },
            { side: 'Buy', quantity: 1, unitPriceCents: 500 },
          ],
        },
      ]),
    ).toEqual({ buyCents: 100500, sellCents: 30000 })
    expect(notesTotals([])).toEqual({ buyCents: 0, sellCents: 0 })
  })
})

describe('noteTitle', () => {
  it('names the side and the assets once each, in order', () => {
    expect(noteTitle([line({}), line({ quantity: 30 }), line({ assetId: 'bbse', symbol: 'BBSE3' })])).toBe(
      'Compra · ITUB3, BBSE3',
    )
  })

  it('says when the note has both sides', () => {
    expect(noteTitle([line({}), line({ side: 'Sell', assetId: 'bbse', symbol: 'BBSE3' })])).toBe('Compra e venda · ITUB3, BBSE3')
  })

  it('names a sale', () => {
    expect(noteTitle([line({ side: 'Sell' })])).toBe('Venda · ITUB3')
  })
})

describe('parseQuantity', () => {
  it('keeps only digits', () => {
    expect(parseQuantity('1.200')).toBe(1200)
    expect(parseQuantity('')).toBe(0)
    expect(parseQuantity('abc')).toBe(0)
  })

  it('stops at the API ceiling', () => {
    expect(parseQuantity('9999999999')).toBe(100_000_000)
  })
})

describe('draftErrors', () => {
  const draft = (over: Partial<DraftLine>): DraftLine => ({
    asset: { id: 'itub', symbol: 'ITUB3', name: 'ITAU', kind: 'Stock', isActive: true, hasLogo: false },
    side: 'Buy',
    quantity: 10,
    unitPriceCents: 2000,
    ...over,
  })

  it('accepts a complete line', () => {
    expect(draftErrors(draft({}))).toEqual({})
  })

  it('asks for what is missing', () => {
    expect(draftErrors(draft({ asset: null, quantity: 0, unitPriceCents: 0 }))).toEqual({
      asset: 'Escolha o ativo.',
      quantity: 'Digite a quantidade.',
      price: 'Digite o preço.',
    })
  })
})

describe('stepTitle', () => {
  const step = (over: Partial<PositionStep>): PositionStep => ({
    kind: 'Trade',
    noteId: 'n',
    date: '2023-11-28',
    broker: 'Íon',
    side: 'Buy',
    quantity: 110,
    grossCents: 0,
    costsCents: 0,
    quantityAfter: 110,
    grossCostAfterCents: 0,
    costBasisAfterCents: 0,
    averagePriceCents: null,
    averageCostCents: null,
    realizedCents: null,
    relatedSymbol: null,
    fromQuantity: null,
    toQuantity: null,
    ...over,
  })

  it.each([
    [step({}), 'Compra · 110 cotas'],
    [step({ side: 'Sell', quantity: 1 }), 'Venda · 1 cota'],
    [step({ kind: 'Split', side: null, fromQuantity: 1, toQuantity: 8 }), 'Desdobramento 1:8'],
    [step({ kind: 'Split', side: null, fromQuantity: 10, toQuantity: 1 }), 'Grupamento 10:1'],
    [step({ kind: 'ConversionOut', side: null, relatedSymbol: 'BTHF11' }), 'Virou BTHF11'],
    [step({ kind: 'ConversionIn', side: null, relatedSymbol: 'BCFF11' }), 'Veio de BCFF11'],
    [step({ kind: 'LiquidationOut', side: null, relatedSymbol: 'BTHF11' }), 'Liquidado em BTHF11'],
    [step({ kind: 'LiquidationIn', side: null, relatedSymbol: 'BCFF11' }), 'Veio de BCFF11'],
  ])('%#', (s, expected) => {
    expect(stepTitle(s, 'Fii')).toBe(expected)
  })
})

// docs/investimentos.md, "I.7c1" e "I.7c4": o detalhe de cada evento no histórico do ativo.
describe('eventDetail', () => {
  const step = (over: Partial<PositionStep>): PositionStep => ({
    kind: 'Split',
    noteId: null,
    date: '2024-12-16',
    broker: null,
    side: null,
    quantity: 0,
    grossCents: 0,
    costsCents: 0,
    quantityAfter: 0,
    grossCostAfterCents: 0,
    costBasisAfterCents: 0,
    averagePriceCents: null,
    averageCostCents: null,
    realizedCents: null,
    relatedSymbol: null,
    fromQuantity: null,
    toQuantity: null,
    ...over,
  })

  it.each([
    [step({ kind: 'Split', quantity: 112, quantityAfter: 896 }), '112 → 896 cotas'],
    [step({ kind: 'ConversionOut', quantity: 880 }), '880 cotas saíram'],
    [step({ kind: 'ConversionIn', quantity: 704 }), '704 cotas entraram'],
    [step({ kind: 'LiquidationOut', quantity: 896, grossCents: 784896, realizedCents: 30464 }), '896 cotas saíram'],
    [step({ kind: 'LiquidationIn', quantity: 716, grossCents: 733900 }), '716 cotas a R$\u00a010,25'],
  ])('%#', (s, expected) => {
    expect(eventDetail(s, 'Fii')).toBe(expected)
  })
  it('a liquidação na origem diz o que recebeu e o resultado, com sinal', () => {
    expect(liquidationLine(step({ kind: 'LiquidationOut', grossCents: 784896, realizedCents: 30464 }))).toBe(
      'recebeu R$\u00a07.848,96 · resultado +R$\u00a0304,64',
    )
    expect(liquidationLine(step({ kind: 'LiquidationOut', grossCents: 100, realizedCents: -41400 }))).toBe(
      'recebeu R$\u00a01,00 · resultado −R$\u00a0414,00',
    )
    expect(liquidationLine(step({ kind: 'ConversionOut' }))).toBeNull()
  })
})

describe('assetStatus', () => {
  const asset = { isActive: true, convertedToSymbol: null, convertedOn: null }

  it('says nothing for a listed asset', () => {
    expect(assetStatus(asset)).toBeNull()
  })

  it('marks an asset that left the exchange', () => {
    expect(assetStatus({ ...asset, isActive: false })).toBe('saiu da bolsa')
  })

  it('tells what the asset became', () => {
    expect(assetStatus({ isActive: false, convertedToSymbol: 'BTHF11', convertedOn: '2024-12-16' })).toBe(
      'virou BTHF11 em 16/12/2024',
    )
  })
})

// docs/investimentos.md, "I.7b3": em quais tipos a corretagem foi cobrada.
describe('corretagem por tipo', () => {
  it('unit conta como ação; as opções seguem a ordem da nota, sem repetir', () => {
    expect(brokerageKindOptions(['Etf', 'Unit', 'Fii', 'Stock', 'Etf'])).toEqual(['Etf', 'Stock', 'Fii'])
  })

  it('rótulos: ação e os tipos do catálogo', () => {
    expect(['Stock', 'Etf', 'Fii', 'Bdr'].map((k) => brokerageKindLabel(k as 'Stock'))).toEqual(['Ação', 'ETF', 'FII', 'BDR'])
  })

  it('vem marcado só "Ação"', () => {
    expect(defaultBrokerageKinds).toEqual(['Stock'])
  })

  it('o caso do dono: ETF e FII com corretagem mostram a escolha', () => {
    expect(showsBrokerageKinds(490, ['Etf', 'Fii'], ['Stock'])).toBe(true)
  })

  it('com tudo marcado, a escolha continua à vista para desmarcar', () => {
    expect(showsBrokerageKinds(490, ['Stock', 'Etf'], ['Stock', 'Etf'])).toBe(true)
  })

  it('sem corretagem, ou só ações com "Ação" marcada, nada aparece', () => {
    expect(showsBrokerageKinds(0, ['Etf', 'Fii'], ['Stock'])).toBe(false)
    expect(showsBrokerageKinds(490, ['Stock'], ['Stock'])).toBe(false)
  })

  it('nota só com ações, mas "Ação" desmarcada (editada): a escolha aparece para corrigir', () => {
    expect(showsBrokerageKinds(490, ['Stock'], ['Etf'])).toBe(true)
  })

  it('recusa corretagem sem tipo marcado da nota, como a API', () => {
    expect(brokerageKindsError(490, ['Etf', 'Fii'], ['Stock'])).toBe('Marque em quais ativos a corretagem foi cobrada.')
    expect(brokerageKindsError(490, ['Etf', 'Fii'], ['Etf'])).toBeUndefined()
    expect(brokerageKindsError(0, ['Etf'], [])).toBeUndefined()
  })
})
