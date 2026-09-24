import { formatMonth } from '@/lib/dates'

export type StatementLike = {
  id: string
  reference: string
  closingDate: string
  dueDate: string
  isPaid: boolean
  datesEditedManually: boolean
  totalCents: number
}

// Faturas de um cartão para a tela: a atual em destaque, as próximas e as anteriores.
// Atual é a primeira fatura não paga que fecha hoje ou depois: a compra no dia do fechamento
// ainda entra nela (docs/fase-1.md, regra 1 do calculador). Faturas vazias (abertas por uma
// edição, por exemplo) ficam escondidas, salvo a atual e as que tiveram as datas ajustadas.
export function groupStatements<T extends StatementLike>(statements: T[], today: string) {
  const byDue = [...statements].sort((a, b) => a.dueDate.localeCompare(b.dueDate))
  const current = byDue.find((s) => !s.isPaid && s.closingDate >= today)
  const visible = (s: T) => s.totalCents !== 0 || s.datesEditedManually

  const upcoming = byDue.filter((s) => s !== current && !s.isPaid && s.closingDate >= today && visible(s))
  const past = byDue.filter((s) => s !== current && (s.isPaid || s.closingDate < today) && visible(s)).reverse()
  return { current, upcoming, past }
}

export type StatementStatus = 'Aberta' | 'Fechada' | 'Paga' | 'Futura'

// Sem pagamento de fatura (etapa 1.10), fatura fechada não vira "vencida": seria alarme falso.
export function statementStatus(statement: StatementLike, today: string, currentId: string | undefined): StatementStatus {
  if (statement.isPaid) return 'Paga'
  if (statement.closingDate < today) return 'Fechada'
  return statement.id === currentId ? 'Aberta' : 'Futura'
}

// A referência é o mês do vencimento: "2026-10" → "Outubro de 2026".
export function statementTitle(reference: string): string {
  const [year, month] = reference.split('-').map(Number)
  return formatMonth({ year, month })
}
