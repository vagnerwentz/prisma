import { addDays, formatShortDate } from '@/lib/dates'

type AccountLike = { id: string; type: string; isActive: boolean }
type BalanceLike = { balanceCents: number | null }

// "Em contas" (docs/fase-2.md, 2.4): saldo atual das contas ativas que não são cartão. Sem nenhuma
// dessas contas, null: o bloco não aparece.
export function balanceInAccounts(
  accounts: AccountLike[],
  balances: Map<string, BalanceLike>,
): { cents: number; count: number } | null {
  const counted = accounts.filter((a) => a.isActive && a.type !== 'CreditCard')
  if (counted.length === 0) return null
  const cents = counted.reduce((sum, a) => sum + (balances.get(a.id)?.balanceCents ?? 0), 0)
  return { cents, count: counted.length }
}

export function describeDue(dueDate: string, today: string): string {
  if (dueDate === today) return 'Vence hoje'
  if (dueDate === addDays(today, 1)) return 'Vence amanhã'
  const date = formatShortDate(dueDate)
  return `Vence ${dueDate.slice(0, 4) === today.slice(0, 4) ? date.slice(0, 5) : date}`
}
