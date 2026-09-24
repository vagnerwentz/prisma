import { CalendarClock, CalendarCog, Check, Lock, Undo2 } from 'lucide-react'
import { useMemo, useState, type FormEvent, type ReactNode } from 'react'
import { toast } from 'sonner'
import { BottomSheet, SheetFooterBar } from '@/components/BottomSheet'
import { AccountTile, EntryTile } from '@/components/brand/Tiles'
import { FieldError } from '@/components/FieldError'
import { Alert, AlertDescription } from '@/components/ui/alert'
import { Button } from '@/components/ui/button'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'
import { SheetDescription, SheetTitle } from '@/components/ui/sheet'
import { Skeleton } from '@/components/ui/skeleton'
import { categoryLabels, useCategories } from '@/features/categories/queries'
import { ApiError } from '@/lib/api'
import { formatLongDate, formatShortDate, todayInSaoPaulo } from '@/lib/dates'
import { formatCents } from '@/lib/money'
import { cn } from '@/lib/utils'
import { Chip, ChipRow, DateChooser, Section } from '@/features/transactions/fields'
import { useDeleteTransaction, usePayStatement, useRestoreTransaction } from '@/features/transactions/queries'
import { paymentMethodLabels, type PaymentMethod } from './labels'
import { useAccounts, useStatementTransactions, useUpdateStatement, type Statement } from './queries'
import { statementTitle, type StatementStatus } from './statements'

// Uma fatura: total, datas, as compras que entraram nela e o ajuste das datas (o banco antecipa
// ou adia o fechamento em fim de semana e feriado; docs/fase-1.md).
export function StatementSheet({
  statement,
  status,
  onClose,
}: {
  statement: Statement
  status: StatementStatus
  onClose: () => void
}) {
  const [view, setView] = useState<'details' | 'dates' | 'pay'>('details')
  const back = () => setView('details')

  return (
    <BottomSheet open onClose={onClose}>
      {view === 'dates' && <EditDates statement={statement} onDone={back} />}
      {view === 'pay' && <PayForm statement={statement} onDone={back} />}
      {view === 'details' && (
        <Details statement={statement} status={status} onEditDates={() => setView('dates')} onPay={() => setView('pay')} />
      )}
    </BottomSheet>
  )
}

