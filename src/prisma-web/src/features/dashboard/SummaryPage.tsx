import { ChevronRight, HandCoins, Info, Receipt, Sprout } from 'lucide-react'
import { lazy, Suspense, useState, type CSSProperties, type ReactNode } from 'react'
import { Link } from 'react-router'
import { MonthSwitcher } from '@/components/MonthSwitcher'
import { toMonthParam, useMonthParam } from '@/lib/monthParam'
import { Alert, AlertDescription } from '@/components/ui/alert'
import { Button } from '@/components/ui/button'
import { Skeleton } from '@/components/ui/skeleton'
import { formatMonth, monthOf, todayInSaoPaulo, type YearMonth } from '@/lib/dates'
import { formatCents } from '@/lib/money'
import { cn } from '@/lib/utils'
import { useMonthlySummary, type MonthlySummary } from './queries'

// As categorias trazem os ícones e logos dos ladrilhos: ficam fora do pacote principal.
const CategoryBreakdown = lazy(() => import('./CategoryBreakdown').then((m) => ({ default: m.CategoryBreakdown })))
// O gráfico traz o Recharts: também sob demanda.
const HistoryChart = lazy(() => import('./HistoryChart'))
// Saldo em contas e próximas faturas: os ladrilhos de conta trazem os logos de marca.
const TodayPanel = lazy(() => import('./TodayPanel'))
// Compromissos herdados (docs/fase-2.md, 2.6): os ladrilhos das compras trazem os logos de marca.
const InheritedInstallments = lazy(() => import('./CommitmentBlocks').then((m) => ({ default: m.InheritedInstallments })))
const CommittedMonths = lazy(() => import('./CommitmentBlocks').then((m) => ({ default: m.CommittedMonths })))

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
      {summary.isSuccess && <Overview summary={summary.data} monthName={monthName} monthParam={param} onSelectMonth={setMonth} />}
    </main>
  )
}

function Overview({
  summary,
  monthName,
  monthParam,
  onSelectMonth,
}: {
  summary: MonthlySummary
  monthName: string
  monthParam: string
  onSelectMonth: (month: YearMonth) => void
}) {
  const { incomeCents, expenseCents, cardExpenseCents, leftoverCents, investedCents } = summary
  const [explaining, setExplaining] = useState(false)
  const empty = incomeCents === 0 && expenseCents === 0 && investedCents === 0
  // Sem receita lançada, a sobra seria só o gasto com sinal trocado, em tom de alarme: muita gente
  // só registra os gastos. Então o destaque mostra os gastos (docs/fase-2.md, 4).
  const withoutIncome = incomeCents === 0 && expenseCents > 0
  const title = withoutIncome ? `Gastos de ${monthName}` : leftoverCents < 0 ? `Faltou em ${monthName}` : `Sobra de ${monthName}`
  // Estornos maiores que os gastos deixam o "Saiu" negativo (docs/fase-2.md, 2.5, regra 11).
  const cardNote =
    expenseCents < 0
      ? 'Os estornos passaram dos gastos do mês'
      : cardExpenseCents <= 0
        ? undefined
        : cardExpenseCents === expenseCents
          ? 'Tudo em faturas de cartão'
          : `${formatCents(cardExpenseCents)} em faturas de cartão`
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
          {withoutIncome ? formatCents(expenseCents) : signed(leftoverCents)}
        </span>
        {withoutIncome && (
          <span className="relative text-sm font-medium text-foreground/75">Lance suas receitas para ver quanto sobra</span>
        )}
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
            fatura, não são receita nem despesa. Estornos abatem as despesas do mês em que caem. Sobra é o que entrou menos o que
            saiu; investir não diminui a sobra.
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
          <Figure icon={<Receipt />} label="Saiu" note={cardNote}>
            {signed(expenseCents)}
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

      {/* Logo abaixo do "Saiu", que ele explica. */}
      {!empty && (
        <Suspense fallback={<Skeleton className="h-56 w-full rounded-2xl" />}>
          <InheritedInstallments month={monthParam} monthName={monthName} />
        </Suspense>
      )}

      <Suspense fallback={<Skeleton className="h-40 w-full rounded-2xl" />}>
        <TodayPanel />
      </Suspense>

      {/* Olha para hoje, como o bloco "Hoje": só no Resumo do mês atual. */}
      {monthParam === toMonthParam(monthOf(todayInSaoPaulo())) && (
        <Suspense fallback={<Skeleton className="h-60 w-full rounded-2xl" />}>
          <CommittedMonths onSelect={onSelectMonth} />
        </Suspense>
      )}

      {!empty && (
        <Suspense fallback={<Skeleton className="h-48 w-full rounded-2xl" />}>
          <CategoryBreakdown month={monthParam} />
        </Suspense>
      )}

      <Suspense fallback={<Skeleton className="h-64 w-full rounded-2xl" />}>
        <HistoryChart month={monthParam} onSelect={onSelectMonth} />
      </Suspense>

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
  note,
  wide,
  children,
}: {
  icon: ReactNode
  tint?: string
  label: string
  hint?: string
  note?: string
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
      <div className={cn('flex min-w-0 flex-col gap-1', wide ? 'sm:-mt-2' : '-mt-2')}>
        <span className="truncate text-[clamp(1.05rem,4.8vw,1.35rem)] leading-tight font-semibold tracking-tight tabular-nums">
          {children}
        </span>
        {note && <span className="text-xs leading-snug text-muted-foreground">{note}</span>}
      </div>
    </div>
  )
}

// Negativo com o sinal tipográfico "−", como nos lançamentos.
function signed(cents: number): string {
  return formatCents(cents).replace('-', '−')
}
