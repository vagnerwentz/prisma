import { ArrowLeft, ChevronLeft, ChevronRight } from 'lucide-react'
import { useMemo, useState } from 'react'
import { Link, useSearchParams } from 'react-router'
import { toast } from 'sonner'
import { Tabs } from 'radix-ui'
import { BottomSheet } from '@/components/BottomSheet'
import { Button } from '@/components/ui/button'
import { SheetTitle } from '@/components/ui/sheet'
import { todayInSaoPaulo } from '@/lib/dates'
import { cn } from '@/lib/utils'
import { AssetPicker } from './AssetPicker'
import { ArrivedRequests } from './AssetRequestList'
import { EventReviewList } from './EventReviewList'
import type { AssetItem } from './assets'
import { HoldingDetails, type ListedHolding } from './HoldingSheet'
import { assetOf } from './holdings'
import { GoalsTab } from './GoalsTab'
import { NotesTab } from './NotesTab'
import { PayoutSheet, type PayoutView } from './PayoutSheet'
import { PayoutsTab } from './PayoutsTab'
import { readView, viewSearch, type PortfolioAsset, type Tab } from './portfolio'
import { PortfolioTab } from './PortfolioTab'
import {
  useAddHolding,
  useAssetRequests,
  useHoldings,
  usePayouts,
  usePortfolio,
  usePositions,
  useRemoveHolding,
  useTradeNotes,
  type Holding,
} from './queries'
import { TradeNoteSheet, type TradeNoteView } from './TradeNoteSheet'

const tabLabels: Record<Tab, string> = { carteira: 'Carteira', proventos: 'Proventos', notas: 'Notas', objetivos: 'Objetivos' }

