import { addDays } from '@/lib/dates'
import { formatCents } from '@/lib/money'
import { describeSeries, shortDate } from './schedule'

// Débito automático (docs/fase-2.md, 2.15): o que a tela diz. O dia útil do débito é decidido pela API
// (calendário bancário); a tela só mostra o vencimento e as datas que a API devolve.

export { canAutoDebit } from './repeatChoice'

// O vencimento da primeira ocorrência: a data do lançamento ou até 7 dias antes (regra 4), quando o débito
// já caiu adiado por fim de semana ou feriado.
export const maxDueDaysBefore = 7

const weekdays = ['dom', 'seg', 'ter', 'qua', 'qui', 'sex', 'sáb']

export function dueDateChoices(start: string): { daysBefore: number; date: string; label: string }[] {
  return Array.from({ length: maxDueDaysBefore + 1 }, (_, daysBefore) => {
    const date = addDays(start, -daysBefore)
    const [year, month, day] = date.split('-').map(Number)
    const weekday = weekdays[new Date(Date.UTC(year, month - 1, day)).getUTCDay()]
    return { daysBefore, date, label: `${weekday} ${date.slice(8, 10)}/${date.slice(5, 7)}` }
  })
}

// "Débito automático · vence todo dia 20 · próximo vencimento 20/10".
export function autoDebitLine({ due, endDate, today }: { due: string; endDate: string | null; today: string }): string {
  const { next } = describeSeries({ start: due, frequency: 'Monthly', endDate, today })
  return [
    `Débito automático · vence todo dia ${Number(due.slice(8, 10))}`,
    next && `próximo vencimento ${shortDate(next, today)}`,
    endDate && `até ${shortDate(endDate, today)}`,
  ]
    .filter(Boolean)
    .join(' · ')
}

// O sino (D4): conta o que falta conferir.
export function bellLabel(count: number): string {
  if (count === 0) return 'Nada para conferir'
  return count === 1 ? '1 débito para conferir' : `${count} débitos para conferir`
}

// "Saiu em 16/11 · Itaú": quando e de onde o dinheiro saiu (o painel já diz que são débitos automáticos).
export function debitDetails(transaction: { purchaseDate: string }, account: string | undefined, today: string): string {
  return [`Saiu em ${shortDate(transaction.purchaseDate, today)}`, account].filter(Boolean).join(' · ')
}

export function confirmedMessage(description: string, cents: number): string {
  return `${description || 'Débito'} conferido: ${formatCents(cents)}.`
}

// Resumo: quanto do "Saiu" ainda é estimativa (regra 8).
export const estimatedNote = (cents: number) => `≈ ${formatCents(cents)} a conferir`
