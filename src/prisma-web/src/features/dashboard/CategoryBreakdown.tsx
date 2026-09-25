import { ChevronRight } from 'lucide-react'
import { Link } from 'react-router'
import { CategoryTile } from '@/components/brand/Tiles'
import { Skeleton } from '@/components/ui/skeleton'
import { formatCents } from '@/lib/money'
import { hiddenRefundsNotice, shareOf } from './categories'
import { useCategoryTotals, type CategoryTotal } from './queries'

const uncategorizedColor = '#94a3b8'

// Despesas do mês pela categoria raiz, já com os estornos abatidos (docs/fase-2.md, 2.2 e 2.5). Cada
// linha abre a lista que soma o mesmo valor: pela data de caixa, despesas e estornos da categoria.
// A participação é sobre as categorias exibidas: a que o estorno zerou some e vira o aviso.
export function CategoryBreakdown({ month }: { month: string }) {
  const totals = useCategoryTotals(month)

  if (totals.isPending) return <Skeleton className="h-48 w-full rounded-2xl" />
  if (totals.isError || totals.data.categories.length === 0) return null

  const { categories, hiddenRefundCents } = totals.data
  const largest = categories[0].amountCents
  const shownTotal = categories.reduce((sum, c) => sum + c.amountCents, 0)
  const notice = hiddenRefundsNotice(hiddenRefundCents)

  return (
    <section className="flex flex-col gap-3">
      <h2 className="px-1 text-sm font-medium text-foreground/75">Onde foi o dinheiro</h2>
      <ul className="surface flex flex-col rounded-2xl py-1.5">
        {categories.map((item) => (
          <Row key={item.categoryId ?? 'sem'} item={item} month={month} totalCents={shownTotal} largestCents={largest} />
        ))}
      </ul>
      {notice && <p className="px-1 text-xs text-muted-foreground">{notice}</p>}
    </section>
  )
}

function Row({
  item,
  month,
  totalCents,
  largestCents,
}: {
  item: CategoryTotal
  month: string
  totalCents: number
  largestCents: number
}) {
  const name = item.name ?? 'Sem categoria'
  const color = item.color ?? uncategorizedColor
  // A barra compara com a maior categoria, para as menores não virarem um fio.
  const width = largestCents > 0 ? Math.max((item.amountCents / largestCents) * 100, 2) : 0

  return (
    <li>
      <Link
        to={`/lancamentos?mes=${month}&categoria=${item.categoryId ?? 'sem'}`}
        className="flex items-center gap-3 px-4 py-2.5 transition-colors outline-none hover:bg-muted/40 focus-visible:bg-muted/60 active:bg-muted/70"
      >
        <CategoryTile name={name} icon={item.icon} color={color} />
        <div className="flex min-w-0 flex-1 flex-col gap-1.5">
          <div className="flex items-baseline justify-between gap-3">
            <span className="truncate text-sm font-medium">{name}</span>
            <span className="shrink-0 text-sm font-semibold tabular-nums">{formatCents(item.amountCents)}</span>
          </div>
          <div className="flex items-center gap-3">
            <div className="h-1.5 flex-1 overflow-hidden rounded-full bg-muted">
              <div
                className="h-full rounded-full"
                style={{ width: `${width}%`, background: `oklch(from ${color} var(--tile-fg-l) var(--tile-fg-c) h)` }}
              />
            </div>
            <span className="w-9 shrink-0 text-right text-xs text-muted-foreground tabular-nums">
              {shareOf(item.amountCents, totalCents)}%
            </span>
          </div>
        </div>
        <ChevronRight className="size-4 shrink-0 text-muted-foreground" />
      </Link>
    </li>
  )
}
