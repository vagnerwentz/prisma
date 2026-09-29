import { describe, expect, it } from 'vitest'
import { removalTexts, reopenRequest } from './removal'
import type { Recurrence } from './queries'

// docs/fase-2.md, 2.14, Tela: excluir o lançamento de uma série ativa pergunta antes.
const causaAnimal: Recurrence = {
  id: 'r1',
  accountId: 'a1',
  type: 'Expense',
  amountCents: 10000,
  categoryId: 'c1',
  description: 'Causa animal',
  method: 'Pix',
  frequency: 'Weekly',
  startDate: '2026-09-29',
  endDate: null,
  generatedThrough: '2026-09-29',
  nextOccurrence: '2026-10-06',
  isEnded: false,
  pendings: [],
}

describe('removalTexts', () => {
  it('says the series was ended too', () => {
    expect(
      removalTexts({ series: causaAnimal, endSeries: true, summary: 'Causa animal · R$ 100,00', today: '2026-09-29' }),
    ).toEqual({
      title: 'Lançamento excluído e série encerrada',
      description: 'Causa animal · R$ 100,00',
      undone: 'Lançamento restaurado e série reaberta',
    })
  })

  it('says the series goes on, with the next date', () => {
    expect(
      removalTexts({ series: causaAnimal, endSeries: false, summary: 'Causa animal · R$ 100,00', today: '2026-09-29' }),
    ).toEqual({
      title: 'Lançamento excluído',
      description: 'A série continua: próxima 06/10.',
      undone: 'Lançamento restaurado',
    })
  })
})

describe('reopenRequest', () => {
  it('keeps the series as it was, with the end it had before', () => {
    expect(reopenRequest({ ...causaAnimal, endDate: '2026-12-31' })).toEqual({
      accountId: 'a1',
      amountCents: 10000,
      categoryId: 'c1',
      description: 'Causa animal',
      method: 'Pix',
      frequency: 'Weekly',
      nextDate: null,
      endDate: '2026-12-31',
    })
  })

  it('reopens a series that had no end', () => expect(reopenRequest(causaAnimal).endDate).toBeNull())
})
