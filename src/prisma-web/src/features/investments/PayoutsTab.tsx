import { Plus } from 'lucide-react'
import { useEffect, useMemo, useRef, useState } from 'react'
import { AssetTile } from '@/components/brand/Tiles'
import { Alert, AlertDescription } from '@/components/ui/alert'
import { Button } from '@/components/ui/button'
import { Skeleton } from '@/components/ui/skeleton'
import { Amount } from '@/features/transactions/Amount'
import { Chip, ChipRow } from '@/features/transactions/fields'
import { formatDayHeading, formatMonth } from '@/lib/dates'
import { formatCents } from '@/lib/money'
import { cn } from '@/lib/utils'
import { logoSrc } from './assets'
import type { PayoutView } from './PayoutSheet'
import {
  filterPayouts,
  groupByDay,
  inYear,
  monthBars,
  monthInitials,
  monthlyAverage,
  payingGroups,
  payoutKindLabels,
  payoutKinds,
  rankByAsset,
  receivedSplit,
  shortReais,
  yearBars,
  type Payout,
  type PayoutDay,
  type PayoutFilter,
  type PayoutKind,
  type PayoutRank,
} from './payouts'
import { formatWeight, groupColors, groupLabels, groupOf } from './portfolio'

// A aba Proventos (docs/investimentos.md, I.7e): o total do ano (recebido e a receber), filtros por tipo de provento e
// por grupo, mês a mês, ano a ano, quem mais pagou e os lançamentos. Tocar num mês mostra só ele; tocar num ano, escolhe
// o ano.
export function PayoutsTab({
  payouts,
  pending,
  error,
  years,
  year,
  today,
  onSelectYear,
  onOpenPayout,
  onOpenAsset,
}: {
  payouts: Payout[]
  pending: boolean
  error: Error | null
  years: number[]
  year: number
  today: string
  onSelectYear: (year: number) => void
  onOpenPayout: (view: PayoutView) => void
  onOpenAsset: (rank: PayoutRank) => void
}) {
  const [filter, setFilter] = useState<PayoutFilter>({ kind: null, group: null })
  const [picked, setPicked] = useState<{ year: number; month: number } | null>(null)
  const month = picked?.year === year ? picked.month : null

  const filtered = useMemo(() => filterPayouts(payouts, filter), [payouts, filter])
  const ofYear = useMemo(() => inYear(filtered, year), [filtered, year])
  const shown = useMemo(
    () => (month === null ? ofYear : ofYear.filter((p) => Number(p.date.slice(5, 7)) === month)),
    [ofYear, month],
  )
  const bars = useMemo(() => monthBars(filtered, year, today), [filtered, year, today])
  const groups = useMemo(() => payingGroups(payouts), [payouts])
  const ranks = useMemo(() => rankByAsset(shown), [shown])
  const days = useMemo(() => groupByDay(shown), [shown])
  const { receivedCents, upcomingCents } = receivedSplit(shown, today)
  const total = receivedCents + upcomingCents
  const monthName = (m: number) => formatMonth({ year, month: m }).split(' de ')[0]
  const period = month === null ? String(year) : `${monthName(month).toLowerCase()} de ${year}`

  if (pending) return <Skeleton className="h-96 w-full rounded-3xl" />
  // Sem os proventos, nada de "R$ 0,00": zero é um número que se lê como verdade.
  if (error)
    return (
      <Alert variant="destructive">
        <AlertDescription>Não foi possível carregar seus proventos.</AlertDescription>
      </Alert>
    )

  return (
    <div className="flex flex-col gap-6">
      <section className="surface flex flex-col gap-4 rounded-3xl px-5 pt-5 pb-4">
        <div className="flex flex-wrap items-end justify-between gap-x-4 gap-y-3">
          <div className="flex min-w-0 flex-col gap-1">
            <h2 className="text-sm font-medium text-foreground/75">Proventos em {period}</h2>
            <Amount type="Income" cents={total} className="font-display text-5xl leading-none font-normal" />
            {upcomingCents > 0 && (
              <p className="pt-1 text-sm text-muted-foreground tabular-nums">
                {formatCents(receivedCents)} recebidos · {formatCents(upcomingCents)} a receber
              </p>
            )}
          </div>
          <Button size="sm" className="shrink-0 rounded-full" onClick={() => onOpenPayout({ kind: 'new' })}>
            <Plus />
            Novo provento
          </Button>
        </div>

        <MonthChart bars={bars} month={month} onPick={(m) => setPicked(m === month ? null : { year, month: m })} />
        <p className="text-sm text-muted-foreground">
          {month === null
            ? `Média de ${formatCents(monthlyAverage(bars, year, today))} por mês`
            : `Toque em ${monthName(month).toLowerCase()} de novo para ver o ano todo`}
        </p>
      </section>

      <div className="flex flex-col gap-2">
        <ChipRow>
          <FilterChip selected={filter.kind === null} onClick={() => setFilter({ ...filter, kind: null })}>
            Todos
          </FilterChip>
          {payoutKinds.map((kind: PayoutKind) => (
            <FilterChip key={kind} selected={filter.kind === kind} onClick={() => setFilter({ ...filter, kind })}>
              {payoutKindLabels[kind]}
            </FilterChip>
          ))}
        </ChipRow>
        {groups.length > 1 && (
          <ChipRow>
            <FilterChip selected={filter.group === null} onClick={() => setFilter({ ...filter, group: null })}>
              Todos os ativos
            </FilterChip>
            {groups.map((group) => (
              <FilterChip key={group} selected={filter.group === group} onClick={() => setFilter({ ...filter, group })}>
                <span className="h-2.5 w-2.5 rounded-[3px]" style={{ background: groupColors[group] }} />
                {groupLabels[group]}
              </FilterChip>
            ))}
          </ChipRow>
        )}
      </div>

      {years.length > 1 && <YearChart bars={yearBars(filtered, years)} year={year} onPick={onSelectYear} />}

      {ranks.length > 0 && (
        <section className="flex flex-col gap-3">
          <h2 className="px-1 text-sm font-medium text-foreground/75">Por ativo</h2>
          <ul className="surface overflow-hidden rounded-2xl">
            {ranks.map((rank) => (
              <li key={rank.assetId} className="[&+&]:border-t [&+&]:border-border/60">
                <button type="button" onClick={() => onOpenAsset(rank)} className={`${rowClass} flex-col items-stretch gap-2`}>
                  <span className="flex items-center gap-3">
                    <AssetTile
                      symbol={rank.symbol}
                      logo={logoSrc({ id: rank.assetId, kind: rank.assetKind, hasLogo: rank.assetHasLogo })}
                    />
                    <span className="flex min-w-0 flex-1 flex-col">
                      <span className="flex items-baseline justify-between gap-2">
                        <span className="font-medium">{rank.symbol}</span>
                        <Amount type="Income" cents={rank.cents} />
                      </span>
                      <span className="flex items-baseline justify-between gap-2 text-sm text-muted-foreground">
                        <span className="truncate">{rank.assetName}</span>
                        <span className="shrink-0 tabular-nums">{formatWeight(Math.round(rank.share * 10000))}</span>
                      </span>
                    </span>
                  </span>
                  <span className="h-1 w-full overflow-hidden rounded-full bg-muted">
                    <span
                      className="meter-fill block h-full"
                      style={{ background: groupColors[groupOf(rank.assetKind)], ['--fill' as string]: `${rank.share * 100}%` }}
                    />
                  </span>
                </button>
              </li>
            ))}
          </ul>
        </section>
      )}

      {days.length > 0 ? (
        <section className="flex flex-col gap-3">
          <h2 className="px-1 text-sm font-medium text-foreground/75">Lançamentos</h2>
          <ul className="surface overflow-hidden rounded-2xl">
            {days.map((day) => (
              <li key={day.date} className="[&+&]:border-t [&+&]:border-border/60">
                <button
                  type="button"
                  onClick={() =>
                    onOpenPayout(
                      day.items.length === 1
                        ? { kind: 'edit', id: day.items[0].id }
                        : { kind: 'day', ids: day.items.map((p) => p.id) },
                    )
                  }
                  className={rowClass}
                >
                  <AssetTile
                    symbol={day.items[0].symbol}
                    logo={logoSrc({ id: day.items[0].assetId, kind: day.items[0].assetKind, hasLogo: day.items[0].assetHasLogo })}
                  />
                  <PayoutDayText day={day} today={today} />
                  <Amount type="Income" cents={day.totalCents} />
                </button>
              </li>
            ))}
          </ul>
        </section>
      ) : (
        <p className="surface rounded-2xl px-4 py-6 text-center text-sm text-muted-foreground">Nenhum provento em {period}.</p>
      )}
    </div>
  )
}

