import { describe, expect, it } from 'vitest'
import { logoSrc, splitSymbol } from './assets'

describe('splitSymbol', () => {
  it.each([
    ['MXRF11', 'MXRF', '11'],
    ['BBAS3', 'BBAS', '3'],
    ['AAPL34', 'AAPL', '34'],
    ['03BK11', '03BK', '11'], // código que começa com dígito
    ['B3SA3', 'B3SA', '3'], // dígito no meio da raiz
    ['BDOM', 'BDOM', ''], // fundo sem número
  ])('%s → %s + %s', (symbol, root, suffix) => {
    expect(splitSymbol(symbol)).toEqual({ root, suffix })
  })
})

describe('logoSrc', () => {
  it('ação, unit e BDR com logo guardado mostram o logo', () => {
    expect(logoSrc({ id: 'bbas', kind: 'Stock', hasLogo: true })).toBe('/api/assets/bbas/logo')
    expect(logoSrc({ id: 'taee', kind: 'Unit', hasLogo: true })).toBe('/api/assets/taee/logo')
    expect(logoSrc({ id: 'aapl', kind: 'Bdr', hasLogo: true })).toBe('/api/assets/aapl/logo')
  })

  it('fundo mostra o código mesmo com o logo da gestora guardado', () => {
    for (const kind of ['Fii', 'Etf', 'FiInfra', 'FiAgro', 'Fip', 'Fidc', 'OtherFund', 'Unknown'] as const)
      expect(logoSrc({ id: 'x', kind, hasLogo: true })).toBeUndefined()
  })

  it('sem logo guardado, código', () => {
    expect(logoSrc({ id: 'bbas', kind: 'Stock', hasLogo: false })).toBeUndefined()
  })
})
