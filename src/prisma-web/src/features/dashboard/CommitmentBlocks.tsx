import { ChevronRight } from 'lucide-react'
import { useMemo, useState } from 'react'
import { Link } from 'react-router'
import { EntryTile } from '@/components/brand/Tiles'
import { Skeleton } from '@/components/ui/skeleton'
import { useAccounts } from '@/features/accounts/queries'
import { categoryLabels, useCategories } from '@/features/categories/queries'
import { monthOf, type YearMonth } from '@/lib/dates'
import { formatCents } from '@/lib/money'
import { barWidths, inheritedShare, lastInstallmentText, shortMonth } from './commitments'
import { useCommittedMonths, useInheritedInstallments, type InheritedInstallment } from './queries'

// Compromissos herdados (docs/fase-2.md, 2.6). Sem espectro novo: o da tela é o filete sob o mês.
// Herdado e comprometido ficam em tinta, como toda despesa (CLAUDE.md, 7.1).

const shownRows = 4

// Parcelas de compras anteriores: a parte do "Saiu" do mês escolhido que já estava decidida.
export function InheritedInstallments({ month, monthName }: { month: string; monthName: string }) {
  const inherited = useInheritedInstallments(month)
  const categories = useCategories()
  const accounts = useAccounts()
  const labels = useMemo(() => categoryLabels(categories.data ?? []), [categories.data])
  const [expanded, setExpanded] = useState(false)

  if (inherited.isPending) return <Skeleton className="h-56 w-full rounded-2xl" />
  if (inherited.isError || inherited.data.inheritedCents <= 0) return null

  const { inheritedCents, decidedInMonthCents, expenseCents, items } = inherited.data
  const share = inheritedShare(inheritedCents, decidedInMonthCents)
  const rows = expanded ? items : items.slice(0, shownRows)
  const accountName = (id: string) => accounts.data?.find((a) => a.id === id)?.name

  return (
    <section className="flex flex-col gap-3">
      <h2 className="px-1 text-sm font-medium text-foreground/75">Parcelas de compras anteriores</h2>
      <div className="surface flex flex-col rounded-2xl">
        <div className="flex flex-col gap-3 p-4">
          <p className="text-sm leading-relaxed text-foreground/75">
            <span className="font-semibold text-foreground tabular-nums">{formatCents(inheritedCents)}</span>
            {share === null
              ? ` em parcelas de compras feitas antes de ${monthName}.`
              : ` dos ${formatCents(expenseCents)} que saíram em ${monthName} vieram de compras anteriores.`}
          </p>
          {share !== null && decidedInMonthCents !== null && (
            <>
              <div
                className="flex h-2 overflow-hidden rounded-full bg-foreground/12"
                role="img"
                aria-label={`${share}% de compras anteriores`}
              >
                <div className="h-full rounded-full bg-foreground/85" style={{ width: `${share}%` }} />
              </div>
              <dl className="grid grid-cols-2 gap-3 text-sm">
                <Legend tone="bg-foreground/85" label="Compras anteriores" cents={inheritedCents} note={`${share}%`} />
                <Legend tone="bg-foreground/20" label="Decidido no mês" cents={decidedInMonthCents} />
              </dl>
            </>
          )}
        </div>

        <ul className="flex flex-col border-t border-border/60 py-1.5">
          {rows.map((item) => (
            <Row
              key={item.transactionId}
              item={item}
              category={item.categoryId ? labels.get(item.categoryId) : undefined}
              account={accountName(item.accountId)}
            />
          ))}
        </ul>
        {items.length > shownRows && (
          <button
            type="button"
            onClick={() => setExpanded((open) => !open)}
            className="border-t border-border/60 px-4 py-3 text-sm font-medium text-foreground/75 transition-colors hover:bg-muted/40 hover:text-foreground"
          >
            {expanded ? 'Mostrar menos' : `Ver todas (${items.length})`}
          </button>
        )}
      </div>
    </section>
  )
}

function Legend({ tone, label, cents, note }: { tone: string; label: string; cents: number; note?: string }) {
  return (
    <div className="flex min-w-0 flex-col gap-0.5">
      <dt className="flex items-center gap-1.5 text-xs text-muted-foreground">
        <span aria-hidden className={`size-2 shrink-0 rounded-full ${tone}`} />
        <span className="truncate">{label}</span>
      </dt>
      <dd className="font-semibold tabular-nums">
        {formatCents(cents)}
        {note && <span className="ml-1.5 text-xs font-normal text-muted-foreground">{note}</span>}
      </dd>
    </div>
  )
}