// Investimentos, dentro de Contas (docs/investimentos.md). Três abas (I.7e): a Carteira (quanto está investido, o
// espectro por tipo e cada ativo com o peso), os Proventos (com filtros, mês a mês e ano a ano) e as Notas. O ano
// escolhido vale para as três, e a aba e o ano ficam no endereço (?aba=proventos&ano=2020). Os avisos de ativo que
// chegou (I.7c3) ficam acima das abas: pedem ação.
export function InvestmentsPage() {
  const today = todayInSaoPaulo()
  const currentYear = Number(today.slice(0, 4))
  const [search, setSearch] = useSearchParams()
  const { tab, year } = readView(search, currentYear)
  const goalParam = search.get('objetivo')
  const go = (next: { tab?: Tab; year?: number; goal?: string | null }) =>
    setSearch(viewSearch(next.tab ?? tab, next.year ?? year, currentYear, next.goal === undefined ? goalParam : next.goal), {
      replace: true,
    })

  const portfolio = usePortfolio(year)
  const holdings = useHoldings()
  const positions = usePositions()
  const payouts = usePayouts()
  const lastNote = useTradeNotes({ take: 1 })
  const requests = useAssetRequests().data ?? []
  const add = useAddHolding()
  const remove = useRemoveHolding()
  const [adding, setAdding] = useState(false)
  const [open, setOpen] = useState<ListedHolding | null>(null)
  const [payoutView, setPayoutView] = useState<PayoutView | null>(null)
  const [noteView, setNoteView] = useState<TradeNoteView | null>(null)

  const holdingList = useMemo(() => holdings.data ?? [], [holdings.data])
  const allPayouts = useMemo(() => payouts.data ?? [], [payouts.data])
  const positionOf = useMemo(() => new Map((positions.data ?? []).map((p) => [p.assetId, p])), [positions.data])
  const years = portfolio.data?.years ?? [currentYear]
  // Ativos com nota ou evento, mesmo zerados: os outros da carteira são os postos à mão ("Sem notas").
  // Enquanto as posições não chegam, null: sem ela, um ativo convertido apareceria por um instante em "Sem notas".
  const trackedAssetIds = useMemo(() => (positions.data ? new Set(positions.data.map((p) => p.assetId)) : null), [positions.data])

  // O painel do ativo funciona igual para quem está na carteira e para quem só veio das notas (conversão, venda).
  const listed = (asset: {
    assetId: string
    symbol: string
    name: string
    kind: Holding['kind']
    isActive: boolean
    hasLogo: boolean
  }) =>
    holdingList.find((h) => h.assetId === asset.assetId) ??
    ({ id: `p-${asset.assetId}`, addedAt: '', fromPositionOnly: true, ...asset } satisfies ListedHolding)

  const addAsset = (asset: AssetItem) => {
    setAdding(false)
    if (holdingList.some((h) => h.assetId === asset.id) || positionOf.get(asset.id)?.quantity) {
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
    const hasPayouts = allPayouts.some((p) => p.assetId === holding.assetId)
    remove.mutate(holding.id, {
      onSuccess: () =>
        toast(`${holding.symbol} saiu da carteira`, {
          description: hasPayouts ? 'Os proventos dele continuam nos lançamentos.' : undefined,
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

      <EventReviewList />
      <ArrivedRequests requests={requests} onLaunch={(asset) => setNoteView({ kind: 'new', asset })} />

      <Tabs.Root value={tab} onValueChange={(value) => go({ tab: value as Tab })} className="flex flex-col gap-5">
        <div className="flex flex-col gap-4">
          <Tabs.List aria-label="Investimentos" className="surface grid grid-cols-4 gap-1 rounded-full p-1">
            {(Object.keys(tabLabels) as Tab[]).map((t) => (
              <Tabs.Trigger
                key={t}
                value={t}
                className={cn(
                  'h-9 rounded-full border-2 border-transparent text-[13px] transition-colors min-[360px]:text-sm outline-none focus-visible:ring-2 focus-visible:ring-ring/50',
                  t === tab ? 'spectrum-ring font-medium text-foreground' : 'text-muted-foreground hover:text-foreground',
                )}
              >
                {tabLabels[t]}
              </Tabs.Trigger>
            ))}
          </Tabs.List>
          {tab !== 'objetivos' && <YearSwitcher year={year} first={years[0]} last={currentYear} onChange={(y) => go({ year: y })} />}
        </div>

        <Tabs.Content value="carteira" className="outline-none">
          <PortfolioTab
            portfolio={portfolio.data}
            stale={portfolio.isPlaceholderData}
            error={portfolio.error}
            today={today}
            holdings={holdingList}
            trackedAssetIds={trackedAssetIds}
            onOpenAsset={(asset: PortfolioAsset) => setOpen(listed(asset))}
            onOpenHolding={setOpen}
            onOpenGoal={(goal) => go({ tab: 'objetivos', goal })}
            onAdd={() => setAdding(true)}
            onNewNote={() => setNoteView({ kind: 'new' })}
          />
        </Tabs.Content>
        <Tabs.Content value="proventos" className="outline-none">
          <PayoutsTab
            payouts={allPayouts}
            pending={payouts.isPending}
            error={payouts.error}
            years={years}
            year={year}
            today={today}
            onSelectYear={(y) => go({ year: y })}
            onOpenPayout={setPayoutView}
            onOpenAsset={(rank) =>
              setOpen(
                listed({
                  assetId: rank.assetId,
                  symbol: rank.symbol,
                  name: rank.assetName,
                  kind: rank.assetKind,
                  isActive: holdingList.find((h) => h.assetId === rank.assetId)?.isActive ?? true,
                  hasLogo: rank.assetHasLogo,
                }),
              )
            }
          />
        </Tabs.Content>
        <Tabs.Content value="notas" className="outline-none">
          <NotesTab
            year={year}
            requests={requests}
            onOpenNote={(id) => setNoteView({ kind: 'edit', id })}
            onNewNote={() => setNoteView({ kind: 'new' })}
          />
        </Tabs.Content>
        <Tabs.Content value="objetivos" className="outline-none">
          <GoalsTab goalParam={goalParam} onOpen={(goal) => go({ goal })} />
        </Tabs.Content>
      </Tabs.Root>

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
      <TradeNoteSheet view={noteView} fallbackBroker={lastNote.data?.[0]?.broker ?? null} onClose={() => setNoteView(null)} />
    </main>
  )
}

// O ano da tela, como o mês do Resumo: ‹ 2020 ›. Não passa do ano corrente nem volta antes do primeiro com nota ou
// provento.
function YearSwitcher({
  year,
  first,
  last,
  onChange,
}: {
  year: number
  first: number
  last: number
  onChange: (y: number) => void
}) {
  return (
    <div className="flex items-center justify-between">
      <p className="flex items-baseline gap-2 px-1">
        <span className="font-display text-3xl leading-none tabular-nums">{year}</span>
        {year === last && <span className="text-sm text-muted-foreground">até hoje</span>}
      </p>
      <div className="flex gap-1">
        <Button
          variant="ghost"
          size="icon"
          className="rounded-full"
          aria-label="Ano anterior"
          disabled={year <= first}
          onClick={() => onChange(year - 1)}
        >
          <ChevronLeft />
        </Button>
        <Button
          variant="ghost"
          size="icon"
          className="rounded-full"
          aria-label="Próximo ano"
          disabled={year >= last}
          onClick={() => onChange(year + 1)}
        >
          <ChevronRight />
        </Button>
      </div>
    </div>
  )
}
