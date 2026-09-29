import { describe, expect, it } from 'vitest'
import { accountSeries, groupSeries, pendingTexts, seriesCount } from './listing'
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
