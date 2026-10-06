import { ArrowLeft, ChevronRight, Plus } from 'lucide-react'
import { useMemo, useState } from 'react'
import { Link } from 'react-router'
import { toast } from 'sonner'
import { AssetTile } from '@/components/brand/Tiles'
import { BottomSheet } from '@/components/BottomSheet'
import { Alert, AlertDescription } from '@/components/ui/alert'
import { Button } from '@/components/ui/button'
import { SheetTitle } from '@/components/ui/sheet'
import { Skeleton } from '@/components/ui/skeleton'
import { Amount } from '@/features/transactions/Amount'
import { formatDayHeading, formatShortDate, todayInSaoPaulo } from '@/lib/dates'
import { formatCents } from '@/lib/money'
import { AssetPicker } from './AssetPicker'
import { assetKindLabel, logoSrc, type AssetItem } from './assets'
import { PayoutSheet, type PayoutView } from './PayoutSheet'
import { groupByDay, payoutKindLabels, totalsByAsset, yearTotal, type Payout, type PayoutDay, type PayoutKind } from './payouts'
import { useAddHolding, useHoldings, usePayouts, useRemoveHolding, type Holding } from './queries'

// Quantos dias de proventos a tela mostra; o resto fica no painel de cada ativo e nos Lançamentos.
const RecentDays = 6

