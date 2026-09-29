import { formatShortDate } from '@/lib/dates'

// A agenda de um lançamento que se repete, como a do domínio (RecurrenceSchedule, docs/fase-2.md, 2.14,
// regra 3): sempre a partir da partida, nunca da ocorrência anterior; o dia que o mês não tem vira o
// último dia; fim de semana e feriado não mudam a data. Serve só para mostrar a série: quem gera é a API.

export type Frequency = 'Weekly' | 'Monthly'

export type SeriesInput = { start: string; frequency: Frequency; endDate: string | null; today: string }

function parse(date: string) {
  const [year, month, day] = date.split('-').map(Number)
  return { year, month, day }
}

const pad = (n: number) => String(n).padStart(2, '0')

// A ocorrência de número index (0 é a partida).
export function occurrence(start: string, frequency: Frequency, index: number): string {
  const { year, month, day } = parse(start)
  if (frequency === 'Weekly') return new Date(Date.UTC(year, month - 1, day + 7 * index)).toISOString().slice(0, 10)

  const total = year * 12 + (month - 1) + index
  const y = Math.floor(total / 12)
  const m = (total % 12) + 1
  const lastDay = new Date(Date.UTC(y, m, 0)).getUTCDate()
  return `${y}-${pad(m)}-${pad(Math.min(day, lastDay))}`
}

// O que a API gera ao criar a série (as ocorrências depois da partida e até hoje) e a próxima depois
// disso. O término entra inclusive.
export function describeSeries({ start, frequency, endDate, today }: SeriesInput): { dueNow: string[]; next: string | null } {
  const dueNow: string[] = []
  for (let index = 1; ; index++) {
    const date = occurrence(start, frequency, index)
    if (endDate !== null && date > endDate) return { dueNow, next: null }
    if (date > today) return { dueNow, next: date }
    dueNow.push(date)
  }
}

const weekdays = ['aos domingos', 'às segundas', 'às terças', 'às quartas', 'às quintas', 'às sextas', 'aos sábados']

// "Todo mês, no dia 25" ou "Toda semana, às sextas".
export function frequencyText(start: string, frequency: Frequency): string {
  const { year, month, day } = parse(start)
  if (frequency === 'Monthly') return `Todo mês, no dia ${day}`
  return `Toda semana, ${weekdays[new Date(Date.UTC(year, month - 1, day)).getUTCDay()]}`
}

// "25/10", ou "25/01/2027" fora do ano de hoje.
export function shortDate(date: string, today: string): string {
  return date.slice(0, 4) === today.slice(0, 4) ? formatShortDate(date).slice(0, 5) : formatShortDate(date)
}

// A linha da série: "Todo mês, no dia 25 · próxima 25/10 · até 30/11".
export function seriesLine(input: SeriesInput): string {
  const { next } = describeSeries(input)
  return [
    frequencyText(input.start, input.frequency),
    next && `próxima ${shortDate(next, input.today)}`,
    input.endDate && `até ${shortDate(input.endDate, input.today)}`,
  ]
    .filter(Boolean)
    .join(' · ')
}