function Details({
  statement,
  status,
  onEditDates,
  onPay,
}: {
  statement: Statement
  status: StatementStatus
  onEditDates: () => void
  onPay: () => void
}) {
  const transactions = useStatementTransactions(statement.id)
  const categories = useCategories()
  const labels = useMemo(() => categoryLabels(categories.data ?? []), [categories.data])
  // A entrada do pagamento fica ligada à fatura, mas não é compra (docs/fase-1.md, 2.3).
  const payment = transactions.data?.find((t) => t.type === 'Transfer')
  const items = (transactions.data ?? [])
    .filter((t) => t.type !== 'Transfer')
    .sort((a, b) => b.purchaseDate.localeCompare(a.purchaseDate))
  const undo = useUndoPayment()
  const canPay = status === 'Fechada' && statement.totalCents > 0

  return (
    <>
      <div className="flex min-h-0 flex-col overflow-y-auto overscroll-contain">
        <header className="flex flex-col items-center gap-2 px-6 pt-3 pb-5 text-center">
          <StatusBadge status={status} />
          <SheetTitle className="font-display text-3xl leading-tight font-normal">
            {statementTitle(statement.reference)}
          </SheetTitle>
          <span className="font-display text-5xl leading-none tabular-nums">{formatCents(statement.totalCents)}</span>
        </header>
        <div className="spectrum-line mx-6 opacity-70" />

        <dl className="mx-4 my-5 flex flex-col divide-y rounded-2xl border bg-card text-sm">
          <Row label="Fechamento">{formatLongDate(statement.closingDate)}</Row>
          <Row label="Vencimento">{formatLongDate(statement.dueDate)}</Row>
          {statement.datesEditedManually && <Row label="Datas">ajustadas à mão</Row>}
          {payment && <Row label="Paga em">{formatLongDate(payment.purchaseDate)}</Row>}
        </dl>

        <section className="mx-4 mb-5 flex flex-col gap-2">
          <h3 className="px-1 text-xs font-medium tracking-wide text-muted-foreground uppercase">
            Compras {transactions.isSuccess && `(${items.length})`}
          </h3>
          {transactions.isPending && <Skeleton className="h-24 w-full rounded-2xl" />}
          {transactions.isError && (
            <Alert variant="destructive">
              <AlertDescription>Não foi possível carregar as compras da fatura.</AlertDescription>
            </Alert>
          )}
          {transactions.isSuccess && items.length === 0 && (
            <p className="rounded-2xl border border-dashed px-4 py-5 text-center text-sm text-muted-foreground">
              Nenhuma compra nesta fatura.
            </p>
          )}
          {items.length > 0 && (
            <ul className="flex flex-col divide-y rounded-2xl border bg-card">
              {items.map((t) => {
                const category = t.categoryId ? labels.get(t.categoryId) : undefined
                const title = t.description || category?.name || 'Sem descrição'
                return (
                  <li key={t.id} className="flex items-center gap-3 px-4 py-2.5">
                    <EntryTile description={t.description} category={category} size="sm" />
                    <div className="min-w-0 flex-1">
                      <p className="truncate text-sm font-medium">{title}</p>
                      <p className="text-xs text-muted-foreground tabular-nums">
                        {formatShortDate(t.purchaseDate)}
                        {t.installmentNumber && ` · parcela ${t.installmentNumber}`}
                      </p>
                    </div>
                    <span className="text-sm tabular-nums">{formatCents(t.amountCents)}</span>
                  </li>
                )
              })}
            </ul>
          )}
        </section>
      </div>

      <SheetFooterBar className={cn(canPay && 'grid grid-cols-[1fr_auto] gap-2')}>
        {statement.isPaid ? (
          <Button
            size="lg"
            variant="outline"
            className="h-12 w-full rounded-2xl text-base"
            disabled={!payment || undo.isPending}
            onClick={() => payment && undo.run(payment.id, statementTitle(statement.reference))}
          >
            <Undo2 />
            Desfazer pagamento
          </Button>
        ) : canPay ? (
          <>
            <Button size="lg" className="h-12 rounded-2xl text-base" onClick={onPay}>
              <Check />
              Pagar {formatCents(statement.totalCents)}
            </Button>
            <Button size="lg" variant="outline" className="h-12 rounded-2xl" aria-label="Ajustar datas" onClick={onEditDates}>
              <CalendarCog />
            </Button>
          </>
        ) : (
          <Button size="lg" variant="outline" className="h-12 w-full rounded-2xl text-base" onClick={onEditDates}>
            <CalendarCog />
            Ajustar datas
          </Button>
        )}
      </SheetFooterBar>
    </>
  )
}

function Row({ label, children }: { label: string; children: ReactNode }) {
  return (
    <div className="flex items-center justify-between gap-4 px-4 py-3">
      <dt className="text-muted-foreground">{label}</dt>
      <dd className="text-right font-medium">{children}</dd>
    </div>
  )
}

