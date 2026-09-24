import { ChevronLeft, ChevronRight } from 'lucide-react'
import { useMemo, useState } from 'react'
import { Alert, AlertDescription } from '@/components/ui/alert'
import { Button } from '@/components/ui/button'
import { useAccounts } from '@/features/accounts/queries'
import { categoryLabels, useCategories, type CategoryLabel } from '@/features/categories/queries'
import { formatDayHeading, formatMonth, monthOf, monthRange, shiftMonth, todayInSaoPaulo } from '@/lib/dates'
import { formatCents } from '@/lib/money'
import { cn } from '@/lib/utils'
import { useTransactions, type Transaction } from './queries'
import { buildTimeline, type TimelineEntry } from './timeline'

type Lookups = { accounts: Map<string, string>; categories: Map<string, CategoryLabel> }

export function TransactionsPage() {
  const today = todayInSaoPaulo()
  const [month, setMonth] = useState(() => monthOf(today))
  const range = monthRange(month)

  const transactions = useTransactions(range)
  const accounts = useAccounts()
  const categories = useCategories()

  const lookups = useMemo<Lookups>(
    () => ({
      accounts: new Map((accounts.data ?? []).map((a) => [a.id, a.name])),
      categories: categoryLabels(categories.data ?? []),
    }),
    [accounts.data, categories.data],
  )
  const days = useMemo(() => buildTimeline(transactions.data ?? []), [transactions.data])

  return (
    <main className="mx-auto flex max-w-2xl flex-col gap-4 p-4 pb-28">
      <div className="flex items-center justify-between">
        <Button variant="ghost" size="icon" aria-label="Mês anterior" onClick={() => setMonth((m) => shiftMonth(m, -1))}>
          <ChevronLeft />
        </Button>
        <h1 className="text-base font-semibold">{formatMonth(month)}</h1>
        <Button variant="ghost" size="icon" aria-label="Próximo mês" onClick={() => setMonth((m) => shiftMonth(m, 1))}>
          <ChevronRight />
        </Button>
      </div>

      {transactions.isPending && <p className="py-8 text-center text-muted-foreground">Carregando…</p>}

      {transactions.isError && (
        <Alert variant="destructive">
          <AlertDescription>Não foi possível carregar os lançamentos. Tente novamente.</AlertDescription>
        </Alert>
      )}

      {transactions.isSuccess && days.length === 0 && (
        <p className="py-8 text-center text-muted-foreground">Nenhum lançamento em {formatMonth(month).toLowerCase()}.</p>
      )}

      {days.map((day) => (
        <section key={day.date} className="flex flex-col gap-2">
          <h2 className="px-1 text-sm font-medium text-muted-foreground">{formatDayHeading(day.date, today)}</h2>
          <ul className="divide-y overflow-hidden rounded-xl border bg-background">
            {day.entries.map((entry) => (
              <EntryRow key={entry.kind === 'single' ? entry.transaction.id : entry.purchaseId} entry={entry} lookups={lookups} />
            ))}
          </ul>
        </section>
      ))}
    </main>
  )
}

function EntryRow({ entry, lookups }: { entry: TimelineEntry<Transaction>; lookups: Lookups }) {
  const first = entry.kind === 'single' ? entry.transaction : entry.installments[0]
  const category = first.categoryId ? lookups.categories.get(first.categoryId) : undefined
  const account = lookups.accounts.get(first.accountId)
  const amount = entry.kind === 'single' ? first.amountCents : entry.totalCents

  const title = first.description || category?.name || 'Sem descrição'
  const details = [
    entry.kind === 'purchase' ? `Parcelado em ${entry.installments.length}x` : null,
    category && category.name !== title ? category.name : null,
    account,
  ].filter(Boolean)

  return (
    <li className="flex items-center justify-between gap-3 px-4 py-3">
      <div className="min-w-0">
        <p className="truncate font-medium">{title}</p>
        {details.length > 0 && <p className="truncate text-sm text-muted-foreground">{details.join(' · ')}</p>}
      </div>
      <Amount type={first.type} cents={amount} />
    </li>
  )
}

function Amount({ type, cents }: { type: Transaction['type']; cents: number }) {
  // AmountCents é sempre positivo; o tipo define o sinal (docs/fase-1.md).
  const text = type === 'Income' ? `+${formatCents(cents)}` : type === 'Expense' ? formatCents(-cents) : formatCents(cents)
  return (
    <span className={cn('shrink-0 font-semibold tabular-nums', type === 'Income' && 'text-emerald-600')}>{text}</span>
  )
}
