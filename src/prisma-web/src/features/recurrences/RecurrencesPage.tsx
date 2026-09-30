import { ArrowLeft, ChevronRight, Pencil, Power, Repeat } from 'lucide-react'
import { useMemo, useState, type FormEvent, type ReactNode } from 'react'
import { Link } from 'react-router'
import { toast } from 'sonner'
import { BottomSheet, SheetFooterBar } from '@/components/BottomSheet'
import { AccountTile, EntryTile } from '@/components/brand/Tiles'
import { Alert, AlertDescription } from '@/components/ui/alert'
import { Button } from '@/components/ui/button'
import { Input } from '@/components/ui/input'
import { SheetDescription, SheetTitle } from '@/components/ui/sheet'
import { Skeleton } from '@/components/ui/skeleton'
import { paymentMethodLabels, type PaymentMethod } from '@/features/accounts/labels'
import { useAccounts, type Account } from '@/features/accounts/queries'
import {
  categoryLabels,
  resolveCategory,
  useCategories,
  type CategoryLabel,
  type CategoryNode,
} from '@/features/categories/queries'
import { Amount } from '@/features/transactions/Amount'
import { AmountField, CategoryPicker, Chip, ChipRow, Section } from '@/features/transactions/fields'
import { ApiError } from '@/lib/api'
import { addDays, formatLongDate, formatShortDate, todayInSaoPaulo } from '@/lib/dates'
import { formatCents } from '@/lib/money'
import { cn } from '@/lib/utils'
import { groupSeries, pendingTexts, seriesWhen, type Pending } from './listing'
import {
  useDiscardPending,
  useEndRecurrence,
  useLaunchPending,
  useRecurrences,
  useUpdateRecurrence,
  type PendingLaunch,
  type Recurrence,
} from './queries'
import { reopenRequest } from './removal'
import { frequencyText, type Frequency } from './schedule'

// "Recorrências" (docs/fase-2.md, 2.14, Tela): as pendências no topo, os débitos automáticos (2.15), as outras
// séries ativas pela próxima data e as encerradas no fim. Tocar numa série abre o painel com editar e encerrar.
export function RecurrencesPage() {
  const recurrences = useRecurrences()
  const accounts = useAccounts()
  const categories = useCategories()
  const labels = useMemo(() => categoryLabels(categories.data ?? []), [categories.data])
  const [openId, setOpenId] = useState<string>()
  const today = todayInSaoPaulo()

  const groups = groupSeries(recurrences.data ?? [])
  const open = recurrences.data?.find((r) => r.id === openId)
  const accountOf = (id: string) => accounts.data?.find((a) => a.id === id)

  return (
    <main className="mx-auto flex max-w-2xl flex-col gap-6 px-4 pt-3 pb-32">
      <nav>
        <Button asChild variant="ghost" size="sm" className="-ml-2 rounded-full text-muted-foreground">
          <Link to="/contas">
            <ArrowLeft />
            Contas
          </Link>
        </Button>
      </nav>
      <div className="flex flex-col gap-3">
        <h1 className="font-display text-4xl leading-none">Recorrências</h1>
        <div className="spectrum-line opacity-80" />
      </div>

      {(recurrences.isPending || accounts.isPending || categories.isPending) && <Skeleton className="h-40 w-full rounded-2xl" />}
      {(recurrences.isError || accounts.isError || categories.isError) && (
        <Alert variant="destructive">
          <AlertDescription>Não foi possível carregar as recorrências.</AlertDescription>
        </Alert>
      )}

      {recurrences.isSuccess && recurrences.data.length === 0 && (
        <div className="flex flex-col items-center gap-2 rounded-3xl border border-dashed px-6 py-12 text-center">
          <Repeat className="size-8 text-muted-foreground" />
          <p className="font-display text-2xl">Nada se repete ainda</p>
          <p className="text-sm text-pretty text-muted-foreground">
            Ao lançar, toque em "Não se repete" e escolha toda semana ou todo mês.
          </p>
        </div>
      )}

      {groups.pendings.length > 0 && (
        <section className="flex flex-col gap-2">
          <h2 className="px-1 text-sm font-medium text-foreground/75">Esperando você</h2>
          {groups.pendings.map(({ series, pending }) => (
            <PendingCard
              key={pending.id}
              series={series}
              pending={pending}
              labels={labels}
              account={accountOf(pending.accountId)}
            />
          ))}
        </section>
      )}

      {groups.autoDebits.length > 0 && (
        <SeriesList title="Débitos automáticos">
          {groups.autoDebits.map((r) => (
            <SeriesRow key={r.id} series={r} account={accountOf(r.accountId)} labels={labels} today={today} onOpen={setOpenId} />
          ))}
        </SeriesList>
      )}

      {groups.active.length > 0 && (
        <SeriesList title={groups.autoDebits.length > 0 ? 'Outras recorrências' : 'Ativas'}>
          {groups.active.map((r) => (
            <SeriesRow key={r.id} series={r} account={accountOf(r.accountId)} labels={labels} today={today} onOpen={setOpenId} />
          ))}
        </SeriesList>
      )}

      {groups.ended.length > 0 && (
        <SeriesList title="Encerradas">
          {groups.ended.map((r) => (
            <SeriesRow key={r.id} series={r} account={accountOf(r.accountId)} labels={labels} today={today} onOpen={setOpenId} />
          ))}
        </SeriesList>
      )}

      <BottomSheet open={open !== undefined} onClose={() => setOpenId(undefined)}>
        {open && (
          <SeriesSheet
            key={open.id}
            series={open}
            accounts={accounts.data ?? []}
            categories={categories.data ?? []}
            labels={labels}
            onClose={() => setOpenId(undefined)}
          />
        )}
      </BottomSheet>
    </main>
  )
}

