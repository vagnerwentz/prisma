import { ChevronRight, Plus } from 'lucide-react'
import type { ReactNode } from 'react'
import { AssetTile } from '@/components/brand/Tiles'
import { Button } from '@/components/ui/button'
import { SheetTitle } from '@/components/ui/sheet'
import { Amount } from '@/features/transactions/Amount'
import { formatShortDate } from '@/lib/dates'
import { formatCents } from '@/lib/money'
import { assetKindLabel } from './assets'
import { holdingLogo, signedCents } from './holdings'
import { payoutKindLabels, type Payout, type PayoutKind } from './payouts'
import { usePositionHistory, type Holding } from './queries'
import { eventDetail, liquidationLine, stepTitle, unitsLabel, type Position } from './trades'

// O painel de um ativo (docs/investimentos.md, I.7b2): posição, histórico das notas e dos eventos, proventos.
export type ListedHolding = Holding & { fromPositionOnly?: true }

export function HoldingDetails({
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
                        {/* Liquidação (I.7c4): a origem saiu como venda, com resultado. */}
                        {liquidationLine(step) && (
                          <span className="block text-xs text-muted-foreground tabular-nums">{liquidationLine(step)}</span>
                        )}
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
