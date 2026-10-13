import { Plus, X } from 'lucide-react'
import { useState } from 'react'
import { AssetTile } from '@/components/brand/Tiles'
import { StaleFade } from '@/components/StaleFade'
import { Alert, AlertDescription } from '@/components/ui/alert'
import { Button } from '@/components/ui/button'
import { Skeleton } from '@/components/ui/skeleton'
import { Amount } from '@/features/transactions/Amount'
import { formatCents } from '@/lib/money'
import { holdingLogo, signedCents } from './holdings'
import { assetKindLabel, logoSrc } from './assets'
import {
  asOfLabel,
  assetCountLabel,
  formatWeight,
  groupColors,
  groupLabels,
  holdingsWithoutNotes,
  splitAssets,
  type Portfolio,
  type PortfolioAsset,
  type PortfolioGroup,
} from './portfolio'
import { PrismSpectrum } from './PrismSpectrum'
import type { Holding } from './queries'
import { unitsLabel } from './trades'

// A aba Carteira (docs/investimentos.md, I.7e): quanto está investido no ano escolhido, o espectro por grupo e cada
// ativo com o peso dele. Embaixo, o que saiu naquele ano e, no ano corrente, os postos à mão sem nenhuma nota.
export function PortfolioTab({
  portfolio,
  stale,
  error,
  today,
  holdings,
  trackedAssetIds,
  onOpenAsset,
  onOpenHolding,
  onAdd,
  onNewNote,
}: {
  portfolio: Portfolio | undefined
  stale: boolean
  error: Error | null
  today: string
  holdings: Holding[]
  trackedAssetIds: Set<string> | null
  onOpenAsset: (asset: PortfolioAsset) => void
  onOpenHolding: (holding: Holding) => void
  onAdd: () => void
  onNewNote: () => void
}) {
  const [group, setGroup] = useState<PortfolioGroup | null>(null)

  if (error) {
    return (
      <Alert variant="destructive">
        <AlertDescription>{error.message || 'Não foi possível carregar sua carteira.'}</AlertDescription>
      </Alert>
    )
  }
  if (!portfolio) return <Skeleton className="h-96 w-full rounded-3xl" />

  const current = portfolio.asOf === today
  // O grupo escolhido que não existe no ano novo deixa de filtrar.
  const chosen = group && portfolio.groups.some((g) => g.group === group && g.investedCents > 0) ? group : null
  const { held, gone } = splitAssets(portfolio.assets, chosen)
  const listed = new Set(portfolio.assets.map((a) => a.assetId))
  // Postos à mão, sem nota nem evento: só no ano corrente. O que zerou antes é histórico do ano em que saiu.
  const loose = current && chosen === null && trackedAssetIds ? holdingsWithoutNotes(holdings, listed, trackedAssetIds) : []
  // Sem nenhum ativo com quantidade no ano corrente, o "Adicionar" vai para o título da primeira seção que aparecer.
  const addInGone = current && held.length === 0 && chosen === null && gone.length > 0
  const addInLoose = current && held.length === 0 && chosen === null && gone.length === 0
  const heldCount = portfolio.assets.filter((a) => a.quantity > 0).length

  if (portfolio.assets.length === 0 && loose.length === 0) {
    return (
      <StaleFade stale={stale}>
        <div className="flex flex-col items-center gap-4 rounded-3xl border border-dashed px-6 py-12 text-center">
          <div className="flex flex-col gap-1">
            <p className="font-display text-2xl">
              {current ? 'Seus ativos ficam aqui' : `Nada na carteira em ${portfolio.year}`}
            </p>
            <p className="text-sm text-muted-foreground">
              {current ? 'Lance suas notas de corretagem para ver quanto tem e como está dividido.' : 'Escolha outro ano.'}
            </p>
          </div>
          {current && (
            <div className="flex flex-wrap justify-center gap-2">
              <Button variant="secondary" className="rounded-full" onClick={onAdd}>
                <Plus />
                Adicionar ativo
              </Button>
              <Button className="rounded-full" onClick={onNewNote}>
                <Plus />
                Nova nota
              </Button>
            </div>
          )}
        </div>
      </StaleFade>
    )
  }

  return (
    <StaleFade stale={stale} className="flex flex-col gap-6">
      <section className="surface flex flex-col gap-5 rounded-3xl px-5 pt-5 pb-4">
        <div className="flex flex-col gap-1">
          <h2 className="text-sm font-medium text-foreground/75">Investido {asOfLabel(portfolio.asOf, today)}</h2>
          <p className="font-display text-5xl leading-none tabular-nums">{formatCents(portfolio.investedCents)}</p>
          <p className="flex flex-wrap gap-x-3 gap-y-0.5 pt-1 text-sm text-muted-foreground">
            <span>{assetCountLabel(heldCount)}</span>
            {portfolio.payoutsCents > 0 && (
              <span>
                <Amount type="Income" cents={portfolio.payoutsCents} className="font-medium" /> em proventos
              </span>
            )}
            {portfolio.realizedCents !== 0 && (
              <span>
                <span className="font-medium text-foreground tabular-nums">{signedCents(portfolio.realizedCents)}</span> nas
                vendas
              </span>
            )}
          </p>
        </div>
        {portfolio.investedCents > 0 && <PrismSpectrum groups={portfolio.groups} selected={chosen} onSelect={setGroup} />}
      </section>

      {held.length > 0 && (
        <section className="flex flex-col gap-3">
          <div className="flex min-h-9 items-end justify-between gap-2">
            {chosen ? (
              <button
                type="button"
                onClick={() => setGroup(null)}
                className="flex items-center gap-1.5 rounded-full px-1 text-sm font-medium text-foreground/75 hover:text-foreground"
              >
                <span className="h-2.5 w-2.5 rounded-[3px]" style={{ background: groupColors[chosen] }} />
                {groupLabels[chosen]}
                <X className="size-3.5" aria-label="Ver todos" />
              </button>
            ) : (
              <h2 className="px-1 text-sm font-medium text-foreground/75">Meus ativos</h2>
            )}
            {current && (
              <Button size="sm" variant="secondary" className="rounded-full" onClick={onAdd}>
                <Plus />
                Adicionar
              </Button>
            )}
          </div>
          <ul className="surface overflow-hidden rounded-2xl">
            {held.map((asset) => (
              <li key={asset.assetId} className="[&+&]:border-t [&+&]:border-border/60">
                <HeldRow asset={asset} onOpen={() => onOpenAsset(asset)} />
              </li>
            ))}
          </ul>
        </section>
      )}

      {gone.length > 0 && (
        <section className="flex flex-col gap-3">
          <SectionHeader title={`Saíram em ${portfolio.year}`} onAdd={addInGone ? onAdd : undefined} />
          <ul className="surface overflow-hidden rounded-2xl">
            {gone.map((asset) => (
              <li key={asset.assetId} className="[&+&]:border-t [&+&]:border-border/60">
                <GoneRow asset={asset} year={portfolio.year} onOpen={() => onOpenAsset(asset)} />
              </li>
            ))}
          </ul>
        </section>
      )}

      {loose.length > 0 && (
        <section className="flex flex-col gap-3">
          <SectionHeader title="Sem notas" onAdd={addInLoose ? onAdd : undefined} />
          <ul className="surface overflow-hidden rounded-2xl">
            {loose.map((holding) => (
              <li key={holding.id} className="[&+&]:border-t [&+&]:border-border/60">
                <button type="button" onClick={() => onOpenHolding(holding)} className={rowClass}>
                  <AssetTile symbol={holding.symbol} logo={holdingLogo(holding)} />
                  <span className="flex min-w-0 flex-1 flex-col">
                    <span className="font-medium">{holding.symbol}</span>
                    <span className="truncate text-sm text-muted-foreground">{holding.name}</span>
                  </span>
                  <span className="shrink-0 text-xs text-muted-foreground">
                    {holding.isActive ? assetKindLabel(holding.kind) : 'saiu da bolsa'}
                  </span>
                </button>
              </li>
            ))}
          </ul>
        </section>
      )}
    </StaleFade>
  )
}

