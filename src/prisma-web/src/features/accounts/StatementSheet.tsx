import { CalendarCog } from 'lucide-react'
import { useMemo, useState, type FormEvent, type ReactNode } from 'react'
import { toast } from 'sonner'
import { BottomSheet, SheetFooterBar } from '@/components/BottomSheet'
import { EntryTile } from '@/components/brand/Tiles'
import { FieldError } from '@/components/FieldError'
import { Alert, AlertDescription } from '@/components/ui/alert'
import { Button } from '@/components/ui/button'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'
import { SheetDescription, SheetTitle } from '@/components/ui/sheet'
import { Skeleton } from '@/components/ui/skeleton'
import { categoryLabels, useCategories } from '@/features/categories/queries'
import { ApiError } from '@/lib/api'
import { formatLongDate, formatShortDate } from '@/lib/dates'
import { formatCents } from '@/lib/money'
import { cn } from '@/lib/utils'
import { useStatementTransactions, useUpdateStatement, type Statement } from './queries'
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
  const [editing, setEditing] = useState(false)

  return (
    <BottomSheet open onClose={onClose}>
      {editing ? (
        <EditDates statement={statement} onDone={() => setEditing(false)} />
      ) : (
        <Details statement={statement} status={status} onEdit={() => setEditing(true)} />
      )}
    </BottomSheet>
  )
}

function Details({ statement, status, onEdit }: { statement: Statement; status: StatementStatus; onEdit: () => void }) {
  const transactions = useStatementTransactions(statement.id)
  const categories = useCategories()
  const labels = useMemo(() => categoryLabels(categories.data ?? []), [categories.data])
  const items = [...(transactions.data ?? [])].sort((a, b) => b.purchaseDate.localeCompare(a.purchaseDate))

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

      <SheetFooterBar>
        <Button
          size="lg"
          variant="outline"
          className="h-12 w-full rounded-2xl text-base"
          disabled={statement.isPaid}
          onClick={onEdit}
        >
          <CalendarCog />
          Ajustar datas
        </Button>
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
      setError(e instanceof ApiError ? e.message : 'Não foi possível conectar. Tente novamente.')
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

// Sem verde/vermelho (CLAUDE.md, 7.1): aberta ganha o espectro; o resto é neutro.
export function StatusBadge({ status }: { status: StatementStatus }) {
  return (
    <span
      className={cn(
        'rounded-full border px-2 py-0.5 text-[0.65rem] font-medium tracking-wide uppercase',
        status === 'Aberta' ? 'spectrum-ring' : 'text-muted-foreground',
      )}
    >
      {status}
    </span>
  )
}