// Investimentos, dentro de Contas (docs/investimentos.md, seção 8, etapas 5a e 5b): o que entrou de proventos no ano,
// os ativos da carteira com o total que cada um já pagou, e os últimos proventos por dia.
export function InvestmentsPage() {
  const today = todayInSaoPaulo()
  const year = Number(today.slice(0, 4))
  const holdings = useHoldings()
  const payouts = usePayouts()
  const add = useAddHolding()
  const remove = useRemoveHolding()
  const [adding, setAdding] = useState(false)
  const [open, setOpen] = useState<Holding | null>(null)
  const [payoutView, setPayoutView] = useState<PayoutView | null>(null)

  const list = holdings.data ?? []
  const allPayouts = useMemo(() => payouts.data ?? [], [payouts.data])
  const totals = useMemo(() => totalsByAsset(allPayouts), [allPayouts])
  const days = useMemo(() => groupByDay(allPayouts).slice(0, RecentDays), [allPayouts])

  const addAsset = (asset: AssetItem) => {
    setAdding(false)
    if (list.some((h) => h.assetId === asset.id)) {
      toast(`${asset.symbol} já está na carteira`)
      return
    }
    add.mutate(asset.id, {
      onSuccess: () => toast(`${asset.symbol} na carteira`),
      onError: (error) => toast.error(error.message),
    })
  }

  const removeHolding = (holding: Holding) => {
    setOpen(null)
    remove.mutate(holding.id, {
      onSuccess: () =>
        toast(`${holding.symbol} saiu da carteira`, {
          description: totals.get(holding.assetId) ? 'Os proventos dele continuam nos lançamentos.' : undefined,
          duration: 8000,
          action: { label: 'Desfazer', onClick: () => add.mutate(holding.assetId) },
        }),
      onError: (error) => toast.error(error.message),
    })
  }

  // Do painel do ativo para o do provento: um painel por vez.
  const openPayout = (view: PayoutView) => {
    setOpen(null)
    setPayoutView(view)
  }

  return (
    <main className="mx-auto flex max-w-2xl flex-col gap-6 px-4 pt-3 pb-32">
      <nav>
        <Button asChild variant="ghost" size="sm" className="-ml-2 rounded-full text-muted-foreground">
          <Link to="/contas">
            <ArrowLeft />
            Contas
          </Link>
        </Button>
      </nav>
      <div className="flex flex-col gap-3">
        <h1 className="font-display text-4xl leading-none">Investimentos</h1>
        <div className="spectrum-line opacity-80" />
      </div>

      {/* O botão desce para a linha de baixo quando o total não cabe ao lado (320 px com milhares de reais). */}
      <section className="surface flex flex-wrap items-end justify-between gap-x-4 gap-y-3 rounded-2xl px-4 py-4">
        <div className="flex min-w-0 flex-col gap-1">
          <h2 className="text-sm font-medium text-foreground/75">Proventos em {year}</h2>
          {payouts.isPending ? (
            <Skeleton className="h-9 w-32" />
          ) : (
            <Amount
              type="Income"
              cents={yearTotal(allPayouts, year)}
              className="font-display text-4xl leading-tight font-normal"
            />
          )}
        </div>
        <Button size="sm" className="shrink-0 rounded-full" onClick={() => setPayoutView({ kind: 'new' })}>
          <Plus />
          Novo provento
        </Button>
      </section>

      <section className="flex flex-col gap-3">
        <div className="flex items-end justify-between">
          <h2 className="px-1 text-sm font-medium text-foreground/75">Meus ativos</h2>
          {list.length > 0 && (
            <Button size="sm" variant="secondary" className="rounded-full" onClick={() => setAdding(true)}>
              <Plus />
              Adicionar
            </Button>
          )}
        </div>

        {holdings.isPending && <Skeleton className="h-40 w-full rounded-2xl" />}
        {holdings.isError && (
          <Alert variant="destructive">
            <AlertDescription>Não foi possível carregar seus ativos.</AlertDescription>
          </Alert>
        )}
        {holdings.isSuccess && list.length === 0 && (
          <div className="flex flex-col items-center gap-4 rounded-3xl border border-dashed px-6 py-12 text-center">
            <div className="flex flex-col gap-1">
              <p className="font-display text-2xl">Seus ativos ficam aqui</p>
              <p className="text-sm text-muted-foreground">Ações, FIIs, ETFs: adicione os que você tem.</p>
            </div>
            <Button className="rounded-full" onClick={() => setAdding(true)}>
              <Plus />
              Adicionar ativo
            </Button>
          </div>
        )}
        {list.length > 0 && (
          <ul className="surface overflow-hidden rounded-2xl">
            {list.map((holding) => (
              <li key={holding.id} className="[&+&]:border-t [&+&]:border-border/60">
                <button
                  type="button"
                  onClick={() => setOpen(holding)}
                  className="flex w-full items-center gap-3 px-4 py-3 text-left transition-colors outline-none hover:bg-muted/40 focus-visible:bg-muted/60 active:bg-muted/70"
                >
                  <AssetTile symbol={holding.symbol} logo={holdingLogo(holding)} />
                  <HoldingText holding={holding} />
                  {/* Soma de todos os anos, não só do ano do topo: o rótulo evita ler como "em 2026". */}
                  {totals.get(holding.assetId) ? (
                    <span className="flex shrink-0 flex-col items-end">
                      <Amount type="Income" cents={totals.get(holding.assetId)!} className="text-sm" />
                      <span className="text-xs text-muted-foreground">total recebido</span>
                    </span>
                  ) : null}
                </button>
              </li>
            ))}
          </ul>
        )}
      </section>

      {days.length > 0 && (
        <section className="flex flex-col gap-3">
          <h2 className="px-1 text-sm font-medium text-foreground/75">Últimos proventos</h2>
          <ul className="surface overflow-hidden rounded-2xl">
            {days.map((day) => (
              <li key={day.date} className="[&+&]:border-t [&+&]:border-border/60">
                <button
                  type="button"
                  onClick={() =>
                    setPayoutView(
                      day.items.length === 1
                        ? { kind: 'edit', id: day.items[0].id }
                        : { kind: 'day', ids: day.items.map((p) => p.id) },
                    )
                  }
                  className="flex w-full items-center gap-3 px-4 py-3 text-left transition-colors outline-none hover:bg-muted/40 focus-visible:bg-muted/60 active:bg-muted/70"
                >
                  <AssetTile symbol={day.items[0].symbol} logo={payoutLogo(day.items[0])} />
                  <PayoutDayText day={day} today={today} />
                  <Amount type="Income" cents={day.totalCents} />
                </button>
              </li>
            ))}
          </ul>
        </section>
      )}

      <BottomSheet open={adding} onClose={() => setAdding(false)}>
        <header className="px-4 pt-2 pb-3">
          <SheetTitle className="font-display text-2xl font-normal">Adicionar ativo</SheetTitle>
        </header>
        <div className="px-4 pb-6">
          <AssetPicker value={null} onChange={(asset) => asset && addAsset(asset)} autoFocus />
        </div>
      </BottomSheet>

      <BottomSheet open={open !== null} onClose={() => setOpen(null)}>
        {open && (
          <HoldingDetails
            holding={open}
            payouts={allPayouts.filter((p) => p.assetId === open.assetId)}
            onNewPayout={() =>
              openPayout({
                kind: 'new',
                asset: {
                  id: open.assetId,
                  symbol: open.symbol,
                  name: open.name,
                  kind: open.kind,
                  isActive: open.isActive,
                  hasLogo: open.hasLogo,
                },
              })
            }
            onOpenPayout={(id) => openPayout({ kind: 'edit', id })}
            onRemove={() => removeHolding(open)}
          />
        )}
      </BottomSheet>

      <PayoutSheet view={payoutView} onClose={() => setPayoutView(null)} />
    </main>
  )
}

