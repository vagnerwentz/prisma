import { lazy, Suspense } from 'react'
import { Link } from 'react-router'
import { MonthSwitcher } from '@/components/MonthSwitcher'
import { Alert, AlertDescription } from '@/components/ui/alert'
import { Button } from '@/components/ui/button'
import { Skeleton } from '@/components/ui/skeleton'
import { formatMonth, monthOf, todayInSaoPaulo } from '@/lib/dates'
import { toMonthParam, useMonthParam } from '@/lib/monthParam'
import { useMonthlySummary } from './queries'

// Cada bloco carrega sob demanda: os ladrilhos trazem os logos de marca, o comparativo traz o Recharts.
const CategoryBreakdown = lazy(() => import('./CategoryBreakdown').then((m) => ({ default: m.CategoryBreakdown })))
const HistoryChart = lazy(() => import('./HistoryChart'))
const InheritedInstallments = lazy(() => import('./CommitmentBlocks').then((m) => ({ default: m.InheritedInstallments })))
const CommittedMonths = lazy(() => import('./CommitmentBlocks').then((m) => ({ default: m.CommittedMonths })))

// Análise: "para onde foi e por quê?" (docs/fase-2.md, 4, etapa 2.9). O Resumo fica de relance; aqui
// ficam categorias, parcelas herdadas, comparativo e o que vem pela frente, no mesmo mês da URL.
export function AnalysisPage() {
  const [month, setMonth] = useMonthParam()
  const param = toMonthParam(month)
  const summary = useMonthlySummary(param)
  const monthName = formatMonth(month).split(' de ')[0].toLowerCase()
  const isCurrentMonth = param === toMonthParam(monthOf(todayInSaoPaulo()))
  const empty =
    summary.isSuccess && summary.data.incomeCents === 0 && summary.data.expenseCents === 0 && summary.data.investedCents === 0

  // "Daqui para frente" olha para hoje: aparece no mês atual, mesmo sem nada lançado nele.
  const ahead = isCurrentMonth && (
    <Suspense fallback={<Skeleton className="h-60 w-full rounded-2xl" />}>
      <CommittedMonths onSelect={setMonth} />
    </Suspense>
  )

  return (
    <main className="mx-auto flex max-w-2xl flex-col gap-6 px-4 pt-5 pb-32 lg:max-w-5xl">
      <MonthSwitcher month={month} onChange={setMonth} />

      {summary.isPending && (
        <>
          <Skeleton className="h-48 w-full rounded-2xl" />
          <Skeleton className="h-56 w-full rounded-2xl" />
        </>
      )}
      {summary.isError && (
        <Alert variant="destructive">
          <AlertDescription>Não foi possível carregar a análise. Tente novamente.</AlertDescription>
        </Alert>
      )}

      {summary.isSuccess && empty && (
        <>
          <div className="surface flex flex-col items-center gap-4 rounded-3xl px-6 py-10 text-center">
            <p className="text-sm font-medium text-foreground/75">Nada lançado para {monthName}.</p>
            <Button asChild className="rounded-full">
              <Link to="/lancar">Fazer um lançamento</Link>
            </Button>
          </div>
          {ahead}
        </>
      )}

      {summary.isSuccess && !empty && (
        // Celular: uma coluna, na ordem da especificação (order-*). A partir de 1024px, duas colunas:
        // os wrappers deixam de ser "contents" e viram colunas, cada uma com os seus blocos.
        <div className="flex flex-col gap-6 lg:grid lg:grid-cols-2 lg:items-start">
          <div className="contents lg:flex lg:flex-col lg:gap-6">
            <div className="order-1">
              <Suspense fallback={<Skeleton className="h-48 w-full rounded-2xl" />}>
                <CategoryBreakdown month={param} />
              </Suspense>
            </div>
            <div className="order-3">
              <Suspense fallback={<Skeleton className="h-64 w-full rounded-2xl" />}>
                <HistoryChart month={param} onSelect={setMonth} />
              </Suspense>
            </div>
          </div>
          <div className="contents lg:flex lg:flex-col lg:gap-6">
            <div className="order-2">
              <Suspense fallback={<Skeleton className="h-56 w-full rounded-2xl" />}>
                <InheritedInstallments month={param} monthName={monthName} />
              </Suspense>
            </div>
            {ahead && <div className="order-4">{ahead}</div>}
          </div>
        </div>
      )}
    </main>
  )
}
