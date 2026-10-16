import { useState, type CSSProperties, type ReactNode } from 'react'
import { Amount } from '@/features/transactions/Amount'
import { formatCents } from '@/lib/money'
import { cn } from '@/lib/utils'
import { Cta, CtaNote } from './parts'

// A luz branca entra no prisma e sai decomposta em pedaços do seu dinheiro (docs/landing.md, D10): cada faixa termina
// numa linha do app, com um valor do usuário de exemplo das telas, e leva à seção que explica aquilo. Passar o dedo ou o
// mouse numa linha acende a faixa dela. A cena toca uma vez, ao abrir a página (D8); com menos movimento, já aparece
// pronta (landing.css).
const Width = 600
const Height = 360
type Point = { x: number; y: number }
const Apex = { x: 112, y: 70 }
const BaseLeft = { x: 16, y: 282 }
const BaseRight = { x: 208, y: 282 }
const along = (a: Point, b: Point, t: number) => ({ x: a.x + (b.x - a.x) * t, y: a.y + (b.y - a.y) * t })
// A luz entra pela face esquerda e sai por um trecho da face direita (como no logo, PrismLogo.tsx).
const Entry = along(Apex, BaseLeft, 0.56)
const ExitTop = 150
const ExitBottom = 196
// Onde o cartão começa (as faixas terminam nele).
const CardLeft = 268
const CardTop = 24
const CardBottom = 336
const rightFaceX = (y: number) => Apex.x + ((y - Apex.y) * (BaseRight.x - Apex.x)) / (BaseRight.y - Apex.y)
const BeamStart = { x: 0, y: Entry.y + 22 }

// Prisma de cantos arredondados: cada canto vira uma curva que começa a `radius` do vértice.
function roundedTriangle(points: Point[], radius: number) {
  const toward = (from: Point, to: Point) => {
    const length = Math.hypot(to.x - from.x, to.y - from.y)
    return { x: from.x + ((to.x - from.x) / length) * radius, y: from.y + ((to.y - from.y) / length) * radius }
  }
  return (
    points
      .map((corner, i) => {
        const before = toward(corner, points[(i + points.length - 1) % points.length])
        const after = toward(corner, points[(i + 1) % points.length])
        return `${i ? 'L' : 'M'}${before.x},${before.y} Q${corner.x},${corner.y} ${after.x},${after.y}`
      })
      .join(' ') + ' Z'
  )
}
const prism = roundedTriangle([Apex, BaseRight, BaseLeft], 12)

type Piece = { color: string; href: string; label: string; detail: string; value: ReactNode }

const pieces: Piece[] = [
  {
    color: '#8b5cf6',
    href: '#fatura',
    label: 'Fatura',
    detail: 'vence 10/11',
    value: <span className="font-medium tabular-nums">{formatCents(115640)}</span>,
  },
  { color: '#3b82f6', href: '#parcelas', label: 'Notebook', detail: 'parcela 3/10', value: <Amount type="Expense" cents={35990} /> },
  { color: '#22d3ee', href: '#dia-a-dia', label: 'iFood', detail: 'Pix, hoje', value: <Amount type="Expense" cents={4290} /> },
  { color: '#84cc16', href: '#investimentos', label: 'MXRF11', detail: 'rendimento', value: <Amount type="Income" cents={5000} /> },
]

// Cada linha do cartão ocupa um quarto da altura dele; a faixa chega no meio da linha. As faixas saem coladas da face
// direita e se abrem em curva até a linha de cada uma, chegando na horizontal.
const rowHeight = (CardBottom - CardTop) / pieces.length
const BandThickness = 22
const exitStep = (ExitBottom - ExitTop) / pieces.length
// Curva de um ponto da face até a borda do cartão: sai quase reta e chega deitada.
const ribbon = (y0: number, y1: number) => {
  const x0 = rightFaceX(y0)
  const reach = (CardLeft - x0) * 0.5
  return { x0, c1: `${x0 + reach},${y0 + (y1 - y0) * 0.2}`, c2: `${CardLeft - reach},${y1}` }
}
const bands = pieces.map((piece, i) => {
  const center = CardTop + rowHeight * (i + 0.5)
  const e0 = ExitTop + exitStep * i
  const e1 = Math.min(ExitBottom, e0 + exitStep + 0.6)
  const top = ribbon(e0, center - BandThickness / 2)
  const bottom = ribbon(e1, center + BandThickness / 2)
  const d =
    `M${top.x0},${e0} C${top.c1} ${top.c2} ${CardLeft},${center - BandThickness / 2}` +
    ` L${CardLeft},${center + BandThickness / 2} C${bottom.c2} ${bottom.c1} ${bottom.x0},${e1} Z`
  return { ...piece, d, x0: top.x0 }
})

