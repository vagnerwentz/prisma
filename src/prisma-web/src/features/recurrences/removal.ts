import type { Schemas } from '@/lib/api'
import type { Recurrence } from './queries'
import { shortDate } from './schedule'

// Excluir o lançamento de uma série ativa (docs/fase-2.md, 2.14, Tela): os avisos de cada escolha.
// summary: "Causa animal · R$ 100,00", o que foi excluído.
export function removalTexts({
  series,
  endSeries,
  summary,
  today,
}: {
  series: Recurrence
  endSeries: boolean
  summary: string
  today: string
}): { title: string; description: string; undone: string } {
  if (endSeries)
    return {
      title: 'Lançamento excluído e série encerrada',
      description: summary,
      undone: 'Lançamento restaurado e série reaberta',
    }
  return {
    title: 'Lançamento excluído',
    description: series.nextOccurrence ? `A série continua: próxima ${shortDate(series.nextOccurrence, today)}.` : summary,
    undone: 'Lançamento restaurado',
  }
}

// Desfazer o encerramento: a série como estava, com o término de antes (nenhum, ou a data que tinha).
export function reopenRequest(series: Recurrence): Schemas['UpdateRecurrenceRequest'] {
  return {
    accountId: series.accountId,
    amountCents: series.amountCents,
    categoryId: series.categoryId,
    description: series.description,
    method: series.method,
    frequency: series.frequency,
    nextDate: null,
    endDate: series.endDate,
  }
}
