import { ArrowLeft, ChevronDown, ChevronRight, Info, Plus } from 'lucide-react'
import { lazy, Suspense, useState } from 'react'
import { toast } from 'sonner'
import { BottomSheet } from '@/components/BottomSheet'
import { StaleFade } from '@/components/StaleFade'
import { Alert, AlertDescription } from '@/components/ui/alert'
import { Button } from '@/components/ui/button'
import { SheetTitle } from '@/components/ui/sheet'
import { Skeleton } from '@/components/ui/skeleton'
import { formatShortDate, todayInSaoPaulo } from '@/lib/dates'
import { formatCents } from '@/lib/money'
import { cn } from '@/lib/utils'
import { DepositSheet, type DepositView } from './DepositSheet'
import {
  balanceLabels,
  canRedeem,
  depositRedeemedNote,
  isFullyRedeemed,
  redeemedLine,
  splitByRedemption,
  type Deposit,
  depositCountLabel,
  depositTaxNote,
  depositUnavailableNote,
  depositYield,
  depositTitle,
  lettersTaxFootnote,
  taxScope,
  goalProgress,
  limitationsNote,
  allDeposits,
  ratesScope,
  looseGoalParam,
  periodLabels,
  periodRange,
  periods,
  progressLabel,
  ratesFootnote,
  ratesNotice,
  shownBalance,
  shownYield,
  taxFootnote,
  valueModeLabels,
  valueModes,
  yieldLabels,
  type Balances,
  type DepositBalance,
  type Goal,
  type GroupBalance,
  type Period,
  type ValueMode,
} from './fixedIncome'
import {
  useDeleteGoal,
  useFixedIncomeBalances,
  useFixedIncomeDeposits,
  useFixedIncomeEvolution,
  useGoals,
  useRestoreGoal,
} from './fixedIncomeQueries'
import { GoalSheet, type GoalView } from './GoalSheet'
import { RedemptionSheet } from './RedemptionSheet'

const EvolutionChart = lazy(() => import('./EvolutionChart'))

// A aba Objetivos (docs/renda-fixa.md, seção 7): cada objetivo é uma caixa de renda fixa (CDB a % do CDI). Só o
// bruto; a nota embaixo diz até quando vão as taxas. O objetivo aberto fica no endereço (?objetivo=<id>|sem).
export function GoalsTab({ goalParam, onOpen }: { goalParam: string | null; onOpen: (param: string | null) => void }) {
  const goals = useGoals()
  const balances = useFixedIncomeBalances()
  const [goalView, setGoalView] = useState<GoalView | null>(null)
  const [depositView, setDepositView] = useState<DepositView | null>(null)
  const [redeeming, setRedeeming] = useState<Deposit | null>(null)
  const [mode, setMode] = useState<ValueMode>('gross')

  const goalList = goals.data?.goals ?? []
  const failed = goals.isError || balances.isError

  if (goals.isPending || balances.isPending)
    return failed ? null : <Skeleton className="h-48 w-full rounded-2xl" />
  if (failed || !balances.data || !goals.data)
    return (
      <Alert variant="destructive">
        <AlertDescription>Não foi possível carregar seus objetivos.</AlertDescription>
      </Alert>
    )

  const data = balances.data
  const open = goalParam === looseGoalParam ? 'loose' : goalList.find((g) => g.id === goalParam)

  return (
    <div className="flex flex-col gap-5">
      {(goalList.length > 0 || goals.data.unassigned.depositCount > 0) && <ModeToggle mode={mode} onChange={setMode} />}
      {open ? (
        <GoalDetail
          goal={open === 'loose' ? null : open}
          balances={data}
          mode={mode}
          onBack={() => onOpen(null)}
          onEdit={(goal) => setGoalView({ kind: 'edit', goal })}
          onDeposit={setDepositView}
          onRedeem={setRedeeming}
          onDeleted={() => onOpen(null)}
        />
      ) : (
        <GoalList
          goals={goalList}
          unassigned={goals.data.unassigned}
          balances={data}
          mode={mode}
          onOpen={onOpen}
          onNewGoal={() => setGoalView({ kind: 'new' })}
          onNewDeposit={() => setDepositView({ kind: 'new', goalId: null })}
        />
      )}
      <GoalSheet view={goalView} onClose={() => setGoalView(null)} onSaved={(id) => goalView?.kind === 'new' && onOpen(id)} />
      <DepositSheet view={depositView} goals={goalList} onClose={() => setDepositView(null)} />
      <RedemptionSheet deposit={redeeming} onClose={() => setRedeeming(null)} />
    </div>
  )
}