// PATCH /statements/{id}: a API valida (vencimento não antes do fechamento) e recalcula o
// vencimento das compras desta fatura.
function EditDates({ statement, onDone }: { statement: Statement; onDone: () => void }) {
  const update = useUpdateStatement()
  const [closingDate, setClosingDate] = useState(statement.closingDate)
  const [dueDate, setDueDate] = useState(statement.dueDate)
  const [error, setError] = useState<string>()
  const invalid = closingDate && dueDate && dueDate < closingDate ? 'O vencimento não pode ser antes do fechamento.' : undefined

  const submit = async (event: FormEvent) => {
    event.preventDefault()
    if (!closingDate || !dueDate || invalid) return
    try {
      await update.mutateAsync({ id: statement.id, body: { closingDate, dueDate } })
      toast.success('Datas da fatura ajustadas', { description: `Vence em ${formatShortDate(dueDate)}.` })
      onDone()
    } catch (e) {
      setError(messageOf(e))
    }
  }

  return (
    <form onSubmit={submit} noValidate className="flex min-h-0 flex-col">
      <header className="px-4 pt-2 pb-3">
        <SheetTitle className="font-display text-2xl font-normal">Ajustar datas</SheetTitle>
        <SheetDescription>Fatura de {statementTitle(statement.reference).toLowerCase()}</SheetDescription>
      </header>
      <div className="flex min-h-0 flex-col gap-4 overflow-y-auto px-4 pb-6">
        {error && (
          <Alert variant="destructive">
            <AlertDescription>{error}</AlertDescription>
          </Alert>
        )}
        <div className="grid grid-cols-2 gap-3">
          <div className="flex flex-col gap-2">
            <Label htmlFor="closing-date">Fechamento</Label>
            <Input
              id="closing-date"
              type="date"
              max="9999-12-31"
              value={closingDate}
              onChange={(e) => setClosingDate(e.target.value)}
              className="h-11 rounded-xl"
            />
          </div>
          <div className="flex flex-col gap-2">
            <Label htmlFor="due-date">Vencimento</Label>
            <Input
              id="due-date"
              type="date"
              max="9999-12-31"
              value={dueDate}
              onChange={(e) => setDueDate(e.target.value)}
              className="h-11 rounded-xl"
            />
          </div>
        </div>
        <FieldError message={invalid} />
        <p className="rounded-2xl bg-muted/60 px-4 py-3 text-xs leading-relaxed text-muted-foreground">
          Use quando o banco antecipa ou adia o fechamento (fim de semana, feriado). As compras desta fatura passam a vencer na
          nova data, e as próximas compras respeitam o novo fechamento.
        </p>
      </div>
      <SheetFooterBar className="grid grid-cols-[auto_1fr] gap-2">
        <Button type="button" size="lg" variant="outline" className="h-12 rounded-2xl" onClick={onDone}>
          Voltar
        </Button>
        <Button type="submit" size="lg" disabled={update.isPending || !!invalid} className="h-12 rounded-2xl text-base">
          {update.isPending ? 'Salvando…' : 'Salvar datas'}
        </Button>
      </SheetFooterBar>
    </form>
  )
}

// Desfazer o pagamento é excluir a transferência; o aviso oferece refazer (restaurar).
function useUndoPayment() {
  const remove = useDeleteTransaction()
  const restore = useRestoreTransaction()
  const run = async (legId: string, title: string) => {
    try {
      await remove.mutateAsync(legId)
    } catch (error) {
      toast.error(messageOf(error))
      return
    }
    toast('Pagamento desfeito', {
      description: `Fatura de ${title.toLowerCase()} em aberto de novo.`,
      duration: 8000,
      action: {
        label: 'Desfazer',
        onClick: () =>
          restore
            .mutateAsync(legId)
            .then(() => toast.success('Fatura paga de novo'))
            .catch((error: unknown) => toast.error(messageOf(error))),
      },
    })
  }
  return { run, isPending: remove.isPending }
}

const paymentMethods: PaymentMethod[] = ['Pix', 'Boleto', 'Debit', 'Ted']