function HoldingDetails({
  holding,
  payouts,
  onNewPayout,
  onOpenPayout,
  onRemove,
}: {
  holding: Holding
  payouts: Payout[]
  onNewPayout: () => void
  onOpenPayout: (id: string) => void
  onRemove: () => void
}) {
  const total = payouts.reduce((sum, p) => sum + p.amountCents, 0)
  return (
    <>
      <header className="flex items-center gap-3 px-4 pt-2 pb-3">
        <AssetTile symbol={holding.symbol} logo={holdingLogo(holding)} size="lg" />
        <div className="min-w-0">
          <SheetTitle className="font-display text-2xl leading-tight font-normal">{holding.symbol}</SheetTitle>
          <p className="truncate text-sm text-muted-foreground">{holding.name}</p>
        </div>
      </header>
      <div className="flex min-h-0 flex-col gap-4 overflow-y-auto overscroll-contain px-4 pb-4">
        <dl className="flex flex-col gap-2 rounded-2xl bg-muted/50 px-4 py-3 text-sm">
          <div className="flex justify-between gap-3">
            <dt className="text-muted-foreground">Tipo</dt>
            <dd>{assetKindLabel(holding.kind)}</dd>
          </div>
          <div className="flex justify-between gap-3">
            <dt className="text-muted-foreground">Na carteira desde</dt>
            <dd>{formatShortDate(todayInSaoPaulo(new Date(holding.addedAt)))}</dd>
          </div>
          <div className="flex justify-between gap-3">
            <dt className="text-muted-foreground">Proventos recebidos</dt>
            <dd className="tabular-nums">{formatCents(total)}</dd>
          </div>
          {!holding.isActive && <p className="text-muted-foreground">Este ativo saiu da bolsa.</p>}
        </dl>
        {payouts.length > 0 && (
          <ul className="flex flex-col">
            {payouts.map((p) => (
              <li key={p.id}>
                <button
                  type="button"
                  onClick={() => onOpenPayout(p.id)}
                  className="flex w-full items-center gap-3 rounded-xl px-2 py-2 text-left transition-colors hover:bg-muted/50 active:bg-muted/70"
                >
                  <span className="min-w-0 flex-1">
                    <span className="block text-sm font-medium">{payoutKindLabels[p.kind as PayoutKind]}</span>
                    <span className="block text-xs text-muted-foreground">{formatShortDate(p.date)}</span>
                  </span>
                  <Amount type="Income" cents={p.amountCents} className="text-sm" />
                  <ChevronRight className="size-4 shrink-0 text-muted-foreground" />
                </button>
              </li>
            ))}
          </ul>
        )}
      </div>
      <div className="flex gap-2 px-4 pt-1 pb-6">
        <Button variant="secondary" className="flex-1 rounded-full" onClick={onRemove}>
          Tirar da carteira
        </Button>
        <Button className="flex-1 rounded-full" onClick={onNewPayout}>
          <Plus />
          Novo provento
        </Button>
      </div>
    </>
  )
}

const holdingLogo = (h: Holding) => logoSrc({ id: h.assetId, kind: h.kind, hasLogo: h.hasLogo })
const payoutLogo = (p: Payout) => logoSrc({ id: p.assetId, kind: p.assetKind, hasLogo: p.assetHasLogo })

function HoldingText({ holding }: { holding: Holding }) {
  return (
    <span className="flex min-w-0 flex-1 flex-col">
      <span className="flex items-baseline gap-2">
        <span className="font-medium">{holding.symbol}</span>
        <span className="truncate text-xs text-muted-foreground">
          {assetKindLabel(holding.kind)}
          {!holding.isActive && ' · saiu da bolsa'}
        </span>
      </span>
      <span className="truncate text-sm text-muted-foreground">{holding.name}</span>
    </span>
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