// Bruto ou líquido estimado: vale para a lista e para o detalhe. O líquido é o bruto menos o imposto de resgatar hoje.
function ModeToggle({ mode, onChange }: { mode: ValueMode; onChange: (mode: ValueMode) => void }) {
  return (
    <div role="group" aria-label="Valores" className="surface grid grid-cols-2 gap-1 rounded-full p-1">
      {valueModes.map((m) => (
        <button
          key={m}
          type="button"
          aria-pressed={m === mode}
          onClick={() => onChange(m)}
          className={cn(
            'h-9 rounded-full border-2 border-transparent text-sm transition-colors outline-none focus-visible:ring-2 focus-visible:ring-ring/50',
            m === mode ? 'spectrum-ring font-medium text-foreground' : 'text-muted-foreground hover:text-foreground',
          )}
        >
          {valueModeLabels[m]}
        </button>
      ))}
    </div>
  )
}

// O aviso das taxas do CDI só vale para quem usa o CDI: uma lista só de pré-fixado não depende delas.
function Notices({ balances, deposits }: { balances: Balances; deposits: DepositBalance[] }) {
  const notice = ratesScope(deposits) === 'prefixed' ? null : ratesNotice(balances.rates)
  return (
    <>
      {notice && (
        <Alert>
          <Info />
          <AlertDescription>{notice.text}</AlertDescription>
        </Alert>
      )}
    </>
  )
}

function Footnote({ balances, deposits, mode }: { balances: Balances; deposits: DepositBalance[]; mode: ValueMode }) {
  const tax = taxScope(deposits)
  return (
    <div className="flex flex-col gap-1 px-1 text-xs text-muted-foreground">
      <p>{ratesFootnote(balances.rates, ratesScope(deposits))}</p>
      <p>{limitationsNote}</p>
      {mode === 'net' && tax.cdb && <p>{taxFootnote}</p>}
      {mode === 'net' && tax.letters && <p>{lettersTaxFootnote}</p>}
    </div>
  )
}

