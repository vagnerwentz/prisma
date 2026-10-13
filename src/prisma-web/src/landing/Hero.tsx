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
const Apex = { x: 128, y: 48 }
const BaseLeft = { x: 54, y: 308 }
const BaseRight = { x: 202, y: 308 }
const Entry = { x: (Apex.x + BaseLeft.x) / 2, y: (Apex.y + BaseLeft.y) / 2 }
// Trecho da face direita por onde as cores saem, e onde o cartão começa (as faixas terminam nele).
const ExitTop = 150
const ExitBottom = 210
const CardLeft = 268
const CardTop = 24
const CardBottom = 336
const rightFaceX = (y: number) => Apex.x + ((y - Apex.y) * (BaseRight.x - Apex.x)) / (BaseRight.y - Apex.y)

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

// Cada linha do cartão ocupa um quarto da altura dele; a faixa chega no meio da linha.
const rowHeight = (CardBottom - CardTop) / pieces.length
const BandThickness = 22
const bands = pieces.map((piece, i) => {
  const center = CardTop + rowHeight * (i + 0.5)
  const exit = ExitTop + ((ExitBottom - ExitTop) * (i + 0.5)) / pieces.length
  const halfExit = (ExitBottom - ExitTop) / pieces.length / 2 - 1
  return { ...piece, center, e0: exit - halfExit, e1: exit + halfExit }
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
              <linearGradient id="hero-beam" x1="0" x2="1">
                <stop offset="0" stopColor="currentColor" stopOpacity="0" />
                <stop offset="1" stopColor="currentColor" stopOpacity="0.9" />
              </linearGradient>
              <linearGradient id="hero-glass" x1="0" y1="0" x2="1" y2="1">
                <stop offset="0" stopColor="currentColor" stopOpacity="0.1" />
                <stop offset="1" stopColor="currentColor" stopOpacity="0.02" />
              </linearGradient>
              {bands.map((b, i) => (
                <linearGradient key={b.color} id={`hero-band-${i}`} x1={rightFaceX(b.e0)} x2={CardLeft} gradientUnits="userSpaceOnUse">
                  <stop offset="0" stopColor={b.color} stopOpacity="0.3" />
                  <stop offset="1" stopColor={b.color} stopOpacity="0.95" />
                </linearGradient>
              ))}
            </defs>

            {/* A luz que entra e o caminho dela dentro do vidro. */}
            <polygon
              className="beam-in"
              points={`0,${Entry.y - 3} ${Entry.x},${Entry.y - 2} ${Entry.x},${Entry.y + 2} 0,${Entry.y + 3}`}
              fill="url(#hero-beam)"
            />
            <polygon
              className="beam-glass"
              points={`${Entry.x},${Entry.y - 2} ${rightFaceX(ExitTop)},${ExitTop} ${rightFaceX(ExitBottom)},${ExitBottom} ${Entry.x},${Entry.y + 2}`}
              fill="currentColor"
              fillOpacity="0.12"
            />

            {bands.map((b, i) => (
              <polygon
                key={b.color}
                className="beam-band"
                data-dim={lit !== null && lit !== i}
                style={{ '--i': i } as CSSProperties}
                points={`${rightFaceX(b.e0)},${b.e0} ${CardLeft},${b.center - BandThickness / 2} ${CardLeft},${b.center + BandThickness / 2} ${rightFaceX(b.e1)},${b.e1}`}
                fill={`url(#hero-band-${i})`}
              />
            ))}

            <polygon
              points={`${Apex.x},${Apex.y} ${BaseRight.x},${BaseRight.y} ${BaseLeft.x},${BaseLeft.y}`}
              fill="url(#hero-glass)"
              stroke="currentColor"
              strokeOpacity="0.45"
              strokeWidth="2"
              strokeLinejoin="round"
            />
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
