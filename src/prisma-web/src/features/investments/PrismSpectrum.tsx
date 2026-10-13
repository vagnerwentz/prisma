import { useId } from 'react'
import { Amount } from '@/features/transactions/Amount'
import { formatCents } from '@/lib/money'
import { cn } from '@/lib/utils'
import {
  assetCountLabel,
  formatWeight,
  groupColors,
  groupLabels,
  prismBands,
  type PortfolioGroup,
  type PortfolioSlice,
} from './portfolio'

// O espectro da carteira (docs/investimentos.md, I.7e): a luz entra no prisma e sai dividida em faixas, uma por grupo,
// com a espessura do peso. A luz é tinta no claro e branca no escuro (currentColor). Tocar numa faixa ou na linha da
// legenda escolhe o grupo; tocar de novo volta a tudo. A luz atravessa uma vez, quando o espectro aparece.
const Width = 340
const Height = 150
const Pad = 8
// O prisma: vértice no alto, base embaixo. A luz entra no meio da face esquerda e sai pela face direita.
const Apex = { x: 112, y: 14 }
const BaseLeft = { x: 66, y: 136 }
const BaseRight = { x: 158, y: 136 }
const Entry = { x: (Apex.x + BaseLeft.x) / 2, y: (Apex.y + BaseLeft.y) / 2 }
// Trecho da face direita por onde as cores saem.
const ExitTop = 62
const ExitBottom = 92
const rightFaceX = (y: number) => Apex.x + ((y - Apex.y) * (BaseRight.x - Apex.x)) / (BaseRight.y - Apex.y)

export function PrismSpectrum({
  groups,
  selected,
  onSelect,
}: {
  groups: PortfolioSlice[]
  selected: PortfolioGroup | null
  onSelect: (group: PortfolioGroup | null) => void
}) {
  const id = useId()
  const span = Height - 2 * Pad
  const bands = prismBands(groups, span, 3, 4)
  const exit = (y: number) => ExitTop + (y / span) * (ExitBottom - ExitTop)
  const toggle = (group: PortfolioGroup) => onSelect(selected === group ? null : group)

  return (
    <div className="flex flex-col gap-4 sm:flex-row sm:items-center">
      <svg
        viewBox={`0 0 ${Width} ${Height}`}
        className="prism-reveal w-full text-foreground sm:w-1/2 sm:shrink-0"
        role="img"
        aria-label="Espectro da carteira por tipo de ativo"
      >
        <defs>
          <linearGradient id={`${id}-beam`} x1="0" x2="1">
            <stop offset="0" stopColor="currentColor" stopOpacity="0" />
            <stop offset="1" stopColor="currentColor" stopOpacity="0.85" />
          </linearGradient>
          <linearGradient id={`${id}-glass`} x1="0" y1="0" x2="1" y2="1">
            <stop offset="0" stopColor="currentColor" stopOpacity="0.09" />
            <stop offset="1" stopColor="currentColor" stopOpacity="0.02" />
          </linearGradient>
          {bands.map((b) => (
            <linearGradient key={b.group} id={`${id}-${b.group}`} x1={rightFaceX(exit(b.y0))} x2={Width} gradientUnits="userSpaceOnUse">
              <stop offset="0" stopColor={groupColors[b.group]} stopOpacity="0.35" />
              <stop offset="1" stopColor={groupColors[b.group]} stopOpacity="0.95" />
            </linearGradient>
          ))}
        </defs>

        {/* A luz que entra e o caminho dela dentro do vidro. */}
        <polygon points={`0,${Entry.y - 2.5} ${Entry.x},${Entry.y - 1.5} ${Entry.x},${Entry.y + 1.5} 0,${Entry.y + 2.5}`} fill={`url(#${id}-beam)`} />
        <polygon
          points={`${Entry.x},${Entry.y - 1.5} ${rightFaceX(ExitTop)},${ExitTop} ${rightFaceX(ExitBottom)},${ExitBottom} ${Entry.x},${Entry.y + 1.5}`}
          fill="currentColor"
          fillOpacity="0.12"
        />

        {bands.map((b) => {
          const e0 = exit(b.y0)
          const e1 = exit(b.y1)
          const dimmed = selected !== null && selected !== b.group
          return (
            <polygon
              key={b.group}
              points={`${rightFaceX(e0)},${e0} ${Width},${Pad + b.y0} ${Width},${Pad + b.y1} ${rightFaceX(e1)},${e1}`}
              fill={`url(#${id}-${b.group})`}
              className="cursor-pointer transition-opacity duration-200 ease-out"
              opacity={dimmed ? 0.18 : 1}
              onClick={() => toggle(b.group)}
            />
          )
        })}

        <polygon
          points={`${Apex.x},${Apex.y} ${BaseRight.x},${BaseRight.y} ${BaseLeft.x},${BaseLeft.y}`}
          fill={`url(#${id}-glass)`}
          stroke="currentColor"
          strokeOpacity="0.4"
          strokeWidth="1.25"
          strokeLinejoin="round"
        />
      </svg>

      <ul className="flex flex-col gap-0.5 sm:flex-1">
        {groups
          .filter((g) => g.investedCents > 0)
          .map((g) => {
            const active = selected === g.group
            return (
              <li key={g.group}>
                <button
                  type="button"
                  aria-pressed={active}
                  onClick={() => toggle(g.group)}
                  className={cn(
                    '-mx-2 flex w-[calc(100%+1rem)] items-center gap-3 rounded-xl px-2 py-1.5 text-left transition-[opacity,background-color] duration-200 ease-out',
                    active ? 'bg-muted/70' : 'hover:bg-muted/40',
                    selected !== null && !active && 'opacity-45',
                  )}
                >
                  <span className="h-3 w-3 shrink-0 rounded-[4px]" style={{ background: groupColors[g.group] }} />
                  <span className="flex min-w-0 flex-1 flex-col">
                    <span className="text-sm font-medium">{groupLabels[g.group]}</span>
                    <span className="truncate text-xs text-muted-foreground">
                      {assetCountLabel(g.assetCount)}
                      {g.payoutsCents > 0 && (
                        <>
                          {' · '}
                          <Amount type="Income" cents={g.payoutsCents} className="text-xs font-medium" />
                        </>
                      )}
                    </span>
                  </span>
                  <span className="flex shrink-0 flex-col items-end">
                    <span className="text-sm font-medium tabular-nums">{formatWeight(g.weightBasisPoints)}</span>
                    <span className="text-xs text-muted-foreground tabular-nums">{formatCents(g.investedCents)}</span>
                  </span>
                </button>
              </li>
            )
          })}
      </ul>
    </div>
  )
}