export function Hero() {
  const [lit, setLit] = useState<number | null>(null)

  return (
    <section id="abertura" className="relative">
      <div className="mx-auto grid max-w-6xl items-center gap-10 px-5 pt-10 pb-12 sm:px-8 sm:pt-16 lg:grid-cols-[1fr_1.05fr] lg:gap-10 lg:pb-20">
        <div className="flex flex-col items-start gap-6">
          <h1 className="font-display text-[2.75rem] leading-[1.02] tracking-tight text-balance sm:text-6xl lg:text-7xl">
            A compra é hoje.
            <br />
            <span className="text-spectrum">O dinheiro sai no vencimento.</span>
          </h1>
          <p className="max-w-xl text-lg text-pretty text-muted-foreground sm:text-xl">
            Cartão, parcelas, Pix e investimentos, cada um na sua cor. O Prisma Money decompõe o seu dinheiro para você ver o que
            entra, o que sai, e quando.
          </p>
          <div className="flex flex-col items-start gap-3">
            <Cta />
            <CtaNote />
          </div>
        </div>

        <div className="prism-beam relative w-full" style={{ aspectRatio: `${Width} / ${Height}` }}>
          <svg viewBox={`0 0 ${Width} ${Height}`} className="absolute inset-0 size-full text-foreground" aria-hidden="true">
            <defs>
              <linearGradient id="hero-beam" gradientUnits="userSpaceOnUse" x1={BeamStart.x} y1={BeamStart.y} x2={Entry.x} y2={Entry.y}>
                <stop offset="0" stopColor="currentColor" stopOpacity="0" />
                <stop offset="0.75" stopColor="currentColor" />
              </linearGradient>
              <linearGradient id="hero-glass" x1="0" y1="0" x2="0.5" y2="1">
                <stop offset="0" stopColor="currentColor" stopOpacity="0.14" />
                <stop offset="1" stopColor="currentColor" stopOpacity="0.03" />
              </linearGradient>
              <linearGradient id="hero-light" gradientUnits="userSpaceOnUse" x1={Entry.x} x2={rightFaceX(ExitBottom)}>
                <stop offset="0" stopColor="#ddd6fe" stopOpacity="0.7" />
                <stop offset="1" stopColor="#a78bfa" stopOpacity="0.35" />
              </linearGradient>
              {bands.map((b, i) => (
                <linearGradient key={b.color} id={`hero-band-${i}`} x1={b.x0} x2={CardLeft} gradientUnits="userSpaceOnUse">
                  <stop offset="0" stopColor={b.color} />
                  <stop offset="1" stopColor={b.color} stopOpacity="0.85" />
                </linearGradient>
              ))}
              <filter id="hero-glow" x="-20%" y="-20%" width="140%" height="140%">
                <feGaussianBlur stdDeviation="7" />
              </filter>
            </defs>

            {/* A luz que entra e o caminho dela dentro do vidro. */}
            <line
              className="beam-in"
              x1={BeamStart.x}
              y1={BeamStart.y}
              x2={Entry.x}
              y2={Entry.y}
              stroke="url(#hero-beam)"
              strokeWidth="4"
            />
            <polygon
              className="beam-glass"
              points={`${Entry.x},${Entry.y} ${rightFaceX(ExitTop)},${ExitTop} ${rightFaceX(ExitBottom)},${ExitBottom}`}
              fill="url(#hero-light)"
            />

            {/* O brilho das faixas, desfocado por baixo delas. */}
            <g filter="url(#hero-glow)" opacity="0.45">
              {bands.map((b, i) => (
                <path key={b.color} className="beam-band" data-dim={lit !== null && lit !== i} style={{ '--i': i } as CSSProperties} d={b.d} fill={b.color} />
              ))}
            </g>
            {bands.map((b, i) => (
              <path
                key={b.color}
                className="beam-band"
                data-dim={lit !== null && lit !== i}
                style={{ '--i': i } as CSSProperties}
                d={b.d}
                fill={`url(#hero-band-${i})`}
              />
            ))}

            <path d={prism} fill="url(#hero-glass)" stroke="currentColor" strokeOpacity="0.85" strokeWidth="2.5" />
          </svg>

          {/* O cartão onde as faixas terminam: linhas do app, em HTML para o texto ficar legível em qualquer largura. */}
          <ul
            className="beam-card surface absolute right-0 flex flex-col rounded-2xl"
            style={{ left: `${(CardLeft / Width) * 100}%`, top: `${(CardTop / Height) * 100}%`, bottom: `${((Height - CardBottom) / Height) * 100}%` }}
            onMouseLeave={() => setLit(null)}
          >
            {pieces.map((piece, i) => (
              <li key={piece.label} className="beam-row flex-1" style={{ '--i': i } as CSSProperties}>
                <a
                  href={piece.href}
                  onMouseEnter={() => setLit(i)}
                  onFocus={() => setLit(i)}
                  onBlur={() => setLit(null)}
                  className={cn(
                    'flex h-full items-center gap-2 px-3 transition-opacity duration-200 ease-out sm:gap-3 sm:px-4',
                    i > 0 && 'border-t border-border/60',
                    lit !== null && lit !== i && 'opacity-45',
                  )}
                >
                  <span className="size-2 shrink-0 rounded-full sm:size-2.5" style={{ background: piece.color, boxShadow: `0 0 8px -1px ${piece.color}` }} />
                  <span className="flex min-w-0 flex-1 flex-col leading-tight">
                    <span className="truncate text-xs font-medium sm:text-sm">{piece.label}</span>
                    <span className="truncate text-[0.6875rem] text-muted-foreground sm:text-xs">{piece.detail}</span>
                  </span>
                  <span className="shrink-0 text-xs sm:text-sm">{piece.value}</span>
                </a>
              </li>
            ))}
          </ul>
        </div>
      </div>
    </section>
  )
}
