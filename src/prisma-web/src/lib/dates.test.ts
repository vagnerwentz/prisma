import { afterEach, describe, expect, it, vi } from 'vitest'
import { formatDayHeading, formatMonth, monthOf, monthRange, shiftMonth, todayInSaoPaulo } from './dates'

describe('todayInSaoPaulo', () => {
  // Mesmo caso do backend (etapa 1.2): 23h30 em São Paulo é 02h30 UTC do dia seguinte.
  it('compra às 23h30 em São Paulo continua no mesmo dia', () => {
    expect(todayInSaoPaulo(new Date('2026-03-16T02:30:00Z'))).toBe('2026-03-15')
  })

  it('o dia vira à meia-noite de São Paulo, não à de UTC', () => {
    expect(todayInSaoPaulo(new Date('2026-03-16T02:59:59Z'))).toBe('2026-03-15')
    expect(todayInSaoPaulo(new Date('2026-03-16T03:00:00Z'))).toBe('2026-03-16')
  })
})

describe('formatDayHeading', () => {
  it.each([
    ['2026-03-15', '2026-03-15', 'Hoje'],
    ['2026-03-14', '2026-03-15', 'Ontem'],
    ['2025-12-31', '2026-01-01', 'Ontem'],
    ['2026-03-10', '2026-03-15', 'Terça-feira, 10 de março'],
    ['2026-03-01', '2026-03-15', 'Domingo, 1 de março'],
    ['2025-12-30', '2026-01-01', 'Terça-feira, 30 de dezembro de 2025'],
  ])('%s com hoje = %s → %s', (date, today, expected) => {
    expect(formatDayHeading(date, today)).toBe(expected)
  })

  // DateOnly da API ("2026-03-10") não tem fuso: o dia não pode mudar com o fuso do aparelho.
  describe('independe do fuso do aparelho', () => {
    afterEach(() => {
      vi.unstubAllEnvs()
    })

    it.each(['America/Los_Angeles', 'Asia/Tokyo', 'UTC'])('%s', (zone) => {
      vi.stubEnv('TZ', zone)
      expect(formatDayHeading('2026-03-10', '2026-03-15')).toBe('Terça-feira, 10 de março')
    })
  })
})

describe('meses', () => {
  it('monthOf lê ano e mês de uma data', () => {
    expect(monthOf('2026-03-10')).toEqual({ year: 2026, month: 3 })
  })

  it.each([
    [{ year: 2026, month: 3 }, { from: '2026-03-01', to: '2026-03-31' }],
    [{ year: 2026, month: 4 }, { from: '2026-04-01', to: '2026-04-30' }],
    [{ year: 2026, month: 2 }, { from: '2026-02-01', to: '2026-02-28' }],
    [{ year: 2028, month: 2 }, { from: '2028-02-01', to: '2028-02-29' }],
  ])('monthRange(%o) = %o', (month, expected) => {
    expect(monthRange(month)).toEqual(expected)
  })

  it('shiftMonth atravessa o ano', () => {
    expect(shiftMonth({ year: 2026, month: 12 }, 1)).toEqual({ year: 2027, month: 1 })
    expect(shiftMonth({ year: 2026, month: 1 }, -1)).toEqual({ year: 2025, month: 12 })
    expect(shiftMonth({ year: 2026, month: 3 }, 0)).toEqual({ year: 2026, month: 3 })
  })

  it('formatMonth escreve o mês por extenso', () => {
    expect(formatMonth({ year: 2026, month: 3 })).toBe('Março de 2026')
  })
})
