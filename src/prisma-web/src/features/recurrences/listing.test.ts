import { describe, expect, it } from 'vitest'
import { accountSeries, groupSeries, pendingTexts, seriesCount, seriesWhen } from './listing'
import type { Recurrence } from './queries'

// docs/fase-2.md, 2.14, Tela: a lista "Recorrências" (pendências no topo, ativas pela próxima data,
// encerradas no fim) e a entrada pela aba Contas.
const base: Recurrence = {
  id: 'r1',
  accountId: 'a1',
  type: 'Expense',
  amountCents: 40000,
  categoryId: null,
  description: 'Pet',
  method: 'Credit',
  frequency: 'Monthly',
  startDate: '2026-09-25',
  endDate: null,
  generatedThrough: '2026-10-25',
  nextOccurrence: '2026-11-25',
  isEnded: false,
  pendings: [],
  kind: 'Regular',
  amountVaries: false,
  nextTransactionDate: '2026-11-25',
}

// Copel: vence todo dia 20, desde 20/09; o de 20/11 (feriado) é debitado em 23/11 (docs/fase-2.md, 2.15).
const copel: Recurrence = {
  ...base,
  id: 'copel',
  description: 'Copel',
  method: 'Debit',
  amountCents: 18000,
  startDate: '2026-09-20',
  generatedThrough: '2026-10-20',
  nextOccurrence: '2026-11-20',
  kind: 'AutoDebit',
  amountVaries: true,
  nextTransactionDate: '2026-11-23',
}

const pending = { id: 'p1', accountId: 'a1', amountCents: 40000, occurrenceDate: '2026-10-25', statementReference: '2026-11' }

describe('pendingTexts', () => {
  // A tabela "Geração" da 2.14: a fatura 2026-11 do Visa foi paga antes da geração.
  it('explains the paid statement and names the two destinations', () => {
    expect(pendingTexts(pending)).toEqual({
      reason: 'Cairia na fatura de novembro, já paga.',
      next: 'Lançar em dezembro',
      same: 'Lançar em novembro',
      sameHint: 'Para lançar em novembro, desfaça antes o pagamento da fatura.',
      launchedNext: 'Lançada na fatura de dezembro',
      launchedSame: 'Lançada na fatura de novembro',
    })
  })

  it('turns the year in december', () => {
    expect(pendingTexts({ ...pending, statementReference: '2026-12' }).next).toBe('Lançar em janeiro')
  })
})

describe('seriesCount', () => {
  it('counts the active ones and the pendings', () => {
    expect(seriesCount([base, { ...base, id: 'r2', pendings: [pending] }, { ...base, id: 'r3', isEnded: true }])).toBe(
      '2 ativas · 1 pendente',
    )
  })

  it('uses the singular', () => expect(seriesCount([base])).toBe('1 ativa'))

  it('says when all are ended', () => expect(seriesCount([{ ...base, isEnded: true }])).toBe('Todas encerradas'))

  it('says nothing without series', () => expect(seriesCount([])).toBeNull())

  // docs/fase-2.md, 2.15, Tela: "Recorrências · 3 ativas, 2 em débito automático".
  it('counts the active auto debits', () => {
    expect(seriesCount([base, copel, { ...copel, id: 'c2' }, { ...copel, id: 'c3', isEnded: true }])).toBe(
      '3 ativas · 2 em débito automático',
    )
    expect(seriesCount([copel])).toBe('1 ativa · 1 em débito automático')
  })
})

describe('groupSeries', () => {
  it('puts pendings first, active by the next date and ended last', () => {
    const later = { ...base, id: 'later', nextOccurrence: '2026-12-01' }
    const sooner = { ...base, id: 'sooner', nextOccurrence: '2026-10-30' }
    const ended = { ...base, id: 'ended', isEnded: true, nextOccurrence: null }
    const waiting = { ...base, id: 'waiting', pendings: [pending] }

    const groups = groupSeries([later, ended, waiting, sooner])

    expect(groups.pendings.map((p) => [p.series.id, p.pending.id])).toEqual([['waiting', 'p1']])
    // waiting tem a próxima em 25/11, entre as outras duas.
    expect(groups.active.map((r) => r.id)).toEqual(['sooner', 'waiting', 'later'])
    expect(groups.ended.map((r) => r.id)).toEqual(['ended'])
  })

  // Os débitos automáticos ativos numa seção própria, pela data do próximo débito; os encerrados, com os outros.
  it('puts the active auto debits apart', () => {
    const sabesp = { ...copel, id: 'sabesp', nextOccurrence: '2026-11-15', nextTransactionDate: '2026-11-16' }
    const endedDebit = { ...copel, id: 'ended-debit', isEnded: true, nextOccurrence: null, nextTransactionDate: null }

    const groups = groupSeries([base, copel, endedDebit, sabesp])

    expect(groups.autoDebits.map((r) => r.id)).toEqual(['sabesp', 'copel'])
    expect(groups.active.map((r) => r.id)).toEqual(['r1'])
    expect(groups.ended.map((r) => r.id)).toEqual(['ended-debit'])
  })
})

describe('seriesWhen', () => {
  it('says the frequency and the next date of a regular series', () =>
    expect(seriesWhen(base, '2026-10-30')).toBe('Todo mês, no dia 25 · próxima 25/11'))

  // O vencimento e o dia do débito, que a API já passou para o dia útil.
  it('says the due day and the next debit of an auto debit', () =>
    expect(seriesWhen(copel, '2026-10-30')).toBe('Vence dia 20 · próximo débito 23/11'))

  it('says when it ended', () =>
    expect(seriesWhen({ ...copel, isEnded: true, nextOccurrence: null, nextTransactionDate: null }, '2026-10-30')).toBe(
      'Encerrada · última 20/10',
    ))
})

// Excluir a conta encerra as séries dela (decisão do dono, 2026-09-29): a confirmação e o aviso dizem quantas.
describe('accountSeries', () => {
  it('counts the active series of the account', () =>
    expect(
      accountSeries(
        [base, { ...base, id: 'r2' }, { ...base, id: 'r3', isEnded: true }, { ...base, id: 'r4', accountId: 'a2' }],
        'a1',
      ),
    ).toEqual({
      count: 2,
      warning: 'As 2 recorrências dela serão encerradas.',
      done: '2 recorrências encerradas',
    }))

  it('uses the singular', () =>
    expect(accountSeries([base], 'a1')).toEqual({
      count: 1,
      warning: 'A recorrência dela será encerrada.',
      done: '1 recorrência encerrada',
    }))

  it('says nothing without active series', () => expect(accountSeries([{ ...base, isEnded: true }], 'a1')).toBeNull())
})
