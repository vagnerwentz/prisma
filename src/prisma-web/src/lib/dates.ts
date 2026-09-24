// Datas de negócio são DateOnly ("2026-03-10"), sem fuso. Para não mudar de dia com o fuso do
// aparelho, toda conta é feita em UTC; só "hoje" usa o fuso de São Paulo (CLAUDE.md, regra 3).
export type YearMonth = { year: number; month: number }

const saoPauloDate = new Intl.DateTimeFormat('en-CA', {
  timeZone: 'America/Sao_Paulo',
  year: 'numeric',
  month: '2-digit',
  day: '2-digit',
})

export function todayInSaoPaulo(now: Date = new Date()): string {
  const parts = Object.fromEntries(saoPauloDate.formatToParts(now).map((p) => [p.type, p.value]))
  return `${parts.year}-${parts.month}-${parts.day}`
}

function parse(date: string): { year: number; month: number; day: number } {
  const [year, month, day] = date.split('-').map(Number)
  return { year, month, day }
}

function toUtc(date: string): Date {
  const { year, month, day } = parse(date)
  return new Date(Date.UTC(year, month - 1, day))
}

function toIso(utc: Date): string {
  return utc.toISOString().slice(0, 10)
}

export function addDays(date: string, days: number): string {
  const utc = toUtc(date)
  utc.setUTCDate(utc.getUTCDate() + days)
  return toIso(utc)
}

const capitalize = (text: string) => text.charAt(0).toUpperCase() + text.slice(1)

const dayHeading = new Intl.DateTimeFormat('pt-BR', { timeZone: 'UTC', weekday: 'long', day: 'numeric', month: 'long' })
const dayHeadingWithYear = new Intl.DateTimeFormat('pt-BR', {
  timeZone: 'UTC',
  weekday: 'long',
  day: 'numeric',
  month: 'long',
  year: 'numeric',
})

export function formatDayHeading(date: string, today: string): string {
  if (date === today) return 'Hoje'
  if (date === addDays(today, -1)) return 'Ontem'
  const formatter = parse(date).year === parse(today).year ? dayHeading : dayHeadingWithYear
  return capitalize(formatter.format(toUtc(date)))
}

export function monthOf(date: string): YearMonth {
  const { year, month } = parse(date)
  return { year, month }
}

export function monthRange({ year, month }: YearMonth): { from: string; to: string } {
  const lastDay = new Date(Date.UTC(year, month, 0)).getUTCDate()
  const pad = (n: number) => String(n).padStart(2, '0')
  return { from: `${year}-${pad(month)}-01`, to: `${year}-${pad(month)}-${pad(lastDay)}` }
}

export function shiftMonth({ year, month }: YearMonth, delta: number): YearMonth {
  const index = year * 12 + (month - 1) + delta
  return { year: Math.floor(index / 12), month: (index % 12) + 1 }
}

const monthName = new Intl.DateTimeFormat('pt-BR', { timeZone: 'UTC', month: 'long', year: 'numeric' })

export function formatMonth({ year, month }: YearMonth): string {
  return capitalize(monthName.format(new Date(Date.UTC(year, month - 1, 1))))
}