function GoalList({
  goals,
  unassigned,
  balances,
  mode,
  onOpen,
  onNewGoal,
  onNewDeposit,
}: {
  goals: Goal[]
  unassigned: { depositCount: number; principalCents: number }
  balances: Balances
  mode: ValueMode
  onOpen: (param: string) => void
  onNewGoal: () => void
  onNewDeposit: () => void
}) {
  const byId = new Map(balances.goals.map((g) => [g.id, g]))
  const total = balances.total

  if (goals.length === 0 && unassigned.depositCount === 0)
    return (
      <div className="surface flex flex-col items-start gap-3 rounded-2xl p-5">
        <h2 className="font-display text-2xl">Renda fixa por objetivo</h2>
        <p className="text-sm text-muted-foreground">
          Crie um objetivo (reserva, viagem, o que for) e lance os aportes em CDB que rendem um percentual do CDI. O Prisma
          calcula quanto cada um rendeu até hoje.
        </p>
        <Button className="h-11 rounded-2xl" onClick={onNewGoal}>
          <Plus /> Criar objetivo
        </Button>
      </div>
    )

  return (
    <>
      <Notices balances={balances} deposits={allDeposits(balances)} />
      <section className="surface flex flex-col gap-1 rounded-2xl p-5">
        <p className="text-sm text-muted-foreground">{balanceLabels[mode]}</p>
        <p className="font-display text-4xl leading-none tabular-nums">{formatCents(shownBalance(total, mode))}</p>
        <p className="text-sm text-muted-foreground">
          Aportado {formatCents(total.principalCents)} · {yieldLabels[mode].toLowerCase()}{' '}
          <span className="text-spectrum font-medium tabular-nums">{formatCents(shownYield(total, mode))}</span>
        </p>
        {mode === 'net' && (
          <p className="text-sm text-muted-foreground">
            Imposto estimado (se resgatar hoje) <span className="font-medium tabular-nums">{formatCents(total.estimatedTaxCents)}</span>
          </p>
        )}
        {redeemedLine(total) && <p className="text-sm text-muted-foreground">{redeemedLine(total)}</p>}
      </section>

      <ul className="flex flex-col gap-3">
        {goals.map((goal) => {
          const balance = byId.get(goal.id)?.balance
          return (
            <li key={goal.id}>
              <GoalCard
                name={goal.name}
                balance={balance}
                mode={mode}
                principalCents={goal.principalCents}
                count={goal.depositCount}
                targetCents={goal.targetCents}
                onClick={() => onOpen(goal.id)}
              />
            </li>
          )
        })}
        {unassigned.depositCount > 0 && (
          <li>
            <GoalCard
              name="Sem objetivo"
              balance={balances.unassigned.balance}
              mode={mode}
              principalCents={unassigned.principalCents}
              count={unassigned.depositCount}
              targetCents={null}
              onClick={() => onOpen(looseGoalParam)}
            />
          </li>
        )}
      </ul>

      <div className="flex gap-2">
        <Button variant="outline" className="h-11 flex-1 rounded-2xl" onClick={onNewGoal}>
          <Plus /> Novo objetivo
        </Button>
        <Button className="h-11 flex-1 rounded-2xl" onClick={onNewDeposit}>
          <Plus /> Novo aporte
        </Button>
      </div>
      <Footnote balances={balances} deposits={allDeposits(balances)} mode={mode} />
    </>
  )
}

function GoalCard({
  name,
  balance,
  mode,
  principalCents,
  count,
  targetCents,
  onClick,
}: {
  name: string
  balance: GroupBalance | undefined
  mode: ValueMode
  principalCents: number
  count: number
  targetCents: number | null
  onClick: () => void
}) {
  const shown = balance ? shownBalance(balance, mode) : principalCents
  const yieldCents = balance ? shownYield(balance, mode) : 0
  const progress = goalProgress(shown, targetCents)
  return (
    <button
      type="button"
      onClick={onClick}
      className="surface flex w-full flex-col gap-2 rounded-2xl p-4 text-left transition-colors active:bg-muted/70"
    >
      <span className="flex items-center justify-between gap-2">
        <span className="min-w-0 truncate font-medium">{name}</span>
        <ChevronRight className="size-4 shrink-0 text-muted-foreground" />
      </span>
      <span className="font-display text-3xl leading-none tabular-nums">{formatCents(shown)}</span>
      {progress !== null && (
        <span className="flex flex-col gap-1">
          <span className="block h-1.5 overflow-hidden rounded-full bg-muted">
            <span
              className="meter-fill block h-full bg-[image:var(--spectrum-cool)]"
              style={{ '--fill': `${progress * 100}%` } as React.CSSProperties}
            />
          </span>
          <span className="text-xs text-muted-foreground">{progressLabel(shown, targetCents)} de {formatCents(targetCents ?? 0)}</span>
        </span>
      )}
      <span className="text-sm text-muted-foreground">
        {depositCountLabel(count)} · aportado {formatCents(principalCents)} · rende{' '}
        <span className="text-spectrum font-medium tabular-nums">{formatCents(yieldCents)}</span>
      </span>
    </button>
  )
}