const rowClass =
  'flex w-full items-center gap-3 px-4 py-3 text-left transition-colors outline-none hover:bg-muted/40 focus-visible:bg-muted/60 active:bg-muted/70'

function FilterChip(props: { selected: boolean; onClick: () => void; children: React.ReactNode }) {
  return <Chip {...props} className="h-9 px-3" />
}

// 12 colunas: o recebido no espectro frio, o a receber esmaecido por cima. O mês escolhido fica aceso.
function MonthChart({
  bars,
  month,
  onPick,
}: {
  bars: ReturnType<typeof monthBars>
  month: number | null
  onPick: (m: number) => void
}) {
  const max = Math.max(1, ...bars.map((b) => b.receivedCents + b.upcomingCents))
  return (
    <div className="grid h-36 grid-cols-12 items-end gap-1.5" role="group" aria-label="Proventos mês a mês">
      {bars.map((bar) => {
        const total = bar.receivedCents + bar.upcomingCents
        const label = formatMonth({ year: 2000, month: bar.month }).split(' de ')[0]
        return (
          <button
            key={bar.month}
            type="button"
            aria-pressed={month === bar.month}
            aria-label={`${label}: ${formatCents(total)}`}
            onClick={() => onPick(bar.month)}
            className={cn(
              'flex h-full flex-col items-center justify-end gap-1.5 rounded-lg transition-opacity duration-200 ease-out',
              month !== null && month !== bar.month && 'opacity-35',
            )}
          >
            <span className="flex w-full max-w-5 flex-1 flex-col justify-end">
              {bar.upcomingCents > 0 && (
                <span
                  className="w-full rounded-t-[5px] border border-b-0 border-dashed border-[#8b5cf6]/60 bg-[#8b5cf6]/10"
                  style={{ height: `${(bar.upcomingCents / max) * 100}%` }}
                />
              )}
              {bar.receivedCents > 0 && (
                <span
                  className={cn('w-full bg-[linear-gradient(#8b5cf6,#22d3ee)]', bar.upcomingCents > 0 ? '' : 'rounded-t-[5px]')}
                  style={{ height: `${(bar.receivedCents / max) * 100}%` }}
                />
              )}
              {total === 0 && <span className="h-[2px] w-full rounded-full bg-muted" />}
            </span>
            <span className="text-[11px] text-muted-foreground">{monthInitials[bar.month - 1]}</span>
          </button>
        )
      })}
    </div>
  )
}

