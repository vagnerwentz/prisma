import { Check, Database, RefreshCw, TriangleAlert } from 'lucide-react'
import { useState, type ReactNode } from 'react'
import { toast } from 'sonner'
import { Alert, AlertDescription } from '@/components/ui/alert'
import { Button } from '@/components/ui/button'
import { Input } from '@/components/ui/input'
import { Skeleton } from '@/components/ui/skeleton'
import { formatShortDate, todayInSaoPaulo } from '@/lib/dates'
import { cn } from '@/lib/utils'
import {
  formatInstant,
  formatWindow,
  isFailure,
  kindLabels,
  nextRunText,
  outcomeLabels,
  runSummary,
  runToast,
  scheduleText,
  seriesState,
  stateDescription,
  stateLabels,
  suggestedWindow,
  syncErrorMessage,
  windowProblem,
  type MarketRun,
  type MarketSeries,
} from './adminMarket'
import { AdminBack } from './AdminBack'
import { useMarketData, useRefreshMarket, useSyncMarket, useSyncRuns } from './queries'

// Admin dos dados de mercado (docs/admin-mercado.md, seção 7): o estado de cada série, a próxima execução automática e as últimas
// execuções, com os botões para sincronizar agora ou reprocessar um período. A busca automática continua nos horários marcados.
export function AdminPage() {
  const data = useMarketData()

  return (
    <main className="mx-auto flex max-w-2xl flex-col gap-6 px-4 pt-3 pb-32">
      <AdminBack />
      <div>
        <h1 className="font-display text-3xl">Dados de mercado</h1>
        <div className="spectrum-line mt-2 opacity-80" />
        <p className="mt-3 text-sm text-muted-foreground">
          As taxas que o Prisma busca no Banco Central. A busca automática continua rodando nos horários marcados; aqui você pode
          rodar na hora.
        </p>
      </div>

      {data.isPending && <Skeleton className="h-72 w-full rounded-3xl" />}
      {data.isError && (
        <Alert variant="destructive">
          <AlertDescription>Não foi possível carregar o estado das taxas. Tente novamente.</AlertDescription>
        </Alert>
      )}
      {data.isSuccess && data.data.map((series) => <SeriesCard key={series.code} series={series} />)}
    </main>
  )
}

function SeriesCard({ series }: { series: MarketSeries }) {
  const state = seriesState(series)
  const sync = useSyncMarket()
  const refresh = useRefreshMarket()
  const busy = sync.isPending || refresh.isPending

  const run = (action: Promise<MarketRun>) =>
    action.then((result) => toast(runToast(result))).catch((error) => toast.error(syncErrorMessage(error)))

  return (
    <section className="flex flex-col gap-5">
      <div className="surface flex flex-col gap-5 rounded-3xl p-5">
        <div className="flex items-start justify-between gap-3">
          <div className="min-w-0">
            <h2 className="font-display text-xl">{series.name}</h2>
            <p className="text-xs text-muted-foreground">Fonte: {series.source}</p>
          </div>
          <StateChip state={state} />
        </div>

        <div className="flex flex-col gap-1 text-sm">
          <p>{stateDescription(series)}</p>
          {series.first && series.last && (
            <p className="text-muted-foreground">
              {series.count.toLocaleString('pt-BR')} taxas, de {formatShortDate(series.first)} a {formatShortDate(series.last)}.
            </p>
          )}
          {series.lastFetchedAt && (
            <p className="text-muted-foreground">Última leitura do Banco Central: {formatInstant(series.lastFetchedAt)}.</p>
          )}
        </div>

        <div className="flex flex-col gap-1 border-t border-border/60 pt-4 text-sm">
          <p>{nextRunText(series.schedule)}</p>
          <p className="text-xs text-muted-foreground">{scheduleText(series.schedule)}</p>
        </div>

        <LastRuns series={series} />

        <div className="flex flex-col gap-3 border-t border-border/60 pt-4">
          <Button className="h-11 rounded-full" disabled={busy} onClick={() => run(sync.mutateAsync(series.code))}>
            <RefreshCw className={cn(sync.isPending && 'animate-spin')} />
            {sync.isPending ? 'Sincronizando…' : 'Sincronizar agora'}
          </Button>
          <RefreshPeriod
            busy={busy}
            pending={refresh.isPending}
            onConfirm={(from, to) => run(refresh.mutateAsync({ series: series.code, from, to }))}
          />
        </div>
      </div>

      <RunList series={series.code} />
    </section>
  )
}

function StateChip({ state }: { state: ReturnType<typeof seriesState> }) {
  const ok = state === 'ok'
  return (
    <span
      className={cn(
        'inline-flex shrink-0 items-center gap-1.5 rounded-full px-2.5 py-1 text-xs font-medium',
        ok ? 'bg-muted text-foreground' : 'bg-destructive/10 text-destructive',
      )}
    >
      {ok ? <Check className="size-3.5" /> : <TriangleAlert className="size-3.5" />}
      {stateLabels[state]}
    </span>
  )
}

// A última execução e, se ela falhou, a última que deu certo.
function LastRuns({ series }: { series: MarketSeries }) {
  const { lastRun, lastSuccessfulRun } = series
  if (!lastRun)
    return <p className="border-t border-border/60 pt-4 text-sm text-muted-foreground">Nenhuma execução registrada ainda.</p>

  return (
    <div className="flex flex-col gap-3 border-t border-border/60 pt-4">
      <RunBlock title="Última execução" run={lastRun} />
      {isFailure(lastRun) && lastSuccessfulRun && <RunBlock title="Última que deu certo" run={lastSuccessfulRun} />}
    </div>
  )
}

