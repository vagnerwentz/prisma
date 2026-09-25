import { formatCents } from '@/lib/money'

// Participação de uma categoria nas despesas do mês, em % inteiro (docs/fase-2.md, 2.2). Quem
// gastou algo nunca aparece com 0%: arredondar R$ 0,01 de R$ 4.000,00 para zero esconderia o gasto.
export function shareOf(amountCents: number, totalCents: number): number {
  if (totalCents <= 0 || amountCents <= 0) return 0
  return Math.max(1, Math.round((amountCents / totalCents) * 100))
}

// Aviso da lista quando um estorno deixou categorias zeradas ou negativas, que somem (docs/fase-2.md,
// 2.5, regra 12): as exibidas somam mais que o "Saiu", e a diferença é este valor.
export function hiddenRefundsNotice(hiddenRefundCents: number): string | null {
  return hiddenRefundCents > 0 ? `${formatCents(hiddenRefundCents)} de estornos em categorias sem gastos no mês` : null
}
