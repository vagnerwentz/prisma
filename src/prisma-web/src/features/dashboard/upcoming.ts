import { addDays, formatShortDate } from '@/lib/dates'

export function describeDue(dueDate: string, today: string): string {
  if (dueDate === today) return 'Vence hoje'
  if (dueDate === addDays(today, 1)) return 'Vence amanhã'
  const date = formatShortDate(dueDate)
  return `Vence ${dueDate.slice(0, 4) === today.slice(0, 4) ? date.slice(0, 5) : date}`
}
