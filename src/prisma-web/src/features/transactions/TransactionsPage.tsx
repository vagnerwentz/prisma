import { lazy, Suspense, useMemo, useState } from 'react'
import { X } from 'lucide-react'
import { Link, useSearchParams } from 'react-router'
import { PrismLogo } from '@/components/brand/PrismLogo'
import { EntryTile, TransferTile } from '@/components/brand/Tiles'
import { Alert, AlertDescription } from '@/components/ui/alert'
import { Button } from '@/components/ui/button'
import { Skeleton } from '@/components/ui/skeleton'
import { MonthSwitcher } from '@/components/MonthSwitcher'
import { StaleFade } from '@/components/StaleFade'
import { useMonthParam } from '@/lib/monthParam'
import { useAccounts } from '@/features/accounts/queries'
import { categoryLabels, useCategories, type CategoryLabel } from '@/features/categories/queries'
import { formatDayHeading, formatMonth, monthRange, todayInSaoPaulo, type YearMonth } from '@/lib/dates'
import { shortInstallments } from '@/lib/money'
import { Amount } from './Amount'
import { entryKey, findEntry } from './editing'
import { useTransactions, type Transaction, type TransactionFilters } from './queries'
import { buildTimeline, type TimelineEntry } from './timeline'

// O painel (detalhes e edição) só carrega quando alguém toca num lançamento.
const EntrySheet = lazy(() => import('./EntrySheet'))

type Lookups = { accounts: Map<string, string>; categories: Map<string, CategoryLabel> }

export function TransactionsPage() {
  const today = todayInSaoPaulo()
  // O mês fica na URL (?mes=2025-11): voltar, recarregar e abrir depois de um lançamento
  // antigo mostram o mês certo.
  const [month, setMonth] = useMonthParam()
  const range = monthRange(month)

  // Vindo de uma categoria do resumo (?categoria=<id> ou sem): só despesas e estornos dela, pela
  // data de caixa, para a lista somar o mesmo valor do resumo (docs/fase-2.md, 2.2 e 2.5).
  const [searchParams, setSearchParams] = useSearchParams()
  const category = searchParams.get('categoria')
  const filters: TransactionFilters = category
    ? {
        ...range,
        dateBasis: 'Settlement',
        type: ['Expense', 'Refund'],
        ...(category === 'sem' ? { uncategorized: true } : { categoryId: category }),
      }
    : range
  const clearCategory = () =>
    setSearchParams(
      (current) => {
        const params = new URLSearchParams(current)
        params.delete('categoria')
        return params
      },
      { replace: true },
    )

  const transactions = useTransactions(filters)
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

  // Guarda a chave, não o lançamento: depois de editar, o painel lê a versão recarregada.
  const [selectedKey, setSelectedKey] = useState<string | null>(null)
  const selected = selectedKey ? findEntry(days, selectedKey) : undefined

  return (
    <main className="mx-auto flex max-w-2xl flex-col gap-6 px-4 pt-5 pb-32">
      <MonthSwitcher month={month} onChange={setMonth} />

      {category && (
        <CategoryFilter
          name={category === 'sem' ? 'Sem categoria' : (lookups.categories.get(category)?.name ?? 'Categoria')}
          onClear={clearCategory}
        />
      )}

      {transactions.isPending && <ListSkeleton />}

      {transactions.isError && (
        <Alert variant="destructive">
          <AlertDescription>Não foi possível carregar os lançamentos. Tente novamente.</AlertDescription>
        </Alert>
      )}

      {transactions.isSuccess && days.length === 0 && <EmptyMonth month={month} />}

      {/* O mês numa superfície só, com o dia como subtítulo: um cartão por dia deixava um mês esparso
          com uns cinco lançamentos por tela. */}
      <StaleFade stale={transactions.isPlaceholderData}>
        {days.length > 0 && (
          <div className="surface overflow-clip rounded-2xl py-1">
            {days.map((day) => (
              <section key={day.date} className="[&+&]:border-t [&+&]:border-border/60">
                <h2 className="px-4 pt-3 pb-0.5 text-xs font-medium text-muted-foreground">
                  {formatDayHeading(day.date, today)}
                </h2>
                <ul>
                  {day.entries.map((entry) => (
                    <EntryRow
                      key={entryKey(entry)}
                      entry={entry}
                      lookups={lookups}
                      onOpen={() => setSelectedKey(entryKey(entry))}
                    />
                  ))}
                </ul>
              </section>
            ))}
          </div>
        )}
      </StaleFade>

      {selectedKey && accounts.data && categories.data && (
        <Suspense>
          <EntrySheet
            entry={selected}
            onClose={() => setSelectedKey(null)}
            onMoved={setMonth}
            accounts={accounts.data}
            categories={categories.data}
            labels={lookups.categories}
          />
        </Suspense>
      )}
    </main>
  )
}

