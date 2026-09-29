import { describe, expect, it } from 'vitest'
import { describeSeries, occurrence, seriesLine } from './schedule'

// docs/fase-2.md, 2.14: a agenda (regra 3) e o texto da linha "se repete" do Novo lançamento. Os
// valores esperados saem da tabela "Agenda" da especificação.
describe('occurrence', () => {
  it('keeps the day of the start every month, the missing day becoming the last one', () => {
    const dates = [1, 2, 3, 4, 5].map((i) => occurrence('2026-10-31', 'Monthly', i))
    expect(dates).toEqual(['2026-11-30', '2026-12-31', '2027-01-31', '2027-02-28', '2027-03-31'])
  })

  it('adds seven days each week', () => {
    const dates = [1, 2, 3, 4, 5].map((i) => occurrence('2026-10-02', 'Weekly', i))
    expect(dates).toEqual(['2026-10-09', '2026-10-16', '2026-10-23', '2026-10-30', '2026-11-06'])
  })

  it('does not move weekends: the rent of 10/10 is a Saturday', () => {
    expect(occurrence('2026-09-10', 'Monthly', 1)).toBe('2026-10-10')
  })
})

describe('seriesLine', () => {
  it('says the day of the month and the next date', () => {
    expect(seriesLine({ start: '2026-09-25', frequency: 'Monthly', endDate: null, today: '2026-09-25' })).toBe(
      'Todo mês, no dia 25 · próxima 25/10',
    )
  })

  it('shows the last day when the month lacks the day', () => {
    expect(seriesLine({ start: '2026-10-31', frequency: 'Monthly', endDate: null, today: '2026-10-31' })).toBe(
      'Todo mês, no dia 31 · próxima 30/11',
    )
  })

  it('says the weekday of a weekly series', () => {
    expect(seriesLine({ start: '2026-10-02', frequency: 'Weekly', endDate: null, today: '2026-10-02' })).toBe(
      'Toda semana, às sextas · próxima 09/10',
    )
  })

  it('names every weekday', () => {
    const week = ['2026-10-04', '2026-10-05', '2026-10-06', '2026-10-07', '2026-10-08', '2026-10-09', '2026-10-10']
    expect(week.map((d) => seriesLine({ start: d, frequency: 'Weekly', endDate: null, today: d }).split(' · ')[0])).toEqual([
      'Toda semana, aos domingos',
      'Toda semana, às segundas',
      'Toda semana, às terças',
      'Toda semana, às quartas',
      'Toda semana, às quintas',
      'Toda semana, às sextas',
      'Toda semana, aos sábados',
    ])
  })

  it('adds the end date', () => {
    expect(seriesLine({ start: '2026-09-25', frequency: 'Monthly', endDate: '2026-11-30', today: '2026-09-25' })).toBe(
      'Todo mês, no dia 25 · próxima 25/10 · até 30/11',
    )
  })

  it('shows the year when the next date is in another year', () => {
    expect(seriesLine({ start: '2026-12-25', frequency: 'Monthly', endDate: null, today: '2026-12-25' })).toBe(
      'Todo mês, no dia 25 · próxima 25/01/2027',
    )
  })
})

describe('describeSeries', () => {
  // A diarista lançada em 29/09 com a data de 04/09: as semanas que já passaram saem junto.
  it('lists the occurrences that are due already, and the next one after today', () => {
    const series = describeSeries({ start: '2026-09-04', frequency: 'Weekly', endDate: null, today: '2026-09-29' })
    expect(series.dueNow).toEqual(['2026-09-11', '2026-09-18', '2026-09-25'])
    expect(series.next).toBe('2026-10-02')
  })

  it('includes today among the ones due now', () => {
    const series = describeSeries({ start: '2026-08-25', frequency: 'Monthly', endDate: null, today: '2026-09-25' })
    expect(series.dueNow).toEqual(['2026-09-25'])
    expect(series.next).toBe('2026-10-25')
  })

  it('has nothing due when the start is in the future', () => {
    const series = describeSeries({ start: '2026-10-10', frequency: 'Monthly', endDate: null, today: '2026-09-29' })
    expect(series.dueNow).toEqual([])
    expect(series.next).toBe('2026-11-10')
  })

  it('stops at the end date, inclusive', () => {
    const series = describeSeries({ start: '2026-09-25', frequency: 'Monthly', endDate: '2026-11-25', today: '2026-12-01' })
    expect(series.dueNow).toEqual(['2026-10-25', '2026-11-25'])
    expect(series.next).toBeNull()
  })
})
