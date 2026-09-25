export type ExpenseChange = { percent: number; direction: 'more' | 'less' | 'same' }

// Variação das despesas contra o mês anterior, em % inteiro (docs/fase-2.md, 2.3). Sem despesa no
// mês anterior não há base de comparação: devolve null e a tela não mostra porcentagem.
export function expenseChange(currentCents: number, previousCents: number): ExpenseChange | null {
  if (previousCents <= 0) return null
  const percent = Math.round((Math.abs(currentCents - previousCents) / previousCents) * 100)
  if (percent === 0) return { percent: 0, direction: 'same' }
  return { percent, direction: currentCents > previousCents ? 'more' : 'less' }
}

export function describeExpenseChange(change: ExpenseChange, previousMonthName: string): string {
  if (change.direction === 'same') return `Gastou o mesmo que em ${previousMonthName}`
  return `Gastou ${change.percent}% a ${change.direction === 'more' ? 'mais' : 'menos'} que em ${previousMonthName}`
}
