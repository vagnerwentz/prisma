import { ChevronRight, HandCoins, Info, Receipt, Sprout } from 'lucide-react'
import { useState, type CSSProperties, type ReactNode } from 'react'
import { Link } from 'react-router'
import { MonthSwitcher, toMonthParam, useMonthParam } from '@/components/MonthSwitcher'
import { Alert, AlertDescription } from '@/components/ui/alert'
import { Button } from '@/components/ui/button'
import { Skeleton } from '@/components/ui/skeleton'
import { formatMonth } from '@/lib/dates'
import { formatCents } from '@/lib/money'
import { cn } from '@/lib/utils'
import { useMonthlySummary, type MonthlySummary } from './queries'

// Tela inicial: o mês de relance, pela data de caixa (docs/fase-2.md, 4).
export function SummaryPage() {
  const [month, setMonth] = useMonthParam()
  const param = toMonthParam(month)
  const summary = useMonthlySummary(param)
  const monthName = formatMonth(month).split(' de ')[0].toLowerCase()

  return (
    <main className="mx-auto flex max-w-2xl flex-col gap-6 px-4 pt-5 pb-32">
      <MonthSwitcher month={month} onChange={setMonth} />

      {summary.isPending && (
        <>
          <Skeleton className="h-40 w-full rounded-3xl" />
          <Skeleton className="h-44 w-full rounded-2xl" />
        </>
      )}
      {summary.isError && (
        <Alert variant="destructive">
          <AlertDescription>Não foi possível carregar o resumo. Tente novamente.</AlertDescription>
        </Alert>
      )}
      {summary.isSuccess && <Overview summary={summary.data} monthName={monthName} monthParam={param} />}
    </main>
  )
}

function Overview({ summary, monthName, monthParam }: { summary: MonthlySummary; monthName: string; monthParam: string }) {
  const { incomeCents, expenseCents, leftoverCents, investedCents } = summary
  const [explaining, setExplaining] = useState(false)
  const empty = incomeCents === 0 && expenseCents === 0 && investedCents === 0
  // Sem receita lançada, "faltou" soaria como alarme: muita gente só registra os gastos.
  const title =
    incomeCents === 0 ? `Saldo de ${monthName}` : leftoverCents < 0 ? `Faltou em ${monthName}` : `Sobra de ${monthName}`
  const spentShare = incomeCents > 0 ? expenseCents / incomeCents : 0

  return (
    <>
      {/* Um espectro por tela: o filete sob o mês. O destaque ganha só o halo frio ao fundo. */}
      <section className="surface relative flex flex-col gap-4 overflow-hidden rounded-3xl p-5 sm:p-6">
        <span
          aria-hidden
          className="pointer-events-none absolute -top-24 -right-20 size-64 rounded-full opacity-25 blur-3xl dark:opacity-30"
          style={{ background: 'var(--halo)' }}
        />
        <div className="relative flex items-center justify-between gap-2">
          <span className="text-sm font-medium text-foreground/75">{title}</span>
          <button
            type="button"
            aria-label="Como o resumo é calculado"
            aria-expanded={explaining}
            onClick={() => setExplaining((open) => !open)}
            className="-m-2 rounded-full p-2 text-muted-foreground transition-colors hover:text-foreground aria-expanded:text-foreground"
          >
            <Info className="size-4" />
          </button>
        </div>
        <span className="relative font-display text-[clamp(2.75rem,12vw,3.5rem)] leading-none tabular-nums">
          {signed(leftoverCents)}
        </span>
        {incomeCents > 0 && (
          <div className="relative flex flex-col gap-2">
            <div className="h-2 overflow-hidden rounded-full bg-muted">
              <div
                className="h-full rounded-full"
                style={{ width: `${Math.min(spentShare, 1) * 100}%`, background: 'var(--spectrum-cool)' }}
              />
            </div>
            <span className="text-sm font-medium text-foreground/75">Gastou {Math.round(spentShare * 100)}% do que entrou</span>
          </div>
        )}
        {explaining && (
          <p className="relative text-sm leading-relaxed text-muted-foreground">
            Pela data de caixa: compras no cartão contam no mês do vencimento da fatura. Transferências, inclusive o pagamento da
            fatura, não são receita nem despesa. Sobra é o que entrou menos o que saiu; investir não diminui a sobra.
          </p>
        )}
      </section>

      {empty ? (
        <div className="surface flex flex-col items-center gap-4 rounded-3xl px-6 py-10 text-center">
          <p className="text-sm font-medium text-foreground/75">Nada lançado para {monthName}.</p>
          <Button asChild className="rounded-full">
            <Link to="/lancar">Fazer um lançamento</Link>
          </Button>
        </div>
      ) : (
        // Celular: Entrou e Saiu lado a lado, Investido embaixo em linha. A partir de 640px, três colunas.
        <div className="grid grid-cols-2 gap-3 sm:grid-cols-3">
          <Figure icon={<HandCoins />} tint={incomeTint} label="Entrou">
            {incomeCents > 0 ? <span className="text-spectrum">{formatCents(incomeCents)}</span> : formatCents(0)}
          </Figure>
          <Figure icon={<Receipt />} label="Saiu">
            {formatCents(expenseCents)}
          </Figure>
          <Figure
            icon={<Sprout />}
            tint={investedTint}
            label="Investido"
            hint={investedCents < 0 ? 'resgatou mais do que aplicou' : undefined}
            wide
          >
            {signed(investedCents)}
          </Figure>
        </div>
      )}

      <Link
        to={`/lancamentos?mes=${monthParam}`}
        className="surface flex items-center justify-between rounded-2xl px-4 py-3.5 text-sm font-medium transition-colors hover:bg-muted/40 active:bg-muted/70"
      >
        Ver lançamentos de {monthName}
        <ChevronRight className="size-4 text-muted-foreground" />
      </Link>
    </>
  )
}