// Uma coluna por ano; tocar escolhe o ano para a tela inteira. Com muitos anos a fileira rola, e o ano escolhido fica à
// vista (rola só a fileira, como o Chip).
function YearChart({
  bars,
  year,
  onPick,
}: {
  bars: { year: number; cents: number }[]
  year: number
  onPick: (y: number) => void
}) {
  const max = Math.max(1, ...bars.map((b) => b.cents))
  const row = useRef<HTMLDivElement>(null)
  useEffect(() => {
    const box = row.current
    const chosen = box?.querySelector<HTMLElement>('[aria-pressed=true]')
    if (!box || !chosen) return
    const c = chosen.getBoundingClientRect()
    const r = box.getBoundingClientRect()
    if (c.left < r.left || c.right > r.right) box.scrollBy({ left: c.left - r.left - (r.width - c.width) / 2 })
  }, [year])
  return (
    <section className="flex flex-col gap-3">
      <h2 className="px-1 text-sm font-medium text-foreground/75">Ano a ano</h2>
      <div
        ref={row}
        className="surface overflow-x-auto rounded-2xl px-3 pt-4 pb-3 [scrollbar-width:none] [&::-webkit-scrollbar]:hidden"
      >
        <div className="flex h-28 min-w-full items-end gap-1">
          {bars.map((bar) => (
            <button
              key={bar.year}
              type="button"
              aria-pressed={bar.year === year}
              aria-label={`${bar.year}: ${formatCents(bar.cents)}`}
              onClick={() => onPick(bar.year)}
              className={cn(
                'flex h-full min-w-11 flex-1 flex-col items-center justify-end gap-1.5 transition-opacity duration-200 ease-out',
                bar.year !== year && 'opacity-45 hover:opacity-80',
              )}
            >
              <span className="text-[11px] text-muted-foreground tabular-nums">{shortReais(bar.cents)}</span>
              <span
                className={cn(
                  'w-full max-w-8 rounded-t-[6px]',
                  bar.cents > 0 ? 'bg-[linear-gradient(#8b5cf6,#22d3ee)]' : 'bg-muted',
                )}
                style={{ height: bar.cents > 0 ? `${Math.max(4, (bar.cents / max) * 70)}%` : '2px' }}
              />
              <span className={cn('text-xs tabular-nums', bar.year === year ? 'font-medium' : 'text-muted-foreground')}>
                {bar.year}
              </span>
            </button>
          ))}
        </div>
      </div>
    </section>
  )
}

// Um provento: o tipo e o ativo. Vários no mesmo dia: quantos e quais.
function PayoutDayText({ day, today }: { day: PayoutDay; today: string }) {
  const [first] = day.items
  const title =
    day.items.length === 1 ? `${payoutKindLabels[first.kind as PayoutKind]} · ${first.symbol}` : `${day.items.length} ativos`
  const details =
    day.items.length === 1
      ? formatDayHeading(day.date, today)
      : `${formatDayHeading(day.date, today)} · ${day.items.map((p) => p.symbol).join(', ')}`
  return (
    <span className="flex min-w-0 flex-1 flex-col">
      <span className="truncate font-medium">{title}</span>
      <span className="truncate text-sm text-muted-foreground">{details}</span>
    </span>
  )
}
