import { describe, expect, it } from 'vitest'
import { describeDue } from './upcoming'

describe('describeDue', () => {
  it('says today and tomorrow, otherwise the date', () => {
    expect(describeDue('2026-10-15', '2026-10-15')).toBe('Vence hoje')
    expect(describeDue('2026-10-16', '2026-10-15')).toBe('Vence amanhã')
    expect(describeDue('2026-11-05', '2026-10-15')).toBe('Vence 05/11')
  })

  it('shows the year when it is not this year', () => {
    expect(describeDue('2027-01-05', '2026-12-20')).toBe('Vence 05/01/2027')
  })
})