function GoalDetail({
  goal,
  balances,
  mode,
  onBack,
  onEdit,
  onDeposit,
  onRedeem,
  onDeleted,
}: {
  goal: Goal | null
  balances: Balances
  mode: ValueMode
  onBack: () => void
  onEdit: (goal: Goal) => void
  onDeposit: (view: DepositView) => void
  onRedeem: (deposit: Deposit) => void
  onDeleted: () => void
}) {
  const today = todayInSaoPaulo()
  const scope = goal ? goal.id : 'none'
  const entry = goal ? balances.goals.find((g) => g.id === goal.id) : balances.unassigned
  const balance = entry?.balance
  const deposits = useFixedIncomeDeposits(scope)
  const balanceOf = new Map<string, DepositBalance>((entry?.deposits ?? []).map((d) => [d.depositId, d]))
  const { open, redeemed } = splitByRedemption(deposits.data ?? [], balanceOf)
  const [showRedeemed, setShowRedeemed] = useState(false)
  const first = (deposits.data ?? []).map((d) => d.depositDate).sort()[0] ?? null
  const [period, setPeriod] = useState<Period>('ALL')
  const range = periodRange(period, today, first)
  const evolution = useFixedIncomeEvolution(scope, range.from, range.step)
  const [asking, setAsking] = useState(false)
  const remove = useDeleteGoal()
  const restore = useRestoreGoal()

  const principal = balance?.principalCents ?? goal?.principalCents ?? 0
  const shown = balance ? shownBalance(balance, mode) : principal
  const hasDeposits = (deposits.data?.length ?? 0) > 0

  const deleteGoal = async (choice?: 'keep' | 'delete') => {
    if (!goal) return
    try {
      const result = await remove.mutateAsync({ id: goal.id, deposits: choice })
      setAsking(false)
      onDeleted()
      toast(`Objetivo "${goal.name}" excluído`, {
        description: result.deletedDepositIds.length > 0 ? 'Os aportes foram junto.' : result.detachedDepositIds.length > 0 ? 'Os aportes ficaram em "Sem objetivo".' : undefined,
        duration: 8000,
        action: { label: 'Desfazer', onClick: () => restore.mutate({ id: goal.id, detachedDepositIds: result.detachedDepositIds }) },
      })
    } catch (error) {
      setAsking(false)
      toast.error((error as Error).message)
    }
  }

  return (
    <>
      <div className="flex items-center justify-between">
        <Button variant="ghost" size="sm" className="-ml-2 rounded-full text-muted-foreground" onClick={onBack}>
          <ArrowLeft /> Objetivos
        </Button>
        {goal && (
          <Button variant="ghost" size="sm" className="rounded-full" onClick={() => onEdit(goal)}>
            Editar
          </Button>
        )}
      </div>
      <Notices balances={balances} deposits={entry?.deposits ?? []} />
      <section className="flex flex-col gap-1 px-1">
        <h2 className="text-sm text-muted-foreground">{goal?.name ?? 'Sem objetivo'}</h2>
        <p className="font-display text-4xl leading-none tabular-nums">{formatCents(shown)}</p>
        <p className="text-sm text-muted-foreground">
          Aportado {formatCents(principal)} · {yieldLabels[mode].toLowerCase()}{' '}
          <span className="text-spectrum font-medium tabular-nums">{formatCents(balance ? shownYield(balance, mode) : 0)}</span>
          {balance?.ratesThrough && ` até ${formatShortDate(balance.ratesThrough)}`}
        </p>
        {mode === 'net' && balance && (
          <p className="text-sm text-muted-foreground">
            Imposto estimado (se resgatar hoje) <span className="font-medium tabular-nums">{formatCents(balance.estimatedTaxCents)}</span>
          </p>
        )}
        {balance && redeemedLine(balance) && <p className="text-sm text-muted-foreground">{redeemedLine(balance)}</p>}
      </section>

      {hasDeposits && (
        <section className="surface flex flex-col gap-3 rounded-2xl p-4">
          <div className="flex gap-1" role="group" aria-label="Período">
            {periods.map((p) => (
              <button
                key={p}
                type="button"
                aria-pressed={p === period}
                onClick={() => setPeriod(p)}
                className={cn(
                  'h-8 rounded-full border-2 border-transparent px-3 text-xs transition-colors',
                  p === period ? 'spectrum-ring font-medium' : 'text-muted-foreground hover:text-foreground',
                )}
              >
                {periodLabels[p]}
              </button>
            ))}
          </div>
          <StaleFade stale={evolution.isPlaceholderData}>
            {evolution.data && evolution.data.points.length > 1 ? (
              <Suspense fallback={<div className="h-44" />}>
                <EvolutionChart points={evolution.data.points} />
              </Suspense>
            ) : (
              <p className="py-10 text-center text-sm text-muted-foreground">Ainda não há dias suficientes para o gráfico.</p>
            )}
          </StaleFade>
          <div className="flex flex-wrap items-center gap-x-4 gap-y-1 text-xs text-muted-foreground">
            <span className="flex items-center gap-1.5">
              <span className="size-2.5 rounded-full bg-foreground/80" /> Aportado
            </span>
            <span className="flex items-center gap-1.5">
              <span className="size-2.5 rounded-full bg-[linear-gradient(#8b5cf6,#22d3ee)]" /> Saldo bruto
            </span>
            {mode === 'net' && <span>O gráfico mostra valores brutos.</span>}
            {(balance?.redeemedGrossCents ?? 0) > 0 && <span>Os resgates aparecem como queda no saldo.</span>}
          </div>
        </section>
      )}

      <section className="flex flex-col gap-2">
        <div className="flex items-center justify-between px-1">
          <h3 className="text-sm font-medium text-foreground/75">Aportes</h3>
          <Button size="sm" className="rounded-full" onClick={() => onDeposit({ kind: 'new', goalId: goal?.id ?? null })}>
            <Plus /> Aporte
          </Button>
        </div>
        {deposits.isPending ? (
          <Skeleton className="h-24 w-full rounded-2xl" />
        ) : !hasDeposits ? (
          <p className="surface rounded-2xl p-4 text-sm text-muted-foreground">Nenhum aporte neste objetivo ainda.</p>
        ) : (
          <ul className="surface divide-y divide-border/60 rounded-2xl">
            {open.map((d) => (
              <DepositRow
                key={d.id}
                deposit={d}
                balance={balanceOf.get(d.id)}
                mode={mode}
                onEdit={() => onDeposit({ kind: 'edit', deposit: d })}
                onRedeem={() => onRedeem(d)}
              />
            ))}
          </ul>
        )}
      </section>

      {redeemed.length > 0 && (
        <section className="flex flex-col gap-2">
          <Button
            variant="ghost"
            size="sm"
            className="-ml-2 self-start rounded-full px-3 text-foreground/75"
            aria-expanded={showRedeemed}
            onClick={() => setShowRedeemed((v) => !v)}
          >
            Resgatados ({redeemed.length})
            <ChevronDown className={cn('transition-transform', showRedeemed && 'rotate-180')} />
          </Button>
          {showRedeemed && (
            <ul className="surface divide-y divide-border/60 rounded-2xl">
              {redeemed.map((d) => (
                <DepositRow
                  key={d.id}
                  deposit={d}
                  balance={balanceOf.get(d.id)}
                  mode={mode}
                  onEdit={() => onDeposit({ kind: 'edit', deposit: d })}
                  onRedeem={() => onRedeem(d)}
                />
              ))}
            </ul>
          )}
        </section>
      )}

      <Footnote balances={balances} deposits={entry?.deposits ?? []} mode={mode} />

      {goal && (
        <Button
          variant="ghost"
          className="self-start rounded-full text-muted-foreground"
          disabled={remove.isPending}
          onClick={() => (hasDeposits ? setAsking(true) : deleteGoal())}
        >
          Excluir objetivo
        </Button>
      )}

      <BottomSheet open={asking} onClose={() => setAsking(false)}>
        <header className="px-4 pt-2 pb-3">
          <SheetTitle className="font-display text-2xl font-normal">Excluir "{goal?.name}"</SheetTitle>
          <p className="text-sm text-muted-foreground">E os investimentos dele ({depositCountLabel(deposits.data?.length ?? 0)})?</p>
        </header>
        <div className="flex flex-col gap-2 px-4 pb-6">
          <Button size="lg" className="h-12 rounded-2xl" disabled={remove.isPending} onClick={() => deleteGoal('keep')}>
            Manter os investimentos
          </Button>
          <p className="px-1 text-xs text-muted-foreground">Eles continuam rendendo, em "Sem objetivo".</p>
          <Button variant="outline" size="lg" className="h-12 rounded-2xl" disabled={remove.isPending} onClick={() => deleteGoal('delete')}>
            Excluir os investimentos também
          </Button>
          <Button variant="ghost" size="lg" className="h-12 rounded-2xl" onClick={() => setAsking(false)}>
            Cancelar
          </Button>
        </div>
      </BottomSheet>
    </>
  )
}