function SeriesList({ title, children }: { title: string; children: ReactNode }) {
  return (
    <section className="flex flex-col gap-2">
      <h2 className="px-1 text-sm font-medium text-foreground/75">{title}</h2>
      <ul className="surface overflow-hidden rounded-2xl">{children}</ul>
    </section>
  )
}

function SeriesRow({
  series,
  account,
  labels,
  today,
  onOpen,
}: {
  series: Recurrence
  account: Account | undefined
  labels: Map<string, CategoryLabel>
  today: string
  onOpen: (id: string) => void
}) {
  const category = series.categoryId ? labels.get(series.categoryId) : undefined
  const when = seriesWhen(series, today)
  return (
    <li className="[&+&]:border-t [&+&]:border-border/60">
      <button
        type="button"
        onClick={() => onOpen(series.id)}
        className={cn(
          'flex w-full items-center gap-3 px-4 py-3 text-left transition-colors outline-none hover:bg-muted/40 focus-visible:bg-muted/60 active:bg-muted/70',
          series.isEnded && 'opacity-60',
        )}
      >
        <EntryTile description={series.description} category={category} />
        {/* O valor fica na linha do título: em 320px, ao lado de tudo, espremia a frequência em cinco linhas. */}
        <span className="min-w-0 flex-1">
          <span className="flex items-baseline justify-between gap-3">
            <span className="truncate font-medium">{series.description || category?.name || 'Sem descrição'}</span>
            <Amount type={series.type} cents={series.amountCents} className="shrink-0 font-medium" />
          </span>
          <span className="block text-sm text-pretty text-muted-foreground">{when}</span>
          {/* Valor que muda: o da série é a média (docs/fase-2.md, 2.15). Aqui, e não no valor: com o "≈" ao lado do
              valor, o nome ficava cortado em 320px. */}
          {(account || series.amountVaries) && (
            <span className="block truncate text-xs text-muted-foreground">
              {[account?.name, series.amountVaries && 'valor médio'].filter(Boolean).join(' · ')}
            </span>
          )}
        </span>
        <ChevronRight className="size-4 shrink-0 text-muted-foreground" />
      </button>
    </li>
  )
}