function RunBlock({ title, run }: { title: string; run: MarketRun }) {
  const failed = isFailure(run)
  return (
    <div className="flex flex-col gap-1">
      <p className="text-xs text-muted-foreground">{title}</p>
      <p className="text-sm">
        <span className={cn('font-medium', failed && 'text-destructive')}>{outcomeLabels[run.outcome]}</span>
        <span className="text-muted-foreground">
          {' '}
          · {kindLabels[run.kind]} · {formatInstant(run.startedAt)}
        </span>
      </p>
      <p className="text-xs text-muted-foreground">Período {formatWindow(run.windowFrom, run.windowTo)}</p>
      <p className={cn('text-sm', failed ? 'text-destructive' : 'text-muted-foreground')}>{runSummary(run)}</p>
    </div>
  )
}

// "Reprocessar período": duas datas, e uma confirmação antes de ir ao Banco Central.
function RefreshPeriod({
  busy,
  pending,
  onConfirm,
}: {
  busy: boolean
  pending: boolean
  onConfirm: (from: string, to: string) => void
}) {
  const today = todayInSaoPaulo()
  const [open, setOpen] = useState(false)
  const [confirming, setConfirming] = useState(false)
  const [window, setWindow] = useState(() => suggestedWindow(today))
  const problem = windowProblem(window.from, window.to, today)

  if (!open)
    return (
      <Button variant="outline" className="h-11 rounded-full" disabled={busy} onClick={() => setOpen(true)}>
        {pending ? 'Reprocessando…' : 'Reprocessar período'}
      </Button>
    )

  return (
    <div className="flex flex-col gap-3 rounded-2xl bg-muted/50 p-4">
      <p className="text-xs text-muted-foreground">Reprocessar período</p>
      <div className="grid grid-cols-1 gap-3 min-[400px]:grid-cols-2">
        <DateField
          label="De"
          value={window.from}
          max={today}
          onChange={(from) => {
            setWindow({ ...window, from })
            setConfirming(false)
          }}
        />
        <DateField
          label="Até"
          value={window.to}
          max={today}
          onChange={(to) => {
            setWindow({ ...window, to })
            setConfirming(false)
          }}
        />
      </div>
      {problem && <p className="text-sm text-destructive">{problem}</p>}

      {confirming && !problem ? (
        <>
          <p className="text-sm">
            Vai consultar o Banco Central de {formatShortDate(window.from)} a {formatShortDate(window.to)} e gravar o que vier.
            Pode demorar até 90 segundos.
          </p>
          <div className="flex gap-2">
            <Button
              className="h-11 flex-1 rounded-full"
              disabled={busy}
              onClick={() => {
                onConfirm(window.from, window.to)
                setConfirming(false)
                setOpen(false)
              }}
            >
              Confirmar
            </Button>
            <Button variant="ghost" className="h-11 rounded-full" onClick={() => setConfirming(false)}>
              Voltar
            </Button>
          </div>
        </>
      ) : (
        <div className="flex gap-2">
          <Button className="h-11 flex-1 rounded-full" disabled={busy || problem !== null} onClick={() => setConfirming(true)}>
            Reprocessar
          </Button>
          <Button variant="ghost" className="h-11 rounded-full" onClick={() => setOpen(false)}>
            Cancelar
          </Button>
        </div>
      )}
    </div>
  )
}

function DateField({
  label,
  value,
  max,
  onChange,
}: {
  label: string
  value: string
  max: string
  onChange: (value: string) => void
}) {
  return (
    <label className="flex flex-col gap-1 text-xs text-muted-foreground">
      {label}
      <Input
        type="date"
        value={value}
        max={max}
        onChange={(e) => onChange(e.target.value)}
        className="h-11 rounded-xl text-foreground"
      />
    </label>
  )
}

function RunList({ series }: { series: string }) {
  const runs = useSyncRuns(series)

  let body: ReactNode
  if (runs.isPending) body = <Skeleton className="h-24 w-full rounded-2xl" />
  else if (runs.isError) body = <p className="text-sm text-destructive">Não foi possível carregar as execuções.</p>
  else if (runs.data.length === 0) body = <p className="text-sm text-muted-foreground">Nenhuma execução registrada ainda.</p>
  else
    body = (
      <ul className="flex flex-col divide-y divide-border/60">
        {runs.data.map((run) => (
          <li key={run.id} className="flex flex-col gap-0.5 py-3 first:pt-0 last:pb-0">
            <span className={cn('text-sm font-medium', isFailure(run) && 'text-destructive')}>{outcomeLabels[run.outcome]}</span>
            <p className="text-xs text-muted-foreground">
              {kindLabels[run.kind]} · {formatInstant(run.startedAt)}
            </p>
            <p className="text-xs text-muted-foreground">Período {formatWindow(run.windowFrom, run.windowTo)}</p>
            <p className={cn('text-xs', isFailure(run) ? 'text-destructive' : 'text-muted-foreground')}>{runSummary(run)}</p>
          </li>
        ))}
      </ul>
    )

  return (
    <div className="surface flex flex-col gap-3 rounded-3xl p-5">
      <h3 className="flex items-center gap-2 text-sm font-medium">
        <Database className="size-4 text-muted-foreground" />
        Últimas execuções
      </h3>
      {body}
    </div>
  )
}
