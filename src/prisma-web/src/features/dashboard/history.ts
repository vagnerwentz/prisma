export type ExpenseChange = { percent: number; direction: 'more' | 'less' | 'same' }

// Variação das despesas contra o mês anterior, em % inteiro (docs/fase-2.md, 2.3). Sem despesa no
// mês anterior não há base de comparação: devolve null e a tela não mostra porcentagem.
// Estornos podem deixar o mês negativo (docs/fase-2.md, 2.5): conta como zero, no máximo 100% a menos.
export function expenseChange(currentCents: number, previousCents: number): ExpenseChange | null {
  if (previousCents <= 0) return null
  const current = Math.max(currentCents, 0)
  const percent = Math.round((Math.abs(current - previousCents) / previousCents) * 100)
  if (percent === 0) return { percent: 0, direction: 'same' }
  return { percent, direction: current > previousCents ? 'more' : 'less' }
}

export function describeExpenseChange(change: ExpenseChange, previousMonthName: string): string {
  if (change.direction === 'same') return `Gastou o mesmo que em ${previousMonthName}`
  return `Gastou ${change.percent}% a ${change.direction === 'more' ? 'mais' : 'menos'} que em ${previousMonthName}`
}
