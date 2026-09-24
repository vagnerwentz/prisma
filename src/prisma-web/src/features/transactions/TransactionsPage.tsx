import { ChevronLeft, ChevronRight } from 'lucide-react'
import { useMemo } from 'react'
import { Link, useSearchParams } from 'react-router'
import { PrismLogo } from '@/components/brand/PrismLogo'
import { EntryTile } from '@/components/brand/Tiles'
import { Alert, AlertDescription } from '@/components/ui/alert'
import { Button } from '@/components/ui/button'
import { Skeleton } from '@/components/ui/skeleton'
import { useAccounts } from '@/features/accounts/queries'
import { categoryLabels, useCategories, type CategoryLabel } from '@/features/categories/queries'
import { formatDayHeading, formatMonth, monthOf, monthRange, shiftMonth, todayInSaoPaulo, type YearMonth } from '@/lib/dates'
import { formatCents, shortInstallments } from '@/lib/money'
import { useTransactions, type Transaction } from './queries'
import { buildTimeline, type TimelineEntry } from './timeline'

type Lookups = { accounts: Map<string, string>; categories: Map<string, CategoryLabel> }

export function TransactionsPage() {
  const today = todayInSaoPaulo()
  // O mês fica na URL (?mes=2025-11): voltar, recarregar e abrir depois de um lançamento
  // antigo mostram o mês certo.
  const [searchParams, setSearchParams] = useSearchParams()
  const month = parseMonthParam(searchParams.get('mes')) ?? monthOf(today)
  const setMonth = (next: YearMonth) => setSearchParams({ mes: toMonthParam(next) }, { replace: true })
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
    <main className="mx-auto flex max-w-2xl flex-col gap-6 px-4 pt-5 pb-32">
      <MonthSwitcher month={month} onChange={setMonth} />

      {transactions.isPending && <ListSkeleton />}

      {transactions.isError && (
        <Alert variant="destructive">
          <AlertDescription>Não foi possível carregar os lançamentos. Tente novamente.</AlertDescription>
        </Alert>
      )}

      {transactions.isSuccess && days.length === 0 && <EmptyMonth month={month} />}

      {days.map((day) => (
        <section key={day.date} className="flex flex-col gap-2">
          <h2 className="px-1 text-xs font-medium tracking-wide text-muted-foreground uppercase">
            {formatDayHeading(day.date, today)}
          </h2>
          <ul className="overflow-hidden rounded-2xl border bg-card shadow-[0_1px_2px_rgb(0_0_0/0.03)]">
            {day.entries.map((entry) => (
              <EntryRow key={entry.kind === 'single' ? entry.transaction.id : entry.purchaseId} entry={entry} lookups={lookups} />
            ))}
          </ul>
        </section>
      ))}
    </main>
  )
}

function toMonthParam({ year, month }: YearMonth): string {
  return `${year}-${String(month).padStart(2, '0')}`
}

function parseMonthParam(value: string | null): YearMonth | null {
  const match = value?.match(/^(\d{4})-(\d{2})$/)
  if (!match) return null
  const month = Number(match[2])
  return month >= 1 && month <= 12 ? { year: Number(match[1]), month } : null
}

function MonthSwitcher({ month, onChange }: { month: YearMonth; onChange: (m: YearMonth) => void }) {
  const [name, year] = formatMonth(month).split(' de ')
  return (
    <div className="flex flex-col gap-3">
      <div className="flex items-end justify-between">
        <h1 className="flex items-baseline gap-2">
          <span className="font-display text-4xl leading-none">{name}</span>
          <span className="text-sm text-muted-foreground tabular-nums">{year}</span>
        </h1>
        <div className="flex gap-1">
          <Button variant="ghost" size="icon" className="rounded-full" aria-label="Mês anterior" onClick={() => onChange(shiftMonth(month, -1))}>
            <ChevronLeft />
          </Button>
          <Button variant="ghost" size="icon" className="rounded-full" aria-label="Próximo mês" onClick={() => onChange(shiftMonth(month, 1))}>
            <ChevronRight />
          </Button>
        </div>
      </div>
      <div className="spectrum-line opacity-80" />
    </div>
  )
}

function EntryRow({ entry, lookups }: { entry: TimelineEntry<Transaction>; lookups: Lookups }) {
  const first = entry.kind === 'single' ? entry.transaction : entry.installments[0]
  const category = first.categoryId ? lookups.categories.get(first.categoryId) : undefined
  const account = lookups.accounts.get(first.accountId)
  const amount = entry.kind === 'single' ? first.amountCents : entry.totalCents
  const installments = entry.kind === 'purchase' ? entry.installments.length : 0

  const title = first.description || category?.name || 'Sem descrição'
  const details = [category && category.name !== title ? category.name : null, account].filter(Boolean)

  return (
    <li className="flex items-center gap-3 px-4 py-3 [&+&]:border-t">
      <EntryTile description={first.description} category={category} />
      <div className="min-w-0 flex-1">
        <p className="truncate font-medium">{title}</p>
        {details.length > 0 && <p className="truncate text-sm text-muted-foreground">{details.join(' · ')}</p>}
      </div>
      <div className="flex shrink-0 flex-col items-end">
        <Amount type={first.type} cents={amount} />
        {installments > 1 && (
          <span className="text-xs text-muted-foreground tabular-nums">{shortInstallments(amount, installments)}</span>
        )}
      </div>
    </li>
  )
}

// AmountCents é sempre positivo; o tipo define o sinal (docs/fase-1.md). Receita é "luz
// entrando" (gradiente espectral); despesa é tinta, sem vermelho alarmista.
function Amount({ type, cents }: { type: Transaction['type']; cents: number }) {
  if (type === 'Income') {
    return <span className="text-spectrum shrink-0 font-semibold tabular-nums">+{formatCents(cents)}</span>
  }
  const text = type === 'Expense' ? formatCents(-cents).replace('-', '−') : formatCents(cents)
  return <span className="shrink-0 font-medium tabular-nums">{text}</span>
}

function ListSkeleton() {
  return (
    <div className="flex flex-col gap-2" aria-busy aria-label="Carregando lançamentos">
      <Skeleton className="h-3 w-20" />
      <div className="overflow-hidden rounded-2xl border bg-card">
        {[0, 1, 2, 3].map((i) => (
          <div key={i} className="flex items-center gap-3 px-4 py-3 [&+&]:border-t">
            <Skeleton className="size-10 rounded-xl" />
            <div className="flex flex-1 flex-col gap-2">
              <Skeleton className="h-3.5 w-1/2" />
              <Skeleton className="h-3 w-1/3" />
            </div>
            <Skeleton className="h-4 w-16" />
          </div>
        ))}
      </div>
    </div>
  )
}

function EmptyMonth({ month }: { month: YearMonth }) {
  return (
    <div className="flex flex-col items-center gap-4 rounded-3xl border border-dashed px-6 py-12 text-center">
      <PrismLogo className="size-14 text-muted-foreground" />
      <div className="flex flex-col gap-1">
        <p className="font-display text-2xl">Nada por aqui</p>
        <p className="text-sm text-muted-foreground">Nenhum lançamento em {formatMonth(month).toLowerCase()}.</p>
      </div>
      <Button asChild className="rounded-full">
        <Link to="/lancar">Fazer um lançamento</Link>
      </Button>
    </div>
  )
}
