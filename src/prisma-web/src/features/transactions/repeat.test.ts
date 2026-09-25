import { describe, expect, it } from 'vitest'
import { isRepeatDraft, repeatDraftOf, repeatValues, type RepeatAccount, type RepeatSource } from './repeat'

// Exemplo de docs/fase-1.md, 5.1: "Mercado" de R$ 300,00 em 3x no Nubank.
const nubank: RepeatAccount = { id: 'nubank', type: 'CreditCard' }
const itau: RepeatAccount = { id: 'itau', type: 'Checking' }

const tx = (overrides: Partial<RepeatSource> = {}): RepeatSource => ({
  type: 'Expense',
  amountCents: 10000,
  accountId: 'nubank',
  categoryId: 'alimentacao',
  method: 'Credit',
  description: 'Mercado',
  purchaseDate: '2026-09-02',
  ...overrides,
})

describe('repeatDraftOf', () => {
  it('na compra parcelada, copia o total e o número de parcelas', () => {
    const draft = repeatDraftOf({ kind: 'purchase', totalCents: 30000, installments: [tx(), tx(), tx()] })

    expect(draft).toEqual({
      type: 'Expense',
      amountCents: 30000,
      accountId: 'nubank',
      categoryId: 'alimentacao',
      method: 'Credit',
      description: 'Mercado',
      installments: 3,
    })
  })

  it('no lançamento avulso, copia o valor e fica à vista', () => {
    const draft = repeatDraftOf({
      kind: 'single',
      transaction: tx({
        type: 'Income',
        amountCents: 800000,
        accountId: 'itau',
        method: 'Pix',
        description: '',
        categoryId: null,
      }),
    })

    expect(draft).toMatchObject({ type: 'Income', amountCents: 800000, installments: 1, categoryId: '', description: '' })
  })

  it('estorno não se repete', () => {
    expect(repeatDraftOf({ kind: 'single', transaction: tx({ type: 'Refund' }) })).toBeNull()
  })
})

describe('repeatValues', () => {
  const draft = repeatDraftOf({ kind: 'purchase', totalCents: 30000, installments: [tx(), tx(), tx()] })!

  it('abre com a data de hoje e o resto como estava', () => {
    expect(repeatValues(draft, [nubank, itau], itau, '2026-09-25')).toEqual({
      type: 'Expense',
      amountCents: 30000,
      accountId: 'nubank',
      categoryId: 'alimentacao',
      method: 'Credit',
      purchaseDate: '2026-09-25',
      installments: 3,
      description: 'Mercado',
    })
  })

  it('com a conta desativada, usa a conta padrão, o meio dela e fica à vista', () => {
    expect(repeatValues(draft, [itau], itau, '2026-09-25')).toMatchObject({
      accountId: 'itau',
      method: 'Pix',
      installments: 1,
      amountCents: 30000,
    })
  })

  it('receita que cairia num cartão vira despesa', () => {
    const salary = repeatDraftOf({ kind: 'single', transaction: tx({ type: 'Income', accountId: 'fechada', method: 'Pix' }) })!

    expect(repeatValues(salary, [nubank], nubank, '2026-09-25')).toMatchObject({ type: 'Expense', method: 'Credit' })
  })
})

describe('isRepeatDraft', () => {
  it('aceita só o formato do rascunho (o estado da navegação pode vir de outra versão do app)', () => {
    expect(isRepeatDraft(repeatDraftOf({ kind: 'single', transaction: tx() }))).toBe(true)
    expect(isRepeatDraft(null)).toBe(false)
    expect(isRepeatDraft({ type: 'Expense' })).toBe(false)
    expect(isRepeatDraft({ ...repeatDraftOf({ kind: 'single', transaction: tx() }), amountCents: '100' })).toBe(false)
  })
})
