import { shiftReference, statementMonth } from '@/features/transactions/statementMove'
import type { Recurrence } from './queries'

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

// A linha da aba Contas: "2 ativas · 1 pendente".
export function seriesCount(recurrences: Recurrence[]): string | null {
  if (recurrences.length === 0) return null
  const active = recurrences.filter((r) => !r.isEnded).length
  const pendings = recurrences.reduce((sum, r) => sum + r.pendings.length, 0)
  const parts = [
    active === 0 ? 'Todas encerradas' : `${active} ${active === 1 ? 'ativa' : 'ativas'}`,
    pendings > 0 && `${pendings} ${pendings === 1 ? 'pendente' : 'pendentes'}`,
  ]
  return parts.filter(Boolean).join(' · ')
}

export function groupSeries(recurrences: Recurrence[]) {
  const byNext = (a: Recurrence, b: Recurrence) => (a.nextOccurrence ?? '').localeCompare(b.nextOccurrence ?? '')
  return {
    pendings: recurrences.flatMap((series) =>
      [...series.pendings]
        .sort((a, b) => a.occurrenceDate.localeCompare(b.occurrenceDate))
        .map((pending) => ({ series, pending })),
    ),
    active: recurrences.filter((r) => !r.isEnded).sort(byNext),
    ended: recurrences.filter((r) => r.isEnded).sort((a, b) => b.generatedThrough.localeCompare(a.generatedThrough)),
  }
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