// Cores do espectro, com parcimônia: entrada é luz fria; saída fica em tinta neutra (CLAUDE.md, 7.1).
const incomeTint = '#22d3ee'
const investedTint = '#8b5cf6'

function Tile({ icon, tint }: { icon: ReactNode; tint?: string }) {
  return (
    <span
      className={cn(
        'flex size-10 shrink-0 items-center justify-center rounded-full [&_svg]:size-5',
        tint ? 'category-tile' : 'bg-muted text-foreground',
      )}
      style={tint ? ({ '--tile': tint } as CSSProperties) : undefined}
    >
      {icon}
    </span>
  )
}

// `wide`: no celular ocupa a linha inteira, com o valor à direita; a partir de 640px vira coluna.
function Figure({
  icon,
  tint,
  label,
  hint,
  wide,
  children,
}: {
  icon: ReactNode
  tint?: string
  label: string
  hint?: string
  wide?: boolean
  children: ReactNode
}) {
  return (
    <div
      className={cn(
        'surface flex min-w-0 gap-3 rounded-2xl p-4',
        wide ? 'col-span-2 flex-row items-center sm:col-span-1 sm:flex-col sm:items-start' : 'flex-col',
      )}
    >
      <Tile icon={icon} tint={tint} />
      <div className={cn('flex min-w-0 flex-col gap-0.5', wide && 'flex-1 sm:flex-none')}>
        <span className="text-sm font-medium text-foreground/75">{label}</span>
        {hint && <span className="text-xs text-muted-foreground">{hint}</span>}
      </div>
      <span
        className={cn(
          'truncate text-[clamp(1.05rem,4.8vw,1.35rem)] leading-tight font-semibold tracking-tight tabular-nums',
          wide ? 'sm:-mt-2' : '-mt-2',
        )}
      >
        {children}
      </span>
    </div>
  )
}

// Negativo com o sinal tipográfico "−", como nos lançamentos.
function signed(cents: number): string {
  return formatCents(cents).replace('-', '−')
}