// Uma linha de aporte. O botão "Resgatar" fica fora do botão da linha (que abre a edição) e só aparece se ainda sobra principal. O aporte
// resgatado por inteiro mostra o que recebeu, não um rendimento (já não rende).
function DepositRow({
  deposit: d,
  balance: b,
  mode,
  onEdit,
  onRedeem,
}: {
  deposit: Deposit
  balance: DepositBalance | undefined
  mode: ValueMode
  onEdit: () => void
  onRedeem: () => void
}) {
  const note = b ? depositUnavailableNote(b) : null
  const yieldCents = b && !isFullyRedeemed(b) ? depositYield(b, mode) : null
  return (
    <li>
      <button
        type="button"
        onClick={onEdit}
        className="flex w-full items-center gap-3 px-4 py-3 text-left transition-colors active:bg-muted/70"
      >
        <span className="min-w-0 flex-1">
          <span className="block font-medium tabular-nums">{formatCents(d.amountCents)}</span>
          <span className="block text-sm text-muted-foreground">
            {formatShortDate(d.depositDate)} · {depositTitle(d.productKind, d.indexKind, d.rateE4)}
            {d.maturityDate && ` · vence ${formatShortDate(d.maturityDate)}`}
            {d.funding && ' · saiu de uma conta'}
          </span>
          {b?.redeemed && (
            <span className="block text-xs text-muted-foreground">{depositRedeemedNote(b.redeemed, b.principalCents)}</span>
          )}
          {b?.redeemed && isFullyRedeemed(b) && (
            <span className="block text-xs text-muted-foreground">
              Recebido {formatCents(b.redeemed.netCents)} · imposto pago {formatCents(b.redeemed.taxCents)}
            </span>
          )}
          {mode === 'net' && b?.tax && !isFullyRedeemed(b) && (
            <span className="block text-xs text-muted-foreground">
              Imposto estimado {formatCents(b.tax.totalCents)} · {depositTaxNote(b.tax)}
            </span>
          )}
          {note && <span className="block text-xs text-muted-foreground">{note}</span>}
        </span>
        <span className="text-spectrum shrink-0 text-sm font-medium tabular-nums">
          {yieldCents !== null ? `+${formatCents(yieldCents)}` : ''}
        </span>
      </button>
      {canRedeem(b) && (
        <div className="px-4 pb-3">
          <Button data-tint="series" className="tinted-action h-9 rounded-xl px-4 text-sm" onClick={onRedeem}>
            Resgatar
          </Button>
        </div>
      )}
    </li>
  )
}
