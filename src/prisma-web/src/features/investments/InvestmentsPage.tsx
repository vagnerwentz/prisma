import { ArrowLeft, ChevronRight, Plus } from 'lucide-react'
import { useMemo, useState, type ReactNode } from 'react'
import { Link } from 'react-router'
import { toast } from 'sonner'
import { AccountTile, AssetTile } from '@/components/brand/Tiles'
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
import {
  useAddHolding,
  useHoldings,
  usePayouts,
  usePositionHistory,
  usePositions,
  useRemoveHolding,
  useTradeNotes,
  type Holding,
} from './queries'
import { TradeNoteSheet, type TradeNoteView } from './TradeNoteSheet'
import {
  noteTitle,
  noteTotals,
  priceParts,
  stepTitle,
  unitsLabel,
  withPositions,
  type Position,
  type PositionStep,
  type TradeNote,
} from './trades'

// Quantos dias de proventos a tela mostra; o resto fica no painel de cada ativo e nos Lançamentos.
const RecentDays = 6
// Quantas notas a tela mostra; as outras ficam no histórico de cada ativo.
const RecentNotes = 6

// Investimentos, dentro de Contas (docs/investimentos.md, seção 8, etapas 5a e 5b): o que entrou de proventos no ano,
// os ativos da carteira com o total que cada um já pagou, e os últimos proventos por dia. Com as notas de corretagem
// (I.7b2): quantidade e os dois preços médios de cada ativo, o histórico no painel dele e as últimas notas.
export function InvestmentsPage() {
  const today = todayInSaoPaulo()
  const year = Number(today.slice(0, 4))
  const holdings = useHoldings()
  const payouts = usePayouts()
  const add = useAddHolding()
  const remove = useRemoveHolding()
  const [adding, setAdding] = useState(false)
  const [open, setOpen] = useState<ListedHolding | null>(null)
  const [payoutView, setPayoutView] = useState<PayoutView | null>(null)
  const [noteView, setNoteView] = useState<TradeNoteView | null>(null)
  const positions = usePositions()
  const notes = useTradeNotes(RecentNotes)
  const recentNotes = notes.data ?? []

  const holdingList = useMemo(() => holdings.data ?? [], [holdings.data])
  const allPayouts = useMemo(() => payouts.data ?? [], [payouts.data])
  const totals = useMemo(() => totalsByAsset(allPayouts), [allPayouts])
  const days = useMemo(() => groupByDay(allPayouts).slice(0, RecentDays), [allPayouts])
  const positionOf = useMemo(() => new Map((positions.data ?? []).map((p) => [p.assetId, p])), [positions.data])
  // O ativo recebido numa conversão (I.7c1) aparece mesmo sem ter entrado na carteira.
  const list = useMemo(() => withPositions(holdingList, positions.data ?? []), [holdingList, positions.data])

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

  // Do painel do ativo para o do provento ou o da nota: um painel por vez.
  const openPayout = (view: PayoutView) => {
    setOpen(null)
    setPayoutView(view)
  }
  const openNote = (view: TradeNoteView) => {
    setOpen(null)
    setNoteView(view)
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
                  {positionOf.get(holding.assetId) ? (
                    <PositionText
                      holding={holding}
                      position={positionOf.get(holding.assetId)!}
                      payoutsCents={totals.get(holding.assetId) ?? 0}
                    />
                  ) : (
                    <HoldingText holding={holding} />
                  )}
                  {/* Soma de todos os anos, não só do ano do topo: o rótulo evita ler como "em 2026". */}
                  {!positionOf.get(holding.assetId) && totals.get(holding.assetId) ? (
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

      <section className="flex flex-col gap-3">
        <div className="flex items-end justify-between">
          <h2 className="px-1 text-sm font-medium text-foreground/75">Últimas notas</h2>
          {recentNotes.length > 0 && (
            <Button size="sm" variant="secondary" className="rounded-full" onClick={() => setNoteView({ kind: 'new' })}>
              <Plus />
              Nova nota
            </Button>
          )}
        </div>
        {notes.isSuccess && recentNotes.length === 0 && (
          <div className="surface flex flex-wrap items-center justify-between gap-3 rounded-2xl px-4 py-4">
            <p className="min-w-0 flex-1 text-sm text-muted-foreground">
              Lance suas notas de corretagem para ver quantidade e preço médio.
            </p>
            <Button size="sm" className="shrink-0 rounded-full" onClick={() => setNoteView({ kind: 'new' })}>
              <Plus />
              Nova nota
            </Button>
          </div>
        )}
        {recentNotes.length > 0 && (
          <ul className="surface overflow-hidden rounded-2xl">
            {recentNotes.map((note) => (
              <li key={note.id} className="[&+&]:border-t [&+&]:border-border/60">
                <NoteRow note={note} onOpen={() => setNoteView({ kind: 'edit', id: note.id })} />
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
            position={positionOf.get(open.assetId)}
            payouts={allPayouts.filter((p) => p.assetId === open.assetId)}
            onNewNote={() => openNote({ kind: 'new', asset: assetOf(open) })}
            onOpenNote={(id) => openNote({ kind: 'edit', id })}
            onNewPayout={() => openPayout({ kind: 'new', asset: assetOf(open) })}
            onOpenPayout={(id) => openPayout({ kind: 'edit', id })}
            onRemove={() => removeHolding(open)}
          />
        )}
      </BottomSheet>

      <PayoutSheet view={payoutView} onClose={() => setPayoutView(null)} />
      <TradeNoteSheet view={noteView} fallbackBroker={recentNotes[0]?.broker ?? null} onClose={() => setNoteView(null)} />
    </main>
  )
}

type ListedHolding = Holding & { fromPositionOnly?: true }

// O que o evento fez com a quantidade: "110 → 880 cotas", "880 cotas saíram", "704 cotas entraram".
function eventDetail(step: PositionStep, kind: NonNullable<Holding['kind']>): string {
  if (step.kind === 'Split') return `${step.quantity.toLocaleString('pt-BR')} → ${unitsLabel(kind, step.quantityAfter)}`
  if (step.kind === 'ConversionOut') return `${unitsLabel(kind, step.quantity)} saíram`
  return `${unitsLabel(kind, step.quantity)} entraram`
}

function HoldingDetails({
  holding,
  position,
  payouts,
  onNewNote,
  onOpenNote,
  onNewPayout,
  onOpenPayout,
  onRemove,
}: {
  holding: ListedHolding
  position: Position | undefined
  payouts: Payout[]
  onNewNote: () => void
  onOpenNote: (id: string) => void
  onNewPayout: () => void
  onOpenPayout: (id: string) => void
  onRemove: () => void
}) {
  const total = payouts.reduce((sum, p) => sum + p.amountCents, 0)
  const history = usePositionHistory(position ? holding.assetId : null)
  // Do mais recente ao mais antigo, como as outras listas.
  const steps = [...(history.data ?? [])].reverse()
  // Decisão do dono: com ações pelas notas, o ativo não sai da carteira.
  const canRemove = !holding.fromPositionOnly && (!position || position.quantity === 0)
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
          {position && (
            <>
              <Row label="Quantidade">{position.quantity === 0 ? 'zerado' : unitsLabel(holding.kind!, position.quantity)}</Row>
              {position.averagePriceCents !== null && <Row label="Preço médio">{formatCents(position.averagePriceCents)}</Row>}
              {position.averageCostCents !== null && (
                <Row label="Preço médio com custos">{formatCents(position.averageCostCents)}</Row>
              )}
              {position.quantity > 0 && <Row label="Custo total">{formatCents(position.costBasisCents)}</Row>}
              {history.data?.some((s) => s.side === 'Sell') && (
                <Row label="Resultado das vendas">{signedCents(position.realizedCents)}</Row>
              )}
            </>
          )}
          <Row label="Tipo">{assetKindLabel(holding.kind)}</Row>
          <Row label="Proventos recebidos">{formatCents(total)}</Row>
          {!holding.isActive && <p className="text-muted-foreground">Este ativo saiu da bolsa.</p>}
        </dl>
        {steps.length > 0 && (
          <section className="flex flex-col gap-1">
            <h3 className="px-2 text-xs font-medium text-muted-foreground">Histórico</h3>
            <ul className="flex flex-col">
              {steps.map((step, index) => (
                <li key={`${step.noteId ?? step.kind}-${step.side}-${index}`}>
                  {step.noteId ? (
                    <button
                      type="button"
                      onClick={() => onOpenNote(step.noteId!)}
                      className="flex w-full items-center gap-3 rounded-xl px-2 py-2 text-left transition-colors hover:bg-muted/50 active:bg-muted/70"
                    >
                      <span className="min-w-0 flex-1">
                        <span className="block text-sm font-medium">{stepTitle(step, holding.kind!)}</span>
                        <span className="block truncate text-xs text-muted-foreground">
                          {formatShortDate(step.date)} · {step.broker}
                        </span>
                      </span>
                      <span className="flex shrink-0 flex-col items-end">
                        <span className="text-sm tabular-nums">{formatCents(step.grossCents)}</span>
                        {step.realizedCents !== null && (
                          <span className="text-xs text-muted-foreground tabular-nums">
                            resultado {signedCents(step.realizedCents)}
                          </span>
                        )}
                      </span>
                      <ChevronRight className="size-4 shrink-0 text-muted-foreground" />
                    </button>
                  ) : (
                    // Evento da empresa (I.7c1): não tem nota para abrir.
                    <div className="flex w-full items-center gap-3 px-2 py-2">
                      <span className="min-w-0 flex-1">
                        <span className="block text-sm font-medium">{stepTitle(step, holding.kind!)}</span>
                        <span className="block truncate text-xs text-muted-foreground tabular-nums">
                          {formatShortDate(step.date)} · {eventDetail(step, holding.kind!)}
                        </span>
                      </span>
                    </div>
                  )}
                </li>
              ))}
            </ul>
          </section>
        )}
        {payouts.length > 0 && (
          <section className="flex flex-col gap-1">
            <h3 className="px-2 text-xs font-medium text-muted-foreground">Proventos</h3>
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
          </section>
        )}
        {canRemove && (
          <Button variant="ghost" size="sm" className="self-start rounded-full text-muted-foreground" onClick={onRemove}>
            Tirar da carteira
          </Button>
        )}
      </div>
      <div className="flex gap-2 px-4 pt-1 pb-6">
        <Button variant="secondary" className="flex-1 rounded-full" onClick={onNewPayout}>
          <Plus />
          Novo provento
        </Button>
        <Button className="flex-1 rounded-full" onClick={onNewNote}>
          <Plus />
          Nova nota
        </Button>
      </div>
    </>
  )
}

function Row({ label, children }: { label: string; children: ReactNode }) {
  return (
    <div className="flex justify-between gap-3">
      <dt className="text-muted-foreground">{label}</dt>
      <dd className="text-right tabular-nums">{children}</dd>
    </div>
  )
}

// O resultado da venda não é receita: sinal em tinta, sem o espectro.
const signedCents = (cents: number) => (cents < 0 ? formatCents(cents).replace('-', '−') : `+${formatCents(cents)}`)

const assetOf = (h: Holding): AssetItem => ({
  id: h.assetId,
  symbol: h.symbol,
  name: h.name,
  kind: h.kind,
  isActive: h.isActive,
  hasLogo: h.hasLogo,
})

// Com notas: código e quantidade, os dois preços médios e o que pagou de proventos (decisão do dono, I.7b).
function PositionText({ holding, position, payoutsCents }: { holding: Holding; position: Position; payoutsCents: number }) {
  const prices = priceParts(position)
  return (
    <span className="flex min-w-0 flex-1 flex-col">
      <span className="flex items-baseline justify-between gap-2">
        <span className="font-medium">{holding.symbol}</span>
        <span className="shrink-0 text-sm tabular-nums">
          {position.quantity === 0 ? 'zerado' : unitsLabel(holding.kind!, position.quantity)}
        </span>
      </span>
      {prices ? (
        <span className="text-sm text-muted-foreground tabular-nums">
          <span className="whitespace-nowrap">{prices[0]}</span> · <span className="whitespace-nowrap">{prices[1]}</span>
        </span>
      ) : (
        <span className="truncate text-sm text-muted-foreground">{holding.name}</span>
      )}
      {payoutsCents > 0 && (
        <span className="text-xs text-muted-foreground">
          <Amount type="Income" cents={payoutsCents} className="text-xs" /> em proventos
        </span>
      )}
    </span>
  )
}

// Uma nota: a corretora, o que fez e com quais ativos, o dia e quanto movimentou.
function NoteRow({ note, onOpen }: { note: TradeNote; onOpen: () => void }) {
  const { buyCents, sellCents } = noteTotals(note.trades)
  return (
    <button
      type="button"
      onClick={onOpen}
      className="flex w-full items-center gap-3 px-4 py-3 text-left transition-colors outline-none hover:bg-muted/40 focus-visible:bg-muted/60 active:bg-muted/70"
    >
      <AccountTile name={note.broker} type="Investment" />
      <span className="flex min-w-0 flex-1 flex-col">
        <span className="truncate font-medium">{noteTitle(note.trades)}</span>
        <span className="truncate text-sm text-muted-foreground">
          {formatShortDate(note.date)} · {note.broker}
        </span>
      </span>
      <span className="shrink-0 text-sm tabular-nums">{formatCents(buyCents + sellCents)}</span>
    </button>
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
