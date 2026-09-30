import { describe, expect, it } from 'vitest'
import {
  autoDebitLine,
  bellLabel,
  canAutoDebit,
  confirmedMessage,
  debitDetails,
  dueDateChoices,
  estimatedNote,
} from './autoDebit'
import { noRepeat, repeatRequest, repeatToSend, type RepeatChoice } from './repeatChoice'

// docs/fase-2.md, 2.15: débito automático só em despesa, em conta corrente, todo mês; o vencimento da
// primeira ocorrência até 7 dias antes da data do lançamento; o sino e o conferir.
const monthly: RepeatChoice = { ...noRepeat, frequency: 'Monthly' }
const autoDebit: RepeatChoice = { ...monthly, autoDebit: true, amountVaries: true, dueDaysBefore: 0 }
const checkingExpense = { type: 'Expense' as const, isCard: false, installments: 1, accountType: 'Checking' as const }

describe('canAutoDebit', () => {
  it('is only an expense from a checking account', () => {
    expect(canAutoDebit({ type: 'Expense', accountType: 'Checking' })).toBe(true)
    expect(canAutoDebit({ type: 'Income', accountType: 'Checking' })).toBe(false)
    expect(canAutoDebit({ type: 'Expense', accountType: 'CreditCard' })).toBe(false)
    expect(canAutoDebit({ type: 'Expense', accountType: 'Cash' })).toBe(false)
    expect(canAutoDebit({ type: 'Expense', accountType: 'Investment' })).toBe(false)
  })
})

describe('repeatRequest', () => {
  // A tela aberta antes da 2.26 não mandava os campos novos; a série comum continua igual.
  it('sends a regular series without the auto debit fields', () => {
    expect(repeatRequest(monthly, '2026-09-15')).toEqual({ frequency: 'Monthly', endDate: null })
  })

  it('sends the auto debit with the due date of the first occurrence', () => {
    expect(repeatRequest({ ...autoDebit, dueDaysBefore: 1 }, '2026-09-21')).toEqual({
      frequency: 'Monthly',
      endDate: null,
      autoDebit: true,
      amountVaries: true,
      dueDate: '2026-09-20',
    })
  })

  it('sends a fixed amount as a fixed amount', () => {
    expect(repeatRequest({ ...autoDebit, amountVaries: false }, '2026-09-15')).toMatchObject({ amountVaries: false })
  })

  it('never sends a weekly auto debit', () => {
    expect(repeatRequest({ ...autoDebit, frequency: 'Weekly' }, '2026-09-15')).toEqual({ frequency: 'Weekly', endDate: null })
  })
})

describe('repeatToSend', () => {
  it('keeps the auto debit on a checking account expense', () => {
    expect(repeatToSend(checkingExpense, autoDebit, '2026-09-15')).toMatchObject({ autoDebit: true })
  })

  // Escolher débito automático e depois trocar a conta ou o tipo: a linha some, e a série vai comum.
  it('drops the auto debit when the account or the type no longer allow it', () => {
    expect(repeatToSend({ ...checkingExpense, accountType: 'Cash' }, autoDebit, '2026-09-15')).toEqual({
      frequency: 'Monthly',
      endDate: null,
    })
    expect(repeatToSend({ ...checkingExpense, type: 'Income' }, autoDebit, '2026-09-15')).toEqual({
      frequency: 'Monthly',
      endDate: null,
    })
  })
})

describe('dueDateChoices', () => {
  // Copel lançada na segunda 21/09/2026: pode vencer no próprio dia ou até 7 dias antes (regra 4).
  it('offers the transaction date and the 7 days before it', () => {
    expect(dueDateChoices('2026-09-21')).toEqual([
      { daysBefore: 0, date: '2026-09-21', label: 'seg 21/09' },
      { daysBefore: 1, date: '2026-09-20', label: 'dom 20/09' },
      { daysBefore: 2, date: '2026-09-19', label: 'sáb 19/09' },
      { daysBefore: 3, date: '2026-09-18', label: 'sex 18/09' },
      { daysBefore: 4, date: '2026-09-17', label: 'qui 17/09' },
      { daysBefore: 5, date: '2026-09-16', label: 'qua 16/09' },
      { daysBefore: 6, date: '2026-09-15', label: 'ter 15/09' },
      { daysBefore: 7, date: '2026-09-14', label: 'seg 14/09' },
    ])
  })

  it('crosses the month', () => {
    expect(dueDateChoices('2026-10-02').at(-1)).toEqual({ daysBefore: 7, date: '2026-09-25', label: 'sex 25/09' })
  })
})

describe('autoDebitLine', () => {
  it('says the due day and the next due date', () => {
    expect(autoDebitLine({ due: '2026-09-20', endDate: null, today: '2026-09-29' })).toBe(
      'Débito automático · vence todo dia 20 · próximo vencimento 20/10',
    )
  })
})

describe('the bell', () => {
  it('counts what is left to check', () => {
    expect(bellLabel(0)).toBe('Nada para conferir')
    expect(bellLabel(1)).toBe('1 débito para conferir')
    expect(bellLabel(3)).toBe('3 débitos para conferir')
  })

  it('shows where and when the money left', () => {
    expect(debitDetails({ purchaseDate: '2026-11-16' }, 'Itaú', '2026-11-20')).toBe('Saiu em 16/11 · Itaú')
    expect(debitDetails({ purchaseDate: '2026-12-15' }, undefined, '2027-01-05')).toBe('Saiu em 15/12/2026')
  })

  it('confirms with the amount that stayed', () => {
    expect(confirmedMessage('Sabesp', 10237)).toBe('Sabesp conferido: R$ 102,37.')
    expect(confirmedMessage('', 9500)).toBe('Débito conferido: R$ 95,00.')
  })

  it('tells how much of the month is still an estimate', () => {
    expect(estimatedNote(9500)).toBe('≈ R$ 95,00 a conferir')
  })
})