// A cobrança que cairia numa fatura já paga (regra 9) e as três saídas. Descartar pede confirmação: não
// tem volta pela tela.
function PendingCard({
  series,
  pending,
  labels,
  account,
}: {
  series: Recurrence
  pending: Pending
  labels: Map<string, CategoryLabel>
  account: Account | undefined
}) {
  const launch = useLaunchPending()
  const discard = useDiscardPending()
  const [confirming, setConfirming] = useState(false)
  const texts = pendingTexts(pending)
  const category = series.categoryId ? labels.get(series.categoryId) : undefined
  const busy = launch.isPending || discard.isPending
  const title = series.description || category?.name || 'Cobrança'

  const run = async (where: PendingLaunch) => {
    try {
      const charge = await launch.mutateAsync({ id: pending.id, where })
      toast.success(where === 'NextStatement' ? texts.launchedNext : texts.launchedSame, {
        description: `${title} · ${formatCents(pending.amountCents)} · vence ${formatShortDate(charge.settlementDate)}`,
      })
    } catch (error) {
      toast.error(messageOf(error))
    }
  }

  const drop = async () => {
    try {
      await discard.mutateAsync(pending.id)
      toast.success('Cobrança descartada', { description: `${title} de ${formatShortDate(pending.occurrenceDate)}` })
    } catch (error) {
      toast.error(messageOf(error))
    }
  }

  return (
    <article className="spectrum-ring flex flex-col gap-3 rounded-2xl p-4">
      <div className="flex items-center gap-3">
        <EntryTile description={series.description} category={category} />
        <div className="min-w-0 flex-1">
          <p className="flex items-baseline justify-between gap-3">
            <span className="truncate font-medium">{title}</span>
            <span className="shrink-0 font-medium tabular-nums">{formatCents(pending.amountCents)}</span>
          </p>
          <p className="text-sm text-muted-foreground">
            {formatShortDate(pending.occurrenceDate)}
            {account ? ` · ${account.name}` : ''}
          </p>
        </div>
      </div>
      <p className="text-sm">{texts.reason}</p>
      {confirming ? (
        <div className="flex flex-col gap-2">
          <p className="text-sm text-muted-foreground">Ela não será lançada. Descartar mesmo?</p>
          <div className="flex flex-wrap gap-2">
            <Button variant="outline" className="h-11 flex-1 rounded-2xl" disabled={busy} onClick={drop}>
              Descartar
            </Button>
            <Button variant="ghost" className="h-11 flex-1 rounded-2xl" disabled={busy} onClick={() => setConfirming(false)}>
              Voltar
            </Button>
          </div>
        </div>
      ) : (
        <div className="flex flex-col gap-2">
          <Button className="h-11 rounded-2xl" disabled={busy} onClick={() => run('NextStatement')}>
            {texts.next}
          </Button>
          <div className="flex flex-wrap gap-2">
            <Button
              variant="outline"
              className="h-11 min-w-fit flex-[1_1_0%] rounded-2xl"
              disabled={busy}
              onClick={() => run('SameStatement')}
            >
              {texts.same}
            </Button>
            <Button
              variant="ghost"
              className="h-11 min-w-fit flex-[1_1_0%] rounded-2xl"
              disabled={busy}
              onClick={() => setConfirming(true)}
            >
              Descartar
            </Button>
          </div>
          <p className="px-1 text-xs text-muted-foreground">{texts.sameHint}</p>
        </div>
      )}
    </article>
  )
}

type SheetProps = {
  series: Recurrence
  accounts: Account[]
  categories: CategoryNode[]
  labels: Map<string, CategoryLabel>
  onClose: () => void
}

function SeriesSheet(props: SheetProps) {
  const [editing, setEditing] = useState(false)
  return editing ? (
    <EditSeries {...props} onDone={() => setEditing(false)} />
  ) : (
    <SeriesDetails {...props} onEdit={() => setEditing(true)} />
  )
}

