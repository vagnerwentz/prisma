import { describe, expect, it } from 'vitest'
import { accountGate } from './accountGate'

// Lançar exige uma conta ativa (etapa 2.19): sem nenhuma, cria-se a primeira ali mesmo; com todas
// desativadas, a mensagem é outra (reativar em Contas ou criar uma nova).
describe('accountGate', () => {
  it('sem nenhuma conta, pede a primeira', () => {
    expect(accountGate([])).toBe('none')
  })

  it('com contas, todas desativadas, avisa que estão desativadas', () => {
    expect(accountGate([{ isActive: false }, { isActive: false }])).toBe('inactive')
  })

  it('com ao menos uma conta ativa, libera o lançamento', () => {
    expect(accountGate([{ isActive: false }, { isActive: true }])).toBe('ready')
  })
})
