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

export type AccountsSplit = {
  availableCents: number
  availableCount: number
  investedCents: number
  investedCount: number
  yieldCents: number
  totalCents: number
}

// "Em contas" separado em Disponível (corrente e carteira) e Investido (contas de investimento, com o rendimento bruto da renda fixa
// que entrou por cada uma: `yields` vem de `byAccount`, docs/renda-fixa.md, D20). O saldo da conta não muda: o rendimento só soma aqui.
// Sem nenhuma dessas contas, null.
export function splitBalances(
  accounts: AccountLike[],
  balances: Map<string, BalanceLike>,
  yields: { accountId: string; grossYieldCents: number }[],
): AccountsSplit | null {
  const counted = accounts.filter((a) => a.isActive && a.type !== 'CreditCard')
  if (counted.length === 0) return null
  const yieldOf = new Map(yields.map((y) => [y.accountId, y.grossYieldCents]))
  const split: AccountsSplit = {
    availableCents: 0,
    availableCount: 0,
    investedCents: 0,
    investedCount: 0,
    yieldCents: 0,
    totalCents: 0,
  }
  for (const a of counted) {
    const cents = balances.get(a.id)?.balanceCents ?? 0
    if (a.type === 'Investment') {
      const earned = yieldOf.get(a.id) ?? 0
      split.investedCents += cents + earned
      split.yieldCents += earned
      split.investedCount++
    } else {
      split.availableCents += cents
      split.availableCount++
    }
  }
  split.totalCents = split.availableCents + split.investedCents
  return split
}

export function describeDue(dueDate: string, today: string): string {
  if (dueDate === today) return 'Vence hoje'
  if (dueDate === addDays(today, 1)) return 'Vence amanhã'
  const date = formatShortDate(dueDate)
  return `Vence ${dueDate.slice(0, 4) === today.slice(0, 4) ? date.slice(0, 5) : date}`
}