function SeriesDetails({ series, accounts, labels, onClose, onEdit }: SheetProps & { onEdit: () => void }) {
  const end = useEndRecurrence()
  const update = useUpdateRecurrence()
  const category = series.categoryId ? labels.get(series.categoryId) : undefined
  const account = accounts.find((a) => a.id === series.accountId)
  const title = series.description || category?.name || 'Sem descrição'
  const autoDebit = series.kind === 'AutoDebit'

  // Encerrar (regra 7): o término vai para o último lançamento; o "Desfazer" reabre com o término de antes.
  const finish = async () => {
    try {
      await end.mutateAsync(series.id)
    } catch (error) {
      toast.error(messageOf(error))
      return
    }
    onClose()
    toast('Série encerrada', {
      description: `${title}. O que já foi lançado continua nos lançamentos.`,
      duration: 8000,
      action: {
        label: 'Desfazer',
        onClick: () => {
          update
            .mutateAsync({ id: series.id, body: reopenRequest(series) })
            .then(() => toast.success('Série reaberta'))
            .catch((error: unknown) => toast.error(messageOf(error)))
        },
      },
    })
  }

  return (
    <>
      <div className="flex min-h-0 flex-col overflow-y-auto overscroll-contain">
        <header className="flex flex-col items-center gap-3 px-6 pt-4 pb-5 text-center">
          <EntryTile description={series.description} category={category} size="xl" />
          <SheetTitle className="font-display text-2xl leading-tight font-normal">{title}</SheetTitle>
          <Amount type={series.type} cents={series.amountCents} className="font-display text-5xl leading-none font-normal" />
          <p className="text-sm text-muted-foreground">
            {autoDebit
              ? `Débito automático, vence dia ${Number(series.startDate.slice(8, 10))}${series.amountVaries ? ' · valor médio' : ''}`
              : frequencyText(series.startDate, series.frequency)}
          </p>
        </header>
        <div className="spectrum-line mx-6 opacity-70" />
        <dl className="surface mx-4 my-5 flex flex-col divide-y divide-border/60 rounded-2xl text-sm">
          {autoDebit ? (
            <>
              <InfoRow label="Próximo vencimento">
                {series.nextOccurrence ? formatLongDate(series.nextOccurrence) : 'nenhum'}
              </InfoRow>
              {series.nextTransactionDate && series.nextTransactionDate !== series.nextOccurrence && (
                <InfoRow label="Debitado em">{formatLongDate(series.nextTransactionDate)}</InfoRow>
              )}
            </>
          ) : (
            <InfoRow label="Próxima">{series.nextOccurrence ? formatLongDate(series.nextOccurrence) : 'nenhuma'}</InfoRow>
          )}
          <InfoRow label="Termina">
            {series.isEnded
              ? `encerrada em ${formatShortDate(series.generatedThrough)}`
              : series.endDate
                ? formatLongDate(series.endDate)
                : 'nunca'}
          </InfoRow>
          {account && (
            <InfoRow label="Conta">
              <span className="flex items-center gap-2">
                <AccountTile name={account.name} type={account.type} size="sm" />
                {account.name}
              </span>
            </InfoRow>
          )}
          <InfoRow label="Pagamento">{paymentMethodLabels[series.method]}</InfoRow>
          <InfoRow label="Último lançado">{formatShortDate(series.generatedThrough)}</InfoRow>
        </dl>
        <p className="mx-4 mb-5 rounded-2xl bg-muted/60 px-4 py-3 text-xs leading-relaxed text-muted-foreground">
          {series.isEnded
            ? 'Nada mais é lançado. O que já foi lançado continua nos lançamentos.'
            : autoDebit
              ? `Cada débito sai no dia útil do vencimento${series.amountVaries ? ', com o valor médio, e vai para o sino para você conferir' : ''}. Editar vale do próximo em diante.`
              : 'Cada lançamento sai no dia dele. Editar vale do próximo em diante; o que já foi lançado fica.'}
        </p>
      </div>
      {!series.isEnded && (
        <SheetFooterBar className="grid grid-cols-[1fr_auto] gap-2">
          <Button size="lg" className="h-12 rounded-2xl text-base" onClick={onEdit}>
            <Pencil />
            Editar
          </Button>
          <Button
            size="lg"
            variant="outline"
            className="h-12 rounded-2xl px-5 text-base"
            disabled={end.isPending}
            onClick={finish}
          >
            <Power />
            Encerrar
          </Button>
        </SheetFooterBar>
      )}
    </>
  )
}

function InfoRow({ label, children }: { label: string; children: ReactNode }) {
  return (
    <div className="flex items-center justify-between gap-4 px-4 py-3">
      <dt className="shrink-0 text-muted-foreground">{label}</dt>
      <dd className="text-right font-medium">{children}</dd>
    </div>
  )
}

