import { describe, expect, it } from 'vitest'
import {
  activeLabel,
  inactiveLabel,
  maxDays,
  parseDays,
  peopleCount,
  quickDays,
  reliabilityNote,
  type UserSummary,
} from './adminPeople'

// docs/admin-email.md, 3.1 e 3.2 (AE.3): o que a tela de Pessoas diz e como valida o campo de dias. Os limites são os mesmos de
// ActivityWindow no domínio (1 a 365); o servidor continua sendo quem decide.
describe('parseDays', () => {
  it('accepts whole numbers from 1 to 365', () => {
    expect(parseDays('1')).toEqual({ days: 1 })
    expect(parseDays('30')).toEqual({ days: 30 })
    expect(parseDays('365')).toEqual({ days: 365 })
  })

  it('ignores spaces around the number', () => {
    expect(parseDays('  45 ')).toEqual({ days: 45 })
  })

  it.each(['0', '366', '-5', '3.5', '3,5', 'abc', '', '   ', '1e2', '+7', '0030x'])('refuses %j with the range in the message', (text) => {
    const result = parseDays(text)
    expect(result).toHaveProperty('problem')
    expect((result as { problem: string }).problem).toContain(`${maxDays}`)
  })

  it('keeps the range of the server', () => {
    expect(maxDays).toBe(365)
  })
})

describe('shortcuts', () => {
  it('are 7, 30 and 90 days, all valid', () => {
    expect(quickDays).toEqual([7, 30, 90])
    for (const days of quickDays) expect(parseDays(String(days))).toEqual({ days })
  })
})

describe('labels', () => {
  it('says "dia" for one day and "dias" for the others', () => {
    expect(inactiveLabel(1)).toBe('Sem acesso há 1 dia ou mais')
    expect(inactiveLabel(30)).toBe('Sem acesso há 30 dias ou mais')
  })

  it('counts today as the first day of the window', () => {
    expect(activeLabel(1)).toBe('Acessaram hoje')
    expect(activeLabel(7)).toBe('Acessaram nos últimos 7 dias')
  })

  it('counts people with the right plural and thousands separator', () => {
    expect(peopleCount(0)).toBe('0 pessoas')
    expect(peopleCount(1)).toBe('1 pessoa')
    expect(peopleCount(2)).toBe('2 pessoas')
    expect(peopleCount(1234)).toBe('1.234 pessoas')
  })
})

describe('reliabilityNote', () => {
  it('explains that old accounts carry the date of the update and for how long to wait', () => {
    const note = reliabilityNote(30)
    expect(note).toContain('30 dias')
    expect(note).toContain('antes do registro de acesso')
  })
})

describe('the summary type', () => {
  it('has only numbers, as the API answers', () => {
    const summary: UserSummary = { days: 30, accounts: 10, confirmed: 8, unconfirmed: 2, active: 5, inactive: 2, neverUsed: 1 }
    expect(Object.values(summary).every((v) => typeof v === 'number')).toBe(true)
    expect(summary.active + summary.inactive + summary.neverUsed).toBe(summary.confirmed)
  })
})
