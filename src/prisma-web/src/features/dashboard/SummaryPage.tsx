import { ArrowDownLeft, ArrowUpRight, ChevronRight, Sprout } from 'lucide-react'
import type { ReactNode } from 'react'
import { Link } from 'react-router'
import { MonthSwitcher, toMonthParam, useMonthParam } from '@/components/MonthSwitcher'
import { Alert, AlertDescription } from '@/components/ui/alert'
import { Button } from '@/components/ui/button'
import { Skeleton } from '@/components/ui/skeleton'
import { formatMonth } from '@/lib/dates'
import { formatCents } from '@/lib/money'
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
  const empty = incomeCents === 0 && expenseCents === 0 && investedCents === 0
  // Quanto da receita já foi gasto: a barra enche até o limite e para.
  const spentShare = incomeCents > 0 ? Math.min(expenseCents / incomeCents, 1) : expenseCents > 0 ? 1 : 0

  return (
    <>
      <section className="spectrum-ring flex flex-col gap-4 rounded-3xl p-5">
        <span className="text-xs font-medium tracking-wide text-muted-foreground uppercase">
          {leftoverCents < 0 ? `Faltou em ${monthName}` : `Sobra de ${monthName}`}
        </span>
        <span className="font-display text-5xl leading-none tabular-nums">{signed(leftoverCents)}</span>
        {!empty && (
          <div className="flex flex-col gap-2">
            <div className="h-1.5 overflow-hidden rounded-full bg-muted">
              <div
                className="h-full rounded-full"
                style={{ width: `${spentShare * 100}%`, background: 'var(--spectrum-cool)' }}
              />
            </div>
            <span className="text-sm text-muted-foreground">
              {incomeCents > 0
                ? `Gastou ${Math.round((expenseCents / incomeCents) * 100)}% do que entrou`
                : 'Nenhuma receita no mês'}
            </span>
          </div>
        )}
      </section>

      {empty ? (
        <div className="flex flex-col items-center gap-4 rounded-3xl border border-dashed px-6 py-10 text-center">
          <p className="text-sm text-muted-foreground">Nada lançado para {monthName}.</p>
          <Button asChild className="rounded-full">
            <Link to="/lancar">Fazer um lançamento</Link>
          </Button>
        </div>
      ) : (
        <dl className="flex flex-col divide-y rounded-2xl border bg-card">
          <Figure icon={<ArrowDownLeft />} label="Receitas">
            {incomeCents > 0 ? <span className="text-spectrum font-semibold">+{formatCents(incomeCents)}</span> : formatCents(0)}
          </Figure>
          <Figure icon={<ArrowUpRight />} label="Despesas">
            {signed(-expenseCents)}
          </Figure>
          <Figure icon={<Sprout />} label="Investido" hint={investedCents < 0 ? 'resgatou mais do que aplicou' : undefined}>
            {signed(investedCents)}
          </Figure>
        </dl>
      )}

      <Link
        to={`/lancamentos?mes=${monthParam}`}
        className="flex items-center justify-between rounded-2xl border bg-card px-4 py-3 text-sm font-medium transition-colors hover:bg-muted/40 active:bg-muted/70"
      >
        Ver lançamentos de {monthName}
        <ChevronRight className="size-4 text-muted-foreground" />
      </Link>

      <p className="px-1 text-xs leading-relaxed text-muted-foreground">
        Pela data de caixa: compras no cartão contam no mês do vencimento da fatura, e transferências (inclusive o pagamento da
        fatura) não são receita nem despesa.
      </p>
    </>
  )
}

function Figure({ icon, label, hint, children }: { icon: ReactNode; label: string; hint?: string; children: ReactNode }) {
  return (
    <div className="flex items-center gap-3 px-4 py-3.5">
      <span className="flex size-9 shrink-0 items-center justify-center rounded-full bg-muted text-muted-foreground [&_svg]:size-4">
        {icon}
      </span>
      <div className="flex min-w-0 flex-1 flex-col">
        <dt className="text-sm">{label}</dt>
        {hint && <span className="text-xs text-muted-foreground">{hint}</span>}
      </div>
      <dd className="text-right text-base font-medium tabular-nums">{children}</dd>
    </div>
  )
}

// Negativo com o sinal tipográfico "−", como nos lançamentos.
function signed(cents: number): string {
  return formatCents(cents).replace('-', '−')
}
