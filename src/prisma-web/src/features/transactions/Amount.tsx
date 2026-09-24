import { formatCents } from '@/lib/money'
import { cn } from '@/lib/utils'
import type { Transaction } from './queries'

// AmountCents é sempre positivo; o tipo define o sinal (docs/fase-1.md). Receita é "luz
// entrando" (gradiente espectral); despesa é tinta, sem vermelho alarmista.
export function Amount({ type, cents, className }: { type: Transaction['type']; cents: number; className?: string }) {
  if (type === 'Income') {
    return <span className={cn('text-spectrum shrink-0 font-semibold tabular-nums', className)}>+{formatCents(cents)}</span>
  }
  const text = type === 'Expense' ? formatCents(-cents).replace('-', '−') : formatCents(cents)
  return <span className={cn('shrink-0 font-medium tabular-nums', className)}>{text}</span>
}