// Uma parcela herdada. Leva ao mês em que a compra foi feita, onde ela aparece na lista.
function Row({
  item,
  category,
  account,
}: {
  item: InheritedInstallment
  category?: { name: string; icon: string | null; color: string | null }
  account?: string
}) {
  const title = item.description || category?.name || 'Compra parcelada'
  const details = [category && category.name !== title ? category.name : null, account].filter(Boolean).join(' · ')
  const progress = (item.installmentNumber / item.installmentCount) * 100

  return (
    <li>
      <Link
        to={`/lancamentos?mes=${item.purchaseDate.slice(0, 7)}`}
        className="flex items-center gap-3 px-4 py-2.5 transition-colors outline-none hover:bg-muted/40 focus-visible:bg-muted/60 active:bg-muted/70"
      >
        <EntryTile description={item.description} category={category} />
        <div className="flex min-w-0 flex-1 flex-col gap-0.5">
          <span className="truncate text-sm font-medium">{title}</span>
          {details && <span className="truncate text-xs text-muted-foreground">{details}</span>}
        </div>
        <div className="flex shrink-0 flex-col items-end gap-1">
          <span className="text-sm font-semibold tabular-nums">{formatCents(item.amountCents)}</span>
          {/* Onde a compra está: a parcela do mês sobre o total, com o caminho já percorrido. */}
          <span className="flex items-center gap-1.5 text-xs text-muted-foreground tabular-nums">
            <span aria-hidden className="h-1 w-8 overflow-hidden rounded-full bg-foreground/12">
              <span className="block h-full rounded-full bg-foreground/60" style={{ width: `${progress}%` }} />
            </span>
            {item.installmentNumber}/{item.installmentCount}
          </span>
        </div>
        <ChevronRight className="size-4 shrink-0 text-muted-foreground" />
      </Link>
    </li>
  )
}

// Já comprometido: o "Saiu" que cada um dos próximos 6 meses já tem hoje. Olha para hoje, como o
// bloco "Hoje"; tocar num mês abre o Resumo dele.
export function CommittedMonths({ onSelect }: { onSelect: (month: YearMonth) => void }) {
  const committed = useCommittedMonths()

  if (committed.isPending) return <Skeleton className="h-60 w-full rounded-2xl" />
  if (committed.isError) return null

  const { months, lastInstallmentMonth } = committed.data
  if (!months.some((m) => m.expenseCents !== 0)) return null
  const widths = barWidths(months.map((m) => m.expenseCents))

  return (
    <section className="flex flex-col gap-3">
      <h2 className="px-1 text-sm font-medium text-foreground/75">Já comprometido</h2>
      <div className="surface flex flex-col rounded-2xl">
        <p className="px-4 pt-4 text-sm text-muted-foreground">Compras já lançadas que vencem nos próximos meses.</p>
        <ul className="flex flex-col py-2">
          {months.map((m, i) => (
            <li key={m.month}>
              <button
                type="button"
                onClick={() => onSelect(monthOf(`${m.month}-01`))}
                className="flex w-full items-center gap-3 px-4 py-2 text-left transition-colors outline-none hover:bg-muted/40 focus-visible:bg-muted/60 active:bg-muted/70"
              >
                <span className="w-12 shrink-0 text-sm font-medium tabular-nums">{shortMonth(m.month)}</span>
                <span className="h-2 flex-1 overflow-hidden rounded-full bg-foreground/8">
                  <span className="block h-full rounded-full bg-foreground/80" style={{ width: `${widths[i]}%` }} />
                </span>
                <span className="w-26 shrink-0 text-right text-sm font-semibold tabular-nums">
                  {m.expenseCents === 0 ? (
                    <span className="font-normal text-muted-foreground">—</span>
                  ) : (
                    formatCents(m.expenseCents).replace('-', '−')
                  )}
                </span>
              </button>
            </li>
          ))}
        </ul>
        {lastInstallmentMonth && (
          <p className="border-t border-border/60 px-4 py-3 text-sm text-foreground/75">
            {lastInstallmentText(lastInstallmentMonth)}
          </p>
        )}
      </div>
    </section>
  )
}
