import type { Schemas } from '@/lib/api'
import { formatShortDate } from '@/lib/dates'

const monthName = new Intl.DateTimeFormat('pt-BR', { timeZone: 'UTC', month: 'long' })

// Mover uma compra no cartão para a fatura seguinte ou anterior (docs/fase-2.md, 2.9, regras 2 e 3).
// O painel só oferece o que a API aceitaria: compra no cartão (não estorno), sem parcela em fatura
// paga, e só a direção cujo destino não está pago. A fatura de destino que ainda não existe é aberta
// pela API.
export type StatementShift = Schemas['StatementShift']
export type MovableTransaction = Pick<Schemas['TransactionResponse'], 'type' | 'statementId' | 'statementPinned'>
export type StatementInfo = Pick<Schemas['ListStatementsStatementResponse'], 'id' | 'reference' | 'dueDate' | 'isPaid'>

export type MoveOptions = {
  // A fatura da compra (na parcelada, a da primeira parcela).
  current: StatementInfo
  // Movida à mão: o Prisma não a devolve sozinho.
  pinned: boolean
  shifts: StatementShift[]
}

// "2026-11", +1 → "2026-12".
export function shiftReference(reference: string, months: number): string {
  const [year, month] = reference.split('-').map(Number)
  const index = year * 12 + (month - 1) + months
  return `${Math.floor(index / 12)}-${String((index % 12) + 1).padStart(2, '0')}`
}

// transactions: a compra à vista, ou as parcelas em ordem. statements: as faturas do cartão.
export function moveOptions(
  transactions: MovableTransaction[],
  statements: StatementInfo[] | undefined,
): MoveOptions | null {
  if (transactions.length === 0 || !statements) return null
  if (transactions.some((t) => t.type !== 'Expense' || !t.statementId)) return null

  const byId = new Map(statements.map((s) => [s.id, s]))
  const own = transactions.map((t) => byId.get(t.statementId!))
  if (own.some((s) => s === undefined)) return null
  const held = own as StatementInfo[]

  const byReference = new Map(statements.map((s) => [s.reference, s]))
  const canGo = (months: number) =>
    held.every((s) => !s.isPaid && !byReference.get(shiftReference(s.reference, months))?.isPaid)

  return {
    current: held[0],
    pinned: transactions.some((t) => t.statementPinned),
    shifts: (['Previous', 'Next'] as const).filter((shift) => canGo(shift === 'Next' ? 1 : -1)),
  }
}

const monthOf = (reference: string) => monthName.format(new Date(`${reference.slice(0, 7)}-01T12:00:00Z`))

// Na lista de lançamentos (pela data da compra), a compra movida diz em que fatura está: é por isso que
// ela conta no "Saiu" de outro mês. A fatura é a do mês em que vence.
export function movedLabel(transaction: Pick<Schemas['TransactionResponse'], 'statementPinned' | 'settlementDate'>) {
  return transaction.statementPinned ? `fatura de ${monthOf(transaction.settlementDate)}` : null
}

export const moveTexts = {
  // Verbo + mês de destino: "Próxima fatura" com seta lia como navegar, não como mover.
  button: (destination: string) => `Mover para ${monthOf(destination)}`,
  heading: (reference: string) => `Na fatura de ${monthOf(reference)}`,
  moved(destination: Pick<StatementInfo, 'reference' | 'dueDate'>, installments: number) {
    // O ano vai no vencimento, para o título caber numa linha.
    const month = monthOf(destination.reference)
    const due = `Vence em ${formatShortDate(destination.dueDate)}.`
    return {
      title: `Movida para a fatura de ${month}`,
      description: installments > 1 ? `${due} As ${installments} parcelas andaram uma fatura.` : due,
    }
  },
  undone: 'A compra voltou para a fatura de antes',
  pinned: 'Você moveu esta compra para esta fatura.',
}
