import { ChevronRight, Ellipsis, Layers } from 'lucide-react'
import type { ReactNode } from 'react'
import { Link } from 'react-router'
import { CategoryTile } from '@/components/brand/Tiles'
import { Skeleton } from '@/components/ui/skeleton'
import { formatCents } from '@/lib/money'
import { useSpendingVariation } from './queries'
import { monthNameOf, reasonDetail, reasonTitle, signedChange, variationHeadline, type VariationReason } from './variation'

const uncategorizedColor = '#94a3b8'

// Por que o gasto mudou (docs/fase-2.md, 2.7), no topo da Análise: os motivos somam a diferença. Em
// tinta, sem verde e vermelho: o sinal já diz a direção (CLAUDE.md, 7.1).
export function VariationBlock({ month }: { month: string }) {
  const variation = useSpendingVariation(month)

  if (variation.isPending) return <Skeleton className="h-52 w-full rounded-2xl" />
  if (variation.isError) return null

  const { expenseCents, previousExpenseCents, previousMonth, reasons } = variation.data
  const previousName = monthNameOf(previousMonth)
  const headline = variationHeadline(expenseCents, previousExpenseCents, previousName)
  if (!headline) return null

  return (
    <section className="flex flex-col gap-3">
      <h2 className="px-1 text-sm font-medium text-foreground/75">Por que o gasto mudou</h2>
      {/* A partir de 1024px, cabeçalho à esquerda e motivos à direita: numa linha só, o valor ficaria longe do nome. */}
      <div className="surface flex flex-col rounded-2xl lg:grid lg:grid-cols-[minmax(0,2fr)_minmax(0,3fr)]">
        <div className="flex flex-col gap-1 p-4 lg:p-5">
          <p className="text-sm font-semibold">{headline}</p>
          <p className="text-xs text-muted-foreground tabular-nums">
            {formatCents(previousExpenseCents).replace('-', '−')} em {previousName} →{' '}
            {formatCents(expenseCents).replace('-', '−')} em {monthNameOf(month)}
          </p>
        </div>
        {reasons.length > 0 && (
          <ul className="flex flex-col border-t border-border/60 py-1.5 lg:border-t-0 lg:border-l">
            {reasons.map((reason) => (
              <Row
                key={`${reason.kind}-${reason.categoryId ?? 'sem'}`}
                reason={reason}
                month={month}
                previousName={previousName}
              />
            ))}
          </ul>
        )}
      </div>
    </section>
  )
}

function Row({ reason, month, previousName }: { reason: VariationReason; month: string; previousName: string }) {
  const title = reasonTitle(reason)
  const detail = reasonDetail(reason, previousName)
  // Categoria abre a lista dela no mês, como em "Onde foi o dinheiro".
  const to = reason.kind === 'Category' ? `/lancamentos?mes=${month}&categoria=${reason.categoryId ?? 'sem'}` : undefined

  const content = (
    <>
      <ReasonTile reason={reason} title={title} />
      {/* O valor fica na linha do nome; o detalhe usa a largura toda embaixo, para caber em 320px. */}
      <div className="flex min-w-0 flex-1 flex-col gap-0.5">
        <div className="flex items-baseline justify-between gap-3">
          <span className="min-w-0 text-sm leading-snug font-medium">{title}</span>
          <span className="shrink-0 text-sm font-semibold tabular-nums">{signedChange(reason.changeCents)}</span>
        </div>
        {detail && <span className="text-xs text-muted-foreground">{detail}</span>}
      </div>
      <ChevronRight aria-hidden className={to ? 'size-4 shrink-0 text-muted-foreground' : 'invisible size-4 shrink-0'} />
    </>
  )
  const layout = 'flex items-center gap-3 px-4 py-2.5'

  return (
    <li>
      {to ? (
        <Link
          to={to}
          className={`${layout} transition-colors outline-none hover:bg-muted/40 focus-visible:bg-muted/60 active:bg-muted/70`}
        >
          {content}
        </Link>
      ) : (
        <div className={layout}>{content}</div>
      )}
    </li>
  )
}

function ReasonTile({ reason, title }: { reason: VariationReason; title: string }) {
  if (reason.kind === 'Category')
    return (
      <CategoryTile name={title} icon={reason.icon} color={reason.color ?? (reason.categoryId ? null : uncategorizedColor)} />
    )
  return <NeutralTile>{reason.kind === 'Inherited' ? <Layers aria-hidden /> : <Ellipsis aria-hidden />}</NeutralTile>
}

function NeutralTile({ children }: { children: ReactNode }) {
  return (
    <span className="flex size-10 shrink-0 items-center justify-center rounded-xl bg-muted text-foreground/80 [&_svg]:size-5">
      {children}
    </span>
  )
}