// Título de seção com o "Adicionar" ao lado, quando ele mora nesta seção.
function SectionHeader({ title, onAdd }: { title: string; onAdd?: () => void }) {
  return (
    <div className="flex min-h-9 items-end justify-between gap-2">
      <h2 className="px-1 text-sm font-medium text-foreground/75">{title}</h2>
      {onAdd && (
        <Button size="sm" variant="secondary" className="rounded-full" onClick={onAdd}>
          <Plus />
          Adicionar
        </Button>
      )}
    </div>
  )
}

const rowClass =
  'flex w-full items-center gap-3 px-4 py-3 text-left transition-colors outline-none hover:bg-muted/40 focus-visible:bg-muted/60 active:bg-muted/70'

const assetLogo = (a: PortfolioAsset) => logoSrc({ id: a.assetId, kind: a.kind, hasLogo: a.hasLogo })

// O ativo com o custo, a quantidade e o peso na carteira; a barra na cor do grupo.
function HeldRow({ asset, onOpen }: { asset: PortfolioAsset; onOpen: () => void }) {
  return (
    <button type="button" onClick={onOpen} className={`${rowClass} flex-col items-stretch gap-2`}>
      <span className="flex items-center gap-3">
        <AssetTile symbol={asset.symbol} logo={assetLogo(asset)} />
        <span className="flex min-w-0 flex-1 flex-col">
          <span className="flex items-baseline justify-between gap-2">
            <span className="font-medium">{asset.symbol}</span>
            <span className="shrink-0 tabular-nums">{formatCents(asset.costBasisCents)}</span>
          </span>
          <span className="flex items-baseline justify-between gap-2 text-sm text-muted-foreground">
            <span className="truncate tabular-nums">
              {unitsLabel(asset.kind ?? 'Unknown', asset.quantity)}
              {!asset.isActive && ' · saiu da bolsa'}
            </span>
            <span className="shrink-0 tabular-nums">{formatWeight(asset.weightBasisPoints)}</span>
          </span>
        </span>
      </span>
      <span className="h-1 w-full overflow-hidden rounded-full bg-muted">
        <span
          className="meter-fill block h-full"
          style={{ background: groupColors[asset.group], ['--fill' as string]: `${asset.weightBasisPoints / 100}%` }}
        />
      </span>
    </button>
  )
}

// Saiu no ano (vendido ou convertido) ou só rendeu nele: o resultado e os proventos do ano.
function GoneRow({ asset, year, onOpen }: { asset: PortfolioAsset; year: number; onOpen: () => void }) {
  return (
    <button type="button" onClick={onOpen} className={rowClass}>
      <AssetTile symbol={asset.symbol} logo={assetLogo(asset)} />
      <span className="flex min-w-0 flex-1 flex-col">
        <span className="font-medium">{asset.symbol}</span>
        <span className="truncate text-sm text-muted-foreground">{asset.name}</span>
      </span>
      <span className="flex shrink-0 flex-col items-end text-xs text-muted-foreground">
        {asset.realizedCents !== 0 && (
          <span>
            <span className="text-sm font-medium text-foreground tabular-nums">{signedCents(asset.realizedCents)}</span> nas
            vendas
          </span>
        )}
        {asset.payoutsCents > 0 && (
          <span>
            <Amount type="Income" cents={asset.payoutsCents} className="text-sm" /> em {year}
          </span>
        )}
      </span>
    </button>
  )
}
