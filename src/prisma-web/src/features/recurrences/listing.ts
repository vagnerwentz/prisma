import { shiftReference, statementMonth } from '@/features/transactions/statementMove'
import type { Recurrence } from './queries'
import { frequencyText, shortDate } from './schedule'

// A tela "Recorrências" (docs/fase-2.md, 2.14, Tela): pendências no topo, ativas pela próxima data e
// encerradas no fim.

export type Pending = Recurrence['pendings'][number]

// A cobrança que cairia numa fatura já paga (regra 9) e as duas saídas que lançam. Verbo + destino.
export function pendingTexts(pending: Pick<Pending, 'statementReference'>) {
  const paid = statementMonth(pending.statementReference)
  const next = statementMonth(shiftReference(pending.statementReference, 1))
  return {
    reason: `Cairia na fatura de ${paid}, já paga.`,
    next: `Lançar em ${next}`,
    same: `Lançar em ${paid}`,
    sameHint: `Para lançar em ${paid}, desfaça antes o pagamento da fatura.`,
    launchedNext: `Lançada na fatura de ${next}`,
    launchedSame: `Lançada na fatura de ${paid}`,
  }
}

const isAutoDebit = (r: Recurrence) => r.kind === 'AutoDebit'

// A linha da aba Contas: "2 ativas · 1 em débito automático · 1 pendente".
export function seriesCount(recurrences: Recurrence[]): string | null {
  if (recurrences.length === 0) return null
  const active = recurrences.filter((r) => !r.isEnded)
  const autoDebits = active.filter(isAutoDebit).length
  const pendings = recurrences.reduce((sum, r) => sum + r.pendings.length, 0)
  const parts = [
    active.length === 0 ? 'Todas encerradas' : `${active.length} ${active.length === 1 ? 'ativa' : 'ativas'}`,
    autoDebits > 0 && `${autoDebits} em débito automático`,
    pendings > 0 && `${pendings} ${pendings === 1 ? 'pendente' : 'pendentes'}`,
  ]
  return parts.filter(Boolean).join(' · ')
}

// Os débitos automáticos ativos ficam numa seção própria (docs/fase-2.md, 2.15, Tela); encerrados, vão com os
// outros. Pelo próximo vencimento: a ordem é a mesma do próximo débito, porque o dia útil nunca inverte dois
// vencimentos (BankCalendar, propriedade "nunca volta no tempo").
export function groupSeries(recurrences: Recurrence[]) {
  const byNext = (a: Recurrence, b: Recurrence) => (a.nextOccurrence ?? '').localeCompare(b.nextOccurrence ?? '')
  return {
    pendings: recurrences.flatMap((series) =>
      [...series.pendings]
        .sort((a, b) => a.occurrenceDate.localeCompare(b.occurrenceDate))
        .map((pending) => ({ series, pending })),
    ),
    autoDebits: recurrences.filter((r) => !r.isEnded && isAutoDebit(r)).sort(byNext),
    active: recurrences.filter((r) => !r.isEnded && !isAutoDebit(r)).sort(byNext),
    ended: recurrences.filter((r) => r.isEnded).sort((a, b) => b.generatedThrough.localeCompare(a.generatedThrough)),
  }
}

// A segunda linha de uma série: "Todo mês, no dia 25 · próxima 25/11"; no débito automático, o vencimento e o
// próximo débito, já no dia útil (a data vem da API): "Vence dia 20 · próximo débito 23/11".
export function seriesWhen(series: Recurrence, today: string): string {
  if (series.isEnded) return `Encerrada · última ${shortDate(series.generatedThrough, today)}`
  if (isAutoDebit(series))
    return `Vence dia ${Number(series.startDate.slice(8, 10))} · próximo débito ${shortDate(series.nextTransactionDate!, today)}`
  return `${frequencyText(series.startDate, series.frequency)} · próxima ${shortDate(series.nextOccurrence!, today)}`
}

// Excluir a conta encerra as séries ativas dela (docs/fase-2.md, 2.14, decisão de 2026-09-29): o aviso da
// confirmação e o do fim. Nulo quando a conta não tem série ativa.
export function accountSeries(recurrences: Recurrence[], accountId: string) {
  const count = recurrences.filter((r) => r.accountId === accountId && !r.isEnded).length
  if (count === 0) return null
  return count === 1
    ? { count, warning: 'A recorrência dela será encerrada.', done: '1 recorrência encerrada' }
    : { count, warning: `As ${count} recorrências dela serão encerradas.`, done: `${count} recorrências encerradas` }
}