// Paga o total da fatura a partir de uma conta (docs/fase-1.md, 2.3): sempre o total, depois do
// fechamento. O gasto já está nas compras; o pagamento é transferência.
function PayForm({ statement, onDone }: { statement: Statement; onDone: () => void }) {
  const accounts = useAccounts()
  const pay = usePayStatement()
  const sources = (accounts.data ?? []).filter((a) => a.type !== 'CreditCard' && a.isActive)
  const [fromId, setFromId] = useState<string>()
  const from = fromId ?? sources.find((a) => a.type === 'Checking')?.id ?? sources[0]?.id
  const [date, setDate] = useState(todayInSaoPaulo())
  const [method, setMethod] = useState<PaymentMethod>('Pix')
  const [error, setError] = useState<string>()

  const submit = async (event: FormEvent) => {
    event.preventDefault()
    if (!from) return
    try {
      await pay.mutateAsync({ id: statement.id, body: { fromAccountId: from, date, method, description: null } })
      toast.success('Fatura paga', {
        description: `${statementTitle(statement.reference)} · ${formatCents(statement.totalCents)}`,
      })
      onDone()
    } catch (e) {
      setError(messageOf(e))
    }
  }

  return (
    <form onSubmit={submit} noValidate className="flex min-h-0 flex-col">
      <header className="flex flex-col items-center gap-1 px-4 pt-2 pb-4 text-center">
        <SheetTitle className="text-sm font-medium text-muted-foreground">
          Pagar fatura de {statementTitle(statement.reference).toLowerCase()}
        </SheetTitle>
        <span className="font-display text-5xl leading-none tabular-nums">{formatCents(statement.totalCents)}</span>
        <SheetDescription className="text-xs">
          Sempre o total. Não conta como despesa: o gasto já está nas compras.
        </SheetDescription>
      </header>
      <div className="flex min-h-0 flex-col gap-6 overflow-y-auto px-4 pb-6">
        {error && (
          <Alert variant="destructive">
            <AlertDescription>{error}</AlertDescription>
          </Alert>
        )}
        {sources.length === 0 ? (
          <p className="text-sm text-muted-foreground">Crie uma conta corrente para pagar a fatura.</p>
        ) : (
          <Section title="Pagar com">
            <ChipRow>
              {sources.map((a) => (
                <Chip key={a.id} selected={a.id === from} onClick={() => setFromId(a.id)}>
                  <AccountTile name={a.name} type={a.type} size="sm" />
                  {a.name}
                </Chip>
              ))}
            </ChipRow>
          </Section>
        )}
        <Section title="Data do pagamento">
          <DateChooser value={date} onChange={setDate} />
        </Section>
        <Section title="Meio">
          <ChipRow>
            {paymentMethods.map((m) => (
              <Chip key={m} selected={m === method} onClick={() => setMethod(m)}>
                {paymentMethodLabels[m]}
              </Chip>
            ))}
          </ChipRow>
        </Section>
      </div>
      <SheetFooterBar className="grid grid-cols-[auto_1fr] gap-2">
        <Button type="button" size="lg" variant="outline" className="h-12 rounded-2xl" onClick={onDone}>
          Voltar
        </Button>
        <Button type="submit" size="lg" disabled={pay.isPending || !from} className="h-12 rounded-2xl text-base">
          {pay.isPending ? 'Pagando…' : `Pagar ${formatCents(statement.totalCents)}`}
        </Button>
      </SheetFooterBar>
    </form>
  )
}

function messageOf(error: unknown): string {
  return error instanceof ApiError ? error.message : 'Não foi possível conectar. Tente novamente.'
}

// Sem verde/vermelho (CLAUDE.md, 7.1): aberta ganha o espectro; o resto é neutro.
const statusIcons = { Fechada: Lock, Paga: Check, Futura: CalendarClock } as const

export function StatusBadge({ status }: { status: StatementStatus }) {
  const Icon = status === 'Aberta' ? null : statusIcons[status]
  return (
    <span
      data-status={status}
      className="status-badge inline-flex items-center gap-1 rounded-full px-2.5 py-1 text-[0.68rem] leading-none font-semibold tracking-wider uppercase [&_svg]:size-3 [&_svg]:stroke-[2.5]"
    >
      {Icon ? (
        <Icon />
      ) : (
        <span className="relative flex size-1.5">
          <span className="absolute inline-flex size-full animate-ping rounded-full bg-white/80" />
          <span className="relative inline-flex size-1.5 rounded-full bg-white" />
        </span>
      )}
      {status}
    </span>
  )
}