function EntryRow({ entry, lookups, onOpen }: { entry: TimelineEntry<Transaction>; lookups: Lookups; onOpen: () => void }) {
  if (entry.kind === 'transfer') return <TransferRow entry={entry} lookups={lookups} onOpen={onOpen} />
  const first = entry.kind === 'single' ? entry.transaction : entry.installments[0]
  const category = first.categoryId ? lookups.categories.get(first.categoryId) : undefined
  const account = lookups.accounts.get(first.accountId)
  const amount = entry.kind === 'single' ? first.amountCents : entry.totalCents
  const installments = entry.kind === 'purchase' ? entry.installments.length : 0

  const isRefund = first.type === 'Refund'
  const title = first.description || category?.name || (isRefund ? 'Estorno' : 'Sem descrição')
  // Estorno se reconhece na linha (docs/fase-2.md, 2.5): o valor tem "+" em tinta, não no espectro.
  const details = [
    isRefund && title !== 'Estorno' ? 'Estorno' : null,
    category && category.name !== title ? category.name : null,
    account,
  ].filter(Boolean)

  return (
    <li>
      <button
        type="button"
        onClick={onOpen}
        className="flex w-full items-center gap-3 px-4 py-3 text-left transition-colors outline-none hover:bg-muted/40 focus-visible:bg-muted/60 active:bg-muted/70"
      >
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
      </button>
    </li>
  )
}

// Transferência: origem → destino, valor sem sinal (não é receita nem despesa; docs/fase-1.md, 2.3).
function TransferRow({
  entry,
  lookups,
  onOpen,
}: {
  entry: Extract<TimelineEntry<Transaction>, { kind: 'transfer' }>
  lookups: Lookups
  onOpen: () => void
}) {
  const leg = entry.out ?? entry.in
  const route = [entry.out, entry.in].map((t) => (t ? lookups.accounts.get(t.accountId) : '…')).join(' → ')
  return (
    <li>
      <button
        type="button"
        onClick={onOpen}
        className="flex w-full items-center gap-3 px-4 py-3 text-left transition-colors outline-none hover:bg-muted/40 focus-visible:bg-muted/60 active:bg-muted/70"
      >
        <TransferTile payment={!!entry.in?.statementId} />
        <div className="min-w-0 flex-1">
          <p className="truncate font-medium">{leg?.description}</p>
          <p className="truncate text-sm text-muted-foreground">{route}</p>
        </div>
        <Amount type="Transfer" cents={entry.amountCents} className="text-muted-foreground" />
      </button>
    </li>
  )
}

function ListSkeleton() {
  return (
    <div className="surface flex flex-col rounded-2xl py-1" aria-busy aria-label="Carregando lançamentos">
      <Skeleton className="mx-4 mt-3 mb-0.5 h-3 w-20" />
      <div>
        {[0, 1, 2, 3].map((i) => (
          <div key={i} className="flex items-center gap-3 px-4 py-3">
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

// Aviso do filtro vindo do resumo: a lista está pela data de caixa, então uma compra no cartão
// aparece no mês da fatura, sob o dia em que foi feita.
function CategoryFilter({ name, onClear }: { name: string; onClear: () => void }) {
  return (
    <div className="flex items-center gap-3 rounded-2xl bg-muted/60 py-2 pr-2 pl-4">
      <div className="flex min-w-0 flex-1 flex-col">
        <span className="truncate text-sm font-medium">Despesas em {name}</span>
        <span className="text-xs text-muted-foreground">Pela data de caixa, como no resumo</span>
      </div>
      <Button variant="ghost" size="icon" className="shrink-0 rounded-full" aria-label="Limpar filtro" onClick={onClear}>
        <X />
      </Button>
    </div>
  )
}