// Fora do cartão não existe "Crédito" (a API recusa).
const simpleMethods: PaymentMethod[] = ['Pix', 'Debit', 'Cash', 'Boleto', 'Ted']
const frequencies: { value: Frequency; label: string }[] = [
  { value: 'Weekly', label: 'Toda semana' },
  { value: 'Monthly', label: 'Todo mês' },
]

// Editar vale do próximo em diante (regra 6): o que já foi lançado fica. Conta só do mesmo tipo (cartão por
// cartão). A próxima data vira a nova partida; é obrigatória ao trocar a frequência (a API recusa sem ela).
function EditSeries({ series, accounts, categories, onDone }: SheetProps & { onDone: () => void }) {
  const update = useUpdateRecurrence()
  const onCard = series.method === 'Credit'
  const [amountCents, setAmountCents] = useState(series.amountCents)
  const [description, setDescription] = useState(series.description)
  const [categoryId, setCategoryId] = useState(series.categoryId ?? '')
  const [accountId, setAccountId] = useState(series.accountId)
  const [method, setMethod] = useState<PaymentMethod>(series.method)
  const [frequency, setFrequency] = useState<Frequency>(series.frequency)
  const [nextDate, setNextDate] = useState(series.nextOccurrence ?? '')
  const [endDate, setEndDate] = useState<string | null>(series.endDate)
  // Débito automático (docs/fase-2.md, 2.15, regra 10): só despesa em conta corrente, todo mês, no débito.
  const [autoDebit, setAutoDebit] = useState(series.kind === 'AutoDebit')
  const [amountVaries, setAmountVaries] = useState(series.amountVaries)
  const [error, setError] = useState<string>()

  const roots = categories.filter((c) => c.type === series.type)
  const { label } = resolveCategory(roots, categoryId)
  const choices = accounts.filter(
    (a) =>
      (a.type === 'CreditCard') === onCard && (!autoDebit || a.type === 'Checking') && (a.isActive || a.id === series.accountId),
  )
  const canAutoDebit = series.type === 'Expense' && accounts.find((a) => a.id === accountId)?.type === 'Checking'
  const minNext = addDays(series.generatedThrough, 1)

  const submit = async (event: FormEvent) => {
    event.preventDefault()
    if (amountCents <= 0) return setError('Informe o valor.')
    if (endDate === '') return setError('Escolha a data do término.')
    const nextChanged = frequency !== series.frequency || nextDate !== (series.nextOccurrence ?? '')
    try {
      const updated = await update.mutateAsync({
        id: series.id,
        body: {
          accountId,
          amountCents,
          categoryId: categoryId || null,
          description: description.trim() || null,
          method: autoDebit ? 'Debit' : method,
          frequency: autoDebit ? 'Monthly' : frequency,
          nextDate: nextChanged && nextDate ? nextDate : null,
          endDate,
          autoDebit: autoDebit && canAutoDebit,
          amountVaries: autoDebit && canAutoDebit && amountVaries,
        },
      })
      toast.success('Série alterada', {
        description: updated.nextOccurrence
          ? `Vale a partir de ${formatShortDate(updated.nextOccurrence)}. O que já foi lançado fica como está.`
          : 'O que já foi lançado fica como está.',
      })
      onDone()
    } catch (e) {
      setError(messageOf(e))
    }
  }

  return (
    <>
      <header className="flex shrink-0 items-center gap-2 px-2 pt-1 pb-2">
        <Button variant="ghost" size="icon" className="rounded-full" aria-label="Voltar" onClick={onDone}>
          <ArrowLeft />
        </Button>
        <div className="flex min-w-0 flex-col">
          <SheetTitle className="truncate text-base font-medium">Editar série</SheetTitle>
          <SheetDescription className="truncate text-xs">Vale do próximo em diante</SheetDescription>
        </div>
      </header>
      <form
        id="edit-series"
        onSubmit={submit}
        noValidate
        className="flex min-h-0 flex-col gap-6 overflow-y-auto overscroll-contain px-4 pt-2 pb-6"
      >
        {error && (
          <Alert variant="destructive">
            <AlertDescription>{error}</AlertDescription>
          </Alert>
        )}
        <AmountField value={amountCents} onChange={setAmountCents} income={series.type === 'Income'} compact />
        <Section title="Descrição">
          <label className="flex items-center gap-3 rounded-2xl border bg-card py-2 pr-3 pl-2 focus-within:ring-2 focus-within:ring-ring/50">
            <EntryTile description={description} category={label} />
            <input
              value={description}
              onChange={(e) => setDescription(e.target.value)}
              maxLength={200}
              autoComplete="off"
              aria-label="Descrição"
              className="h-10 min-w-0 flex-1 bg-transparent text-base outline-none placeholder:text-muted-foreground"
            />
          </label>
        </Section>
        <Section title="Categoria">
          <CategoryPicker roots={roots} value={categoryId} onChange={setCategoryId} />
        </Section>
        {choices.length > 1 && (
          <Section title={onCard ? 'Cartão' : 'Conta'}>
            <ChipRow>
              {choices.map((a) => (
                <Chip key={a.id} selected={a.id === accountId} onClick={() => setAccountId(a.id)}>
                  <AccountTile name={a.name} type={a.type} size="sm" />
                  {a.name}
                </Chip>
              ))}
            </ChipRow>
          </Section>
        )}
        {canAutoDebit && (
          <Section title="Débito automático">
            <ChipRow>
              <Chip selected={!autoDebit} onClick={() => setAutoDebit(false)}>
                Não
              </Chip>
              <Chip
                selected={autoDebit}
                onClick={() => {
                  setAutoDebit(true)
                  setFrequency('Monthly')
                }}
              >
                É débito automático
              </Chip>
            </ChipRow>
          </Section>
        )}
        {autoDebit && canAutoDebit && (
          <Section title="Valor">
            <ChipRow>
              <Chip selected={amountVaries} onClick={() => setAmountVaries(true)}>
                Muda a cada mês
              </Chip>
              <Chip selected={!amountVaries} onClick={() => setAmountVaries(false)}>
                Sempre o mesmo
              </Chip>
            </ChipRow>
          </Section>
        )}
        {/* No débito automático, o meio é sempre débito e a frequência, todo mês. */}
        {!onCard && !autoDebit && (
          <Section title="Pagamento">
            <ChipRow>
              {simpleMethods.map((m) => (
                <Chip key={m} selected={m === method} onClick={() => setMethod(m)}>
                  {paymentMethodLabels[m]}
                </Chip>
              ))}
            </ChipRow>
          </Section>
        )}
        {!autoDebit && (
          <Section title="Frequência">
            <ChipRow>
              {frequencies.map((f) => (
                <Chip key={f.value} selected={f.value === frequency} onClick={() => setFrequency(f.value)}>
                  {f.label}
                </Chip>
              ))}
            </ChipRow>
          </Section>
        )}
        <Section
          title={autoDebit ? 'Próximo vencimento' : 'Próxima'}
          aside={
            nextDate
              ? autoDebit
                ? `vence todo dia ${Number(nextDate.slice(8, 10))}`
                : frequencyText(nextDate, frequency)
              : undefined
          }
        >
          <Input
            type="date"
            aria-label="Próxima data"
            value={nextDate}
            min={minNext}
            max="9999-12-31"
            onChange={(e) => setNextDate(e.target.value)}
            className="h-11 rounded-xl"
          />
        </Section>
        <Section title="Termina">
          <ChipRow>
            <Chip selected={endDate === null} onClick={() => setEndDate(null)}>
              Nunca
            </Chip>
            <Chip selected={endDate !== null} onClick={() => endDate === null && setEndDate('')}>
              Em uma data
            </Chip>
          </ChipRow>
          {endDate !== null && (
            <Input
              type="date"
              aria-label="Data do término"
              value={endDate}
              min={series.generatedThrough}
              max="9999-12-31"
              onChange={(e) => setEndDate(e.target.value)}
              className="h-11 rounded-xl"
            />
          )}
        </Section>
      </form>
      <SheetFooterBar>
        <Button
          type="submit"
          form="edit-series"
          size="lg"
          disabled={update.isPending}
          className="h-12 w-full rounded-2xl text-base"
        >
          {update.isPending ? 'Salvando…' : 'Salvar alterações'}
        </Button>
      </SheetFooterBar>
    </>
  )
}

function messageOf(error: unknown): string {
  return error instanceof ApiError ? error.message : 'Não foi possível conectar. Tente novamente.'
}
