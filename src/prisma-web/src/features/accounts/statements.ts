import { formatMonth, formatShortDate } from '@/lib/dates'
import { formatCents } from '@/lib/money'

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

export type StatementStatus = 'Aberta' | 'Fechada' | 'Paga' | 'Futura' | 'Saldo a favor'

// Sem pagamento de fatura (etapa 1.10), fatura fechada não vira "vencida": seria alarme falso.
// Fechada com total negativo (estorno maior que as compras) não tem o que pagar: é saldo a favor
// (docs/fase-2.md, 2.5, regra 9).
export function statementStatus(statement: StatementLike, today: string, currentId: string | undefined): StatementStatus {
  if (statement.isPaid) return 'Paga'
  if (statement.closingDate < today) return statement.totalCents < 0 ? 'Saldo a favor' : 'Fechada'
  return statement.id === currentId ? 'Aberta' : 'Futura'
}

// Total da fatura para exibir: negativo vira "Saldo a favor", sem sinal.
export function statementTotal(totalCents: number): { label: string | null; text: string } {
  return totalCents < 0
    ? { label: 'Saldo a favor', text: formatCents(-totalCents) }
    : { label: null, text: formatCents(totalCents) }
}

// A referência é o mês do vencimento: "2026-10" → "Outubro de 2026".
export function statementTitle(reference: string): string {
  const [year, month] = reference.split('-').map(Number)
  return formatMonth({ year, month })
}

// Ajustar datas da fatura (docs/fase-2.md, 2.9, tarefa 8). O aviso traz o vencimento e quantas compras
// mudaram de fatura (a parcelada conta uma), para um giro a mais na roda de data saltar aos olhos.
export const dateEditTexts = {
  edited(dueDate: string, movedPurchases: number) {
    const moved =
      movedPurchases === 1
        ? ' 1 compra mudou de fatura.'
        : movedPurchases > 1
          ? ` ${movedPurchases} compras mudaram de fatura.`
          : ''
    return { title: 'Datas da fatura ajustadas', description: `Vence em ${formatShortDate(dueDate)}.${moved}` }
  },
  undone: 'Datas de antes de volta',
  help:
    'Use quando o banco muda o fechamento ou o vencimento. Compras feitas perto do fechamento podem entrar ou sair ' +
    'desta fatura. As que você moveu à mão ficam onde estão.',
}
