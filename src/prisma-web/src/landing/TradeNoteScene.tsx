import { Sparkles } from 'lucide-react'
import { useEffect, useState, useSyncExternalStore, type CSSProperties } from 'react'
import { splitSymbol } from '@/features/investments/assets'
import { groupColors, type PortfolioGroup } from '@/features/investments/portfolio'
import { formatCents } from '@/lib/money'
import { allocateCents, averageCostCents } from './demoMath'
import { prefersReducedMotion, useSeenOnce } from './motion'
import { Eyebrow } from './parts'
import { ChapterChips, PhoneVideo } from './PhoneVideo'
import { useChapterVideo, type Chapter } from './useChapterVideo'
import noteDark from './video/note-dark.mp4'
import noteLight from './video/note-light.mp4'
import posterDark from './video/note-poster-dark.webp'
import posterLight from './video/note-poster-light.webp'

// A nota de corretagem (docs/landing.md, D13): à esquerda, o app de verdade lançando uma nota com três operações; à
// direita, a conta que o app faz dessa mesma nota: as taxas divididas pelo valor de cada compra (Money.Allocate) e o
// preço médio com custos de cada uma (AssetPosition). Os números são os do vídeo. A conta toca uma vez, quando a pessoa
// chega na seção (D8).
type Line = { symbol: string; group: PortfolioGroup; quantity: number; unitPriceCents: number; unit: string }

const lines: Line[] = [
  { symbol: 'HGLG11', group: 'Fiis', quantity: 12, unitPriceCents: 15830, unit: 'cotas' },
  { symbol: 'ITUB4', group: 'Stocks', quantity: 30, unitPriceCents: 3412, unit: 'ações' },
  { symbol: 'MXRF11', group: 'Fiis', quantity: 200, unitPriceCents: 968, unit: 'cotas' },
]
const FeesCents = 148

const values = lines.map((l) => l.quantity * l.unitPriceCents)
const fees = allocateCents(FeesCents, values)
const costs = values.map((v, i) => v + fees[i])

// Os tempos saem da gravação (o roteiro está no método da memória de capturas): mudou o roteiro, mudam aqui.
const chapters: Chapter[] = [
  { start: 0, title: 'Corretora e data' },
  { start: 4.7, title: 'As operações' },
  { start: 21.4, title: 'As taxas' },
  { start: 25.2, title: 'A carteira' },
]

const sources = {
  light: noteLight,
  dark: noteDark,
  posterLight,
  posterDark,
  label: 'O Prisma Money lançando uma nota de corretagem com três compras: HGLG11, ITUB4 e MXRF11, e a carteira atualizada',
}

// Uma etapa por linha da conta, depois as taxas.
const Final = lines.length + 1
const StepMs = 420
const noSubscription = () => () => {}

function AssetChip({ symbol, group }: { symbol: string; group: PortfolioGroup }) {
  const { root, suffix } = splitSymbol(symbol)
  return (
    <span
      aria-hidden
      className="category-tile flex size-10 shrink-0 flex-col items-center justify-center rounded-xl leading-none font-semibold tracking-tight"
      style={{ '--tile': groupColors[group] } as CSSProperties}
    >
      <span className="text-[0.6rem]">{root}</span>
      <span className="mt-0.5 text-[0.5rem] opacity-75">{suffix}</span>
    </span>
  )
}

export function TradeNoteScene() {
  const video = useChapterVideo(chapters)

  // "animate" é falso no HTML do build e verdadeiro no navegador que quer movimento: a hidratação começa igual ao
  // HTML e logo depois esconde a conta até ela tocar.
  const animate = useSyncExternalStore(noSubscription, () => !prefersReducedMotion(), () => false)
  const [step, setStep] = useState(0)
  const [ref, seen] = useSeenOnce<HTMLDivElement>(0.3)

  useEffect(() => {
    if (!animate || !seen) return
    let current = 0
    const timer = window.setInterval(() => {
      current++
      setStep(current)
      if (current >= Final) window.clearInterval(timer)
    }, StepMs)
    return () => window.clearInterval(timer)
  }, [animate, seen])

  const stage = animate ? step : Final
  const shown = (at: number) => (stage >= at ? 'true' : 'false')

  return (
    <section id="investimentos" className="mx-auto max-w-6xl scroll-mt-8 px-5 py-16 sm:px-8 sm:py-24">
      <div className="flex max-w-2xl flex-col gap-4">
        <Eyebrow band="#84cc16">Investimentos</Eyebrow>
        <h2 className="feature-title">Sua corretora manda a nota. Você lança a nota.</h2>
        <p className="lead">
          Uma nota, vários ativos, do jeito que vem da corretora. As taxas se dividem sozinhas entre as compras, pelo valor
          de cada uma, e a carteira já sai com quantidade e preço médio.
        </p>
      </div>

      <div className="mt-10 grid grid-cols-[minmax(0,1fr)] items-center gap-10 lg:grid-cols-[auto_minmax(0,1fr)] lg:gap-16">
        <div className="flex flex-col items-center gap-5">
          <PhoneVideo sources={sources} video={video} />
          <ChapterChips chapters={chapters} video={video} />
        </div>

        <div ref={ref} className="surface flex flex-col gap-4 rounded-3xl p-5 sm:p-6">
          <div className="flex flex-col gap-1">
            <p className="font-medium">O que o Prisma Money calcula desta nota</p>
            <p className="text-sm text-muted-foreground">As mesmas três compras do vídeo.</p>
          </div>
          <ul className="flex flex-col divide-y divide-border/60">
            {lines.map((line, i) => (
              <li key={line.symbol} className="scene-item flex items-center gap-3 py-3" data-shown={shown(i + 1)}>
                <AssetChip symbol={line.symbol} group={line.group} />
                <span className="flex min-w-0 flex-1 flex-col">
                  <span className="font-medium">{line.symbol}</span>
                  <span className="text-sm text-pretty text-muted-foreground">
                    {line.quantity} {line.unit} × {formatCents(line.unitPriceCents)}
                  </span>
                </span>
                <span className="flex shrink-0 flex-col items-end">
                  <span className="font-medium tabular-nums">{formatCents(values[i])}</span>
                  <span className="scene-item text-xs text-spectrum tabular-nums" data-shown={shown(Final)}>
                    + {formatCents(fees[i])} de taxas
                  </span>
                  <span className="scene-item text-xs text-muted-foreground tabular-nums" data-shown={shown(Final)}>
                    preço médio {formatCents(averageCostCents(costs[i], line.quantity))}
                  </span>
                </span>
              </li>
            ))}
          </ul>
          <p className="scene-item flex items-center justify-between gap-3 rounded-2xl bg-muted/60 px-4 py-3 text-sm" data-shown={shown(Final)}>
            <span>Taxas da nota, divididas pelo valor de cada compra</span>
            <span className="font-medium tabular-nums">{formatCents(FeesCents)}</span>
          </p>
          <p className="flex items-start gap-3 pt-2 text-sm text-muted-foreground">
            <Sparkles aria-hidden className="mt-0.5 size-4 shrink-0 text-foreground" />
            <span>
              <span className="font-medium text-foreground">Desdobrou, agrupou ou incorporou?</span> O Prisma Money aplica o evento
              na sua carteira sozinho, e você só confere se bateu.
            </span>
          </p>
        </div>
      </div>
    </section>
  )
}
