// Participação de uma categoria nas despesas do mês, em % inteiro (docs/fase-2.md, 2.2). Quem
// gastou algo nunca aparece com 0%: arredondar R$ 0,01 de R$ 4.000,00 para zero esconderia o gasto.
export function shareOf(amountCents: number, totalCents: number): number {
  if (totalCents <= 0 || amountCents <= 0) return 0
  return Math.max(1, Math.round((amountCents / totalCents) * 100))
}
