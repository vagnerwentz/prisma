import { describe, expect, it } from 'vitest'
import {
  confirmedToast,
  eventReviewDetail,
  eventReviewTitle,
  parseReportedQuantity,
  showsConfirmAll,
  type EventReview,
} from './eventReviews'

// docs/investimentos.md, "I.7c2": o que cada evento fez com a quantidade da pessoa.
const review = (over: Partial<EventReview>): EventReview => ({
  eventId: 'e',
  kind: 'Split',
  date: '2023-08-01',
  assetId: 'snag',
  symbol: 'SNAG11',
  assetKind: 'FiAgro',
  hasLogo: false,
  targetSymbol: null,
  fromQuantity: 1,
  toQuantity: 10,
  quantityBefore: 19,
  quantityAfter: 190,
  targetUnitPriceCents: null,
  ...over,
})

describe('título e detalhe de cada evento', () => {
  it('desdobramento e grupamento', () => {
    expect(eventReviewTitle(review({}))).toBe('SNAG11 · desdobramento\u00a01:10')
    expect(eventReviewDetail(review({}))).toBe('01/08/2023 · 19 → 190 cotas')
    expect(eventReviewTitle(review({ fromQuantity: 10, toQuantity: 1 }))).toBe('SNAG11 · grupamento\u00a010:1')
  })

  it('conversão', () => {
    const petz = review({
      kind: 'Conversion',
      symbol: 'PETZ3',
      assetKind: 'Stock',
      targetSymbol: 'AUAU3',
      quantityBefore: 50,
      quantityAfter: 50,
      date: '2026-01-02',
    })
    expect(eventReviewTitle(petz)).toBe('PETZ3 → AUAU3 · conversão')
    expect(eventReviewDetail(petz)).toBe('02/01/2026 · 50 ações → 50 AUAU3')
  })

  it('liquidação, com o preço de referência', () => {
    const bcff = review({
      kind: 'Liquidation',
      symbol: 'BCFF11',
      assetKind: 'Fii',
      targetSymbol: 'BTHF11',
      quantityBefore: 896,
      quantityAfter: 716,
      targetUnitPriceCents: 1025,
      date: '2024-12-16',
    })
    expect(eventReviewTitle(bcff)).toBe('BCFF11 → BTHF11 · liquidação')
    expect(eventReviewDetail(bcff)).toBe('16/12/2024 · 896 cotas → 716 BTHF11 a R$ 10,25')
  })

  it('milhares com ponto', () => {
    expect(eventReviewDetail(review({ quantityBefore: 1200, quantityAfter: 12000 }))).toBe('01/08/2023 · 1.200 → 12.000 cotas')
  })
})

describe('conferir', () => {
  it('"Conferir todos" só com mais de um', () => {
    expect(showsConfirmAll(0)).toBe(false)
    expect(showsConfirmAll(1)).toBe(false)
    expect(showsConfirmAll(2)).toBe(true)
  })

  it('o aviso diz o que foi conferido', () => {
    expect(confirmedToast([review({})])).toBe('SNAG11 conferido')
    expect(confirmedToast([review({}), review({ symbol: 'BBAS3' })])).toBe('2 eventos conferidos')
  })

  it('a quantidade da corretora: dígitos, vazio é "não sei", até o teto', () => {
    expect(parseReportedQuantity('704')).toBe(704)
    expect(parseReportedQuantity('1.200')).toBe(1200)
    expect(parseReportedQuantity('')).toBeNull()
    expect(parseReportedQuantity('abc')).toBeNull()
    expect(parseReportedQuantity('999999999999')).toBe(100_000_000)
    expect(parseReportedQuantity('0')).toBe(0)
  })
})
