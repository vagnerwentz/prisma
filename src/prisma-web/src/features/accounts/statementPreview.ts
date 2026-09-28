import type { Schemas } from '@/lib/api'
import { formatCents } from '@/lib/money'

// Prévia do ajuste de datas (docs/fase-2.md, 2.10): as compras que mudariam de fatura, antes de salvar.
export type PreviewedPurchase = Schemas['PreviewStatementDatesMovedPurchase']

type Dates = { closingDate: string; dueDate: string }

// Só vale perguntar à API quando as datas mudaram e o formulário já as aceitaria (o vencimento antes do
// fechamento a própria tela recusa).
export function shouldPreview(original: Dates, closingDate: string, dueDate: string): boolean {
  if (!closingDate || !dueDate || dueDate < closingDate) return false
  return closingDate !== original.closingDate || dueDate !== original.dueDate
}

const monthName = new Intl.DateTimeFormat('pt-BR', { timeZone: 'UTC', month: 'long' })
const monthOf = (reference: string) => monthName.format(new Date(`${reference}-01T12:00:00Z`))

export const previewTexts = {
  heading: (count: number) =>
    count === 0 ? 'Nenhuma compra muda de fatura.' : count === 1 ? '1 compra muda de fatura' : `${count} compras mudam de fatura`,
  // "27/10 · dezembro → novembro"
  route: (p: Pick<PreviewedPurchase, 'purchaseDate' | 'fromReference' | 'toReference'>) => {
    const [, month, day] = p.purchaseDate.split('-')
    return `${day}/${month} · ${monthOf(p.fromReference)} → ${monthOf(p.toReference)}`
  },
  amount: (p: Pick<PreviewedPurchase, 'type' | 'amountCents' | 'installmentCount'>) => {
    const value = formatCents(p.amountCents)
    if (p.type === 'Refund') return `estorno ${value}`
    return p.installmentCount > 1 ? `${value} em ${p.installmentCount}x` : value
  },
  // Sem prévia ainda (ou nada muda), o botão é o de sempre.
  save: (count: number | undefined) =>
    !count ? 'Salvar datas' : count === 1 ? 'Salvar e mover 1 compra' : `Salvar e mover ${count} compras`,
}
