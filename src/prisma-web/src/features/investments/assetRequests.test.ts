import { describe, expect, it } from 'vitest'
import { arrivedItem, missingAction, requestableSymbol, requestedLabel, type AssetRequest } from './assetRequests'

// docs/investimentos.md, "I.7c3". Os mesmos casos do domínio (AssetRequestTests): a tela só mostra a ação para o que a
// API aceitaria.
describe('requestableSymbol', () => {
  it.each([
    ['CVBI11', 'CVBI11'],
    ['cvbi11', 'CVBI11'],
    ['  CVBI 11 ', 'CVBI11'],
    ['CVBI11F', 'CVBI11'],
    ['bbas3f', 'BBAS3'],
    ['TAEE11', 'TAEE11'],
    ['EQMA3B', 'EQMA3B'],
    ['B3SA3', 'B3SA3'],
  ])('%s → %s', (typed, symbol) => {
    expect(requestableSymbol(typed)).toBe(symbol)
  })

  it.each(['', '   ', 'CVBI', 'CVBI111', 'VBI CRI', 'CV11', 'CVBI11AB', 'CVBÍ11', 'Maxi Renda'])('%s não é código', (typed) => {
    expect(requestableSymbol(typed)).toBeNull()
  })
})

const waiting: AssetRequest = { id: 'r1', symbol: 'CVBI11', requestedOn: '2026-10-02', status: 'Waiting', asset: null }

describe('missingAction', () => {
  it('código novo: avisar que falta', () => {
    expect(missingAction('abcd11', [waiting])).toEqual({ kind: 'request', symbol: 'ABCD11' })
  })

  it('código já avisado, mesmo digitado no fracionário: lembra quando', () => {
    expect(missingAction('cvbi11f', [waiting])).toEqual({ kind: 'already', symbol: 'CVBI11', requestedOn: '2026-10-02' })
  })

  it('busca pelo nome: pede o código', () => {
    expect(missingAction('VBI CRI', [waiting])).toEqual({ kind: 'hint' })
  })
})

describe('requestedLabel', () => {
  it('no ano corrente, só dia e mês', () => {
    expect(requestedLabel('2026-10-02', '2026-10-08')).toBe('02/10')
  })

  it('em outro ano, a data inteira', () => {
    expect(requestedLabel('2025-12-30', '2026-01-02')).toBe('30/12/2025')
  })
})

describe('arrivedItem', () => {
  it('o ativo que chegou, pronto para a nota', () => {
    const arrived: AssetRequest = {
      ...waiting,
      status: 'Arrived',
      asset: { id: 'a1', name: 'VBI CRI', kind: 'Fii', hasLogo: false, isActive: false },
    }
    expect(arrivedItem(arrived)).toEqual({ id: 'a1', symbol: 'CVBI11', name: 'VBI CRI', kind: 'Fii', isActive: false, hasLogo: false })
  })

  it('o que espera não abre nota', () => {
    expect(arrivedItem(waiting)).toBeNull()
  })
})
