import { zodResolver } from '@hookform/resolvers/zod'
import { ArrowLeft, ChevronRight, CopyPlus, Pencil, Repeat, Trash2, Undo2 } from 'lucide-react'
import { useEffect, useState, type FormEvent, type ReactNode } from 'react'
import { useForm, useWatch, type Path, type PathValue, type UseFormRegisterReturn } from 'react-hook-form'
import { useNavigate } from 'react-router'
import { toast } from 'sonner'
import { z } from 'zod'
import { AccountTile, EntryTile, TransferTile } from '@/components/brand/Tiles'
import { FieldError } from '@/components/FieldError'
import { Alert, AlertDescription } from '@/components/ui/alert'
import { Button } from '@/components/ui/button'
import { BottomSheet, SheetFooterBar } from '@/components/BottomSheet'
import { SheetDescription, SheetTitle } from '@/components/ui/sheet'
import { paymentMethodLabels, type PaymentMethod } from '@/features/accounts/labels'
import { useStatements, type Account } from '@/features/accounts/queries'
import { resolveCategory, type CategoryLabel, type CategoryNode } from '@/features/categories/queries'
import { RecurrenceChooser } from '@/features/recurrences/RecurrenceChooser'
import { autoDebitLine } from '@/features/recurrences/autoDebit'
import { canAutoDebit, missingEndMessage, repeatRequest, type RepeatChoice } from '@/features/recurrences/repeatChoice'
import {
  useEndRecurrence,
  useRecurrences,
  useStartRecurrence,
  useUpdateRecurrence,
  type Recurrence,
} from '@/features/recurrences/queries'
import { removalTexts, reopenRequest } from '@/features/recurrences/removal'
import { ConfirmCard } from '@/features/recurrences/ToConfirmPanel'
import { describeSeries, frequencyText, seriesLine, shortDate } from '@/features/recurrences/schedule'
import { ApiError } from '@/lib/api'
import { formatLongDate, formatShortDate, monthOf, todayInSaoPaulo, type YearMonth } from '@/lib/dates'
import { describeInstallments, formatCents } from '@/lib/money'
import { cn } from '@/lib/utils'
import { Amount } from './Amount'
import { editKind, entryKey, type EditKind } from './editing'
import { AmountField, CategoryPicker, Chip, ChipRow, DateChooser, Section, TypeToggle } from './fields'
import {
  useDeletePurchase,
  useDeleteTransaction,
  useMoveStatement,
  useRestorePurchase,
  useRestoreTransaction,
  useUpdatePurchase,
  useTransaction,
  useUpdateTransaction,
  type Transaction,
} from './queries'
import { cardChangeText, cardChoices } from './cardChange'
import { repeatDraftOf } from './repeat'
import { moveOptions, moveTexts, shiftReference, type StatementShift } from './statementMove'
import type { TimelineEntry } from './timeline'

type AnyEntry = TimelineEntry<Transaction>
type TransferEntry = Extract<AnyEntry, { kind: 'transfer' }>
// Lançamentos editáveis; transferência tem painel próprio, só com "Excluir".
type Entry = Exclude<AnyEntry, TransferEntry>

export type EntrySheetProps = {
  entry: AnyEntry | undefined
  onClose: () => void
  // Editar a data pode levar o lançamento para outro mês: a lista vai junto.
  onMoved: (month: YearMonth) => void
  accounts: Account[]
  categories: CategoryNode[]
  labels: Map<string, CategoryLabel>
}

type Mode = { view: 'details' } | { view: 'edit' } | { view: 'installment'; id: string } | { view: 'repeat' } | { view: 'remove' }

const maxInstallments = 24

// Fora do cartão não existe "Crédito" (a API recusa).
const simpleMethods: PaymentMethod[] = ['Pix', 'Debit', 'Cash', 'Boleto', 'Ted']

// Painel de um lançamento: detalhes, "Editar" e "Excluir" (com "Desfazer" no aviso).
export default function EntrySheet({ entry, onClose, ...rest }: EntrySheetProps) {
  const [mode, setMode] = useState<Mode>({ view: 'details' })

  // Outro lançamento aberto começa pelos detalhes.
  const key = entry && entryKey(entry)
  const [shownKey, setShownKey] = useState(key)
  if (key !== shownKey) {
    setShownKey(key)
    setMode({ view: 'details' })
  }

  return (
    <BottomSheet open={entry !== undefined} onClose={onClose}>
      {entry?.kind === 'transfer' && <TransferDetails entry={entry} accounts={rest.accounts} onClose={onClose} />}
      {entry && entry.kind !== 'transfer' && mode.view === 'details' && (
        <Details entry={entry} onClose={onClose} onEdit={(next) => setMode(next)} {...rest} />
      )}
      {entry?.kind === 'single' && mode.view === 'remove' && (
        <RemoveFromSeries transaction={entry.transaction} onBack={() => setMode({ view: 'details' })} onClose={onClose} />
      )}
      {entry && entry.kind !== 'transfer' && mode.view !== 'details' && mode.view !== 'remove' && (
        <Editor entry={entry} mode={mode} onDone={() => setMode({ view: 'details' })} onClose={onClose} {...rest} />
      )}
    </BottomSheet>
  )
}

type Common = Omit<EntrySheetProps, 'entry' | 'onClose'> & { entry: Entry; onClose: () => void }

function Details({ entry, accounts, labels, onClose, onEdit }: Common & { onEdit: (mode: Mode) => void }) {
  const installments = entry.kind === 'purchase' ? [...entry.installments].sort(byNumber) : []
  const first = entry.kind === 'single' ? entry.transaction : installments[0]
  const kind = editKind(entry)
  const category = first.categoryId ? labels.get(first.categoryId) : undefined
  const account = accounts.find((a) => a.id === first.accountId)
  const amount = entry.kind === 'single' ? first.amountCents : entry.totalCents
  const isRefund = first.type === 'Refund'
  const title = first.description || category?.name || (isRefund ? 'Estorno' : 'Sem descrição')
  // Na compra parcelada, cada parcela traz o estornado da compra inteira (docs/fase-2.md, 2.5).
  const refunded = first.refundedCents ?? 0
  const refundable = first.type === 'Expense' ? (first.refundableCents ?? 0) : 0
  const canRefund = first.type === 'Expense' && refundable > 0
  // "Lançar de novo" (docs/fase-1.md, 5.1): despesa ou receita, com o que o painel já mostra.
  const repeat = repeatDraftOf(entry)
  // "Se repete" (docs/fase-2.md, 2.14): despesa ou receita avulsa que ainda não é de uma série. Compra
  // parcelada, estorno e transferência não se repetem.
  const canStartSeries = entry.kind === 'single' && (first.type === 'Expense' || first.type === 'Income') && !first.recurrenceId
  const today = todayInSaoPaulo()
  // Série ainda ativa: excluir pergunta antes se ela também acaba (2.14, Tela).
  const series = useRecurrences(!!first.recurrenceId).data?.find((r) => r.id === first.recurrenceId)
  const inActiveSeries = entry.kind === 'single' && !!series && !series.isEnded
  const navigate = useNavigate()
  const remove = useRemove(
    entry.kind === 'purchase' ? { kind: 'purchase', id: entry.purchaseId } : { kind: 'single', id: first.id },
    `${title} · ${formatCents(amount)}`,
  )

  return (
    <>
      <div className="flex min-h-0 flex-col overflow-y-auto overscroll-contain">
        <header className="flex flex-col items-center gap-3 px-6 pt-4 pb-5 text-center">
          <EntryTile description={first.description} category={category} size="xl" />
          <div className="flex flex-col gap-0.5">
            <SheetTitle className="font-display text-2xl leading-tight font-normal">{title}</SheetTitle>
            {category && category.name !== title && (
              <p className="text-sm text-muted-foreground">
                {category.parentName ? `${category.parentName} › ${category.name}` : category.name}
              </p>
            )}
          </div>
          <Amount type={first.type} cents={amount} className="font-display text-5xl leading-none font-normal" />
          {isRefund && <p className="text-sm text-muted-foreground">Estorno · abate a despesa do mês em que cai</p>}
          {first.amountEstimated && <p className="text-sm text-muted-foreground">Valor médio · a conferir</p>}
          {installments.length > 1 && (
            <p className="text-sm text-muted-foreground tabular-nums">{describeInstallments(amount, installments.length)}</p>
          )}
        </header>

        <div className="spectrum-line mx-6 opacity-70" />

        <dl className="surface mx-4 my-5 flex flex-col divide-y divide-border/60 rounded-2xl text-sm">
          <InfoRow label={isRefund ? 'Data do estorno' : series?.kind === 'AutoDebit' ? 'Data do débito' : 'Data da compra'}>
            {formatLongDate(first.purchaseDate)}
          </InfoRow>
          {kind === 'card' && <InfoRow label="Fatura que vence em">{formatLongDate(first.settlementDate)}</InfoRow>}
          {account && (
            <InfoRow label="Conta">
              <span className="flex items-center gap-2">
                <AccountTile name={account.name} type={account.type} size="sm" />
                {account.name}
              </span>
            </InfoRow>
          )}
          <InfoRow label="Pagamento">{paymentMethodLabels[first.method]}</InfoRow>
          {first.recurrenceId && (
            <InfoRow label="Se repete">
              <SeriesInfo id={first.recurrenceId} today={today} />
            </InfoRow>
          )}
          {isRefund && first.refundedTransactionId && (
            <InfoRow label="Estorno de">
              <RefundedPurchase id={first.refundedTransactionId} />
            </InfoRow>
          )}
          {first.type === 'Expense' && refunded > 0 && (
            <InfoRow label="Estornado">
              <span className="tabular-nums">
                {formatCents(refunded)} de {formatCents(refunded + refundable)}
              </span>
            </InfoRow>
          )}
        </dl>

        {/* Débito automático com o valor médio (docs/fase-2.md, 2.15, regra 6): conferir antes das outras ações. */}
        {entry.kind === 'single' && first.amountEstimated && <ConfirmCard transaction={first} className="mx-4 mb-5" />}

        {(repeat || canRefund || canStartSeries) && (
          // Ações secundárias, cada uma na sua faixa do espectro; lado a lado quando cabem.
          <div className="mx-4 mb-5 flex flex-wrap gap-2">
            {repeat && (
              <Button
                data-tint="repeat"
                className="tinted-action h-11 flex-1 rounded-2xl"
                onClick={() => {
                  onClose()
                  navigate('/lancar', { state: { repeat } })
                }}
              >
                <CopyPlus />
                Lançar de novo
              </Button>
            )}
            {canRefund && (
              <Button
                data-tint="refund"
                className="tinted-action h-11 flex-1 rounded-2xl"
                onClick={() => {
                  onClose()
                  navigate(`/lancar?estorno=${first.id}`)
                }}
              >
                <Undo2 />
                {refunded > 0 ? `Estornar mais (restam ${formatCents(refundable)})` : 'Estornar'}
              </Button>
            )}
            {canStartSeries && (
              <Button
                data-tint="series"
                className="tinted-action h-11 flex-1 rounded-2xl"
                onClick={() => onEdit({ view: 'repeat' })}
              >
                <Repeat />
                Se repete todo mês…
              </Button>
            )}
          </div>
        )}
        {first.type === 'Expense' && refunded > 0 && refundable === 0 && (
          <p className="mx-4 mb-5 text-center text-xs text-muted-foreground">Compra estornada por inteiro.</p>
        )}

        {(kind === 'card' || kind === 'purchase') && (
          <StatementMoveActions transactions={entry.kind === 'single' ? [first] : installments} />
        )}

        {installments.length > 0 && (
          <InstallmentList installments={installments} labels={labels} onPick={(id) => onEdit({ view: 'installment', id })} />
        )}
      </div>

      <SheetFooterBar className="grid grid-cols-[1fr_auto] gap-2">
        <Button size="lg" className="h-12 rounded-2xl text-base" onClick={() => onEdit({ view: 'edit' })}>
          <Pencil />
          {kind === 'purchase' ? 'Editar compra' : 'Editar'}
        </Button>
        <Button
          size="lg"
          variant="outline"
          className="h-12 rounded-2xl px-5 text-base"
          disabled={remove.isPending}
          onClick={() => (inActiveSeries ? onEdit({ view: 'remove' }) : remove.run(onClose))}
        >
          <Trash2 />
          Excluir
        </Button>
      </SheetFooterBar>
    </>
  )
}

// Excluir o lançamento de uma série ativa (docs/fase-2.md, 2.14, Tela): excluir sozinho não para a série, e
// quem exclui pode achar que parou tudo. Exclui primeiro e só então encerra: exclusão recusada (fatura paga)
// deixa a série como estava. O "Desfazer" volta o lançamento e reabre a série com o término de antes.
function RemoveFromSeries({
  transaction,
  onBack,
  onClose,
}: {
  transaction: Transaction
  onBack: () => void
  onClose: () => void
}) {
  const series = useRecurrences().data?.find((r) => r.id === transaction.recurrenceId)
  const deleteTransaction = useDeleteTransaction()
  const restoreTransaction = useRestoreTransaction()
  const endRecurrence = useEndRecurrence()
  const updateRecurrence = useUpdateRecurrence()
  const today = todayInSaoPaulo()
  const summary = `${transaction.description || 'Lançamento'} · ${formatCents(transaction.amountCents)}`
  const busy = deleteTransaction.isPending || endRecurrence.isPending

  const run = async (before: Recurrence, endSeries: boolean) => {
    try {
      await deleteTransaction.mutateAsync(transaction.id)
    } catch (error) {
      toast.error(messageOf(error))
      return
    }
    if (endSeries) {
      try {
        await endRecurrence.mutateAsync(before.id)
      } catch (error) {
        onClose()
        toast.error(`Lançamento excluído, mas a série continua. ${messageOf(error)}`)
        return
      }
    }
    onClose()
    const texts = removalTexts({ series: before, endSeries, summary, today })
    toast(texts.title, {
      description: texts.description,
      duration: 8000,
      action: {
        label: 'Desfazer',
        onClick: () => {
          restoreTransaction
            .mutateAsync(transaction.id)
            .then(() => (endSeries ? updateRecurrence.mutateAsync({ id: before.id, body: reopenRequest(before) }) : undefined))
            .then(() => toast.success(texts.undone))
            .catch((error: unknown) => toast.error(messageOf(error)))
        },
      },
    })
  }

  return (
    <>
      <header className="flex shrink-0 items-center gap-2 px-2 pt-1 pb-2">
        <Button variant="ghost" size="icon" className="rounded-full" aria-label="Voltar" onClick={onBack}>
          <ArrowLeft />
        </Button>
        <SheetTitle className="text-base font-medium">Excluir lançamento</SheetTitle>
      </header>
      <div className="flex flex-col gap-5 px-4 pt-2 pb-2">
        <div className="surface flex flex-col gap-1 rounded-2xl px-4 py-3 text-sm">
          <p className="font-medium">{summary}</p>
          <p className="text-muted-foreground">
            {series
              ? `Faz parte de uma série: ${frequencyText(series.startDate, series.frequency).toLowerCase()}.`
              : 'Faz parte de uma série.'}
          </p>
        </div>
        <p className="px-1 text-sm text-muted-foreground">
          Excluir só este não para a série: os próximos continuam a ser lançados.
        </p>
      </div>
      <SheetFooterBar className="flex flex-col gap-2">
        <Button
          size="lg"
          className="h-12 w-full rounded-2xl text-base"
          disabled={!series || busy}
          onClick={() => series && run(series, true)}
        >
          Excluir e encerrar a série
        </Button>
        <Button
          size="lg"
          variant="outline"
          className="h-12 w-full rounded-2xl text-base"
          disabled={!series || busy}
          onClick={() => series && run(series, false)}
        >
          Excluir só este
        </Button>
      </SheetFooterBar>
    </>
  )
}

// Mudar a compra no cartão de fatura (docs/fase-2.md, 2.9): o banco pode lançar a compra feita perto
// do fechamento na fatura seguinte. Só aparece o que a API aceitaria (statementMove.ts); o aviso traz
// "Desfazer", que a devolve e a solta (regra 3).
function StatementMoveActions({ transactions }: { transactions: Transaction[] }) {
  const statements = useStatements(transactions[0].accountId, true)
  const move = useMoveStatement()
  const options = moveOptions(transactions, statements.data)
  if (!options || (options.shifts.length === 0 && !options.pinned)) return null

  const run = async (direction: StatementShift) => {
    const id = transactions[0].id
    const destination = shiftReference(options.current.reference, direction === 'Next' ? 1 : -1)
    let moved: Transaction[]
    try {
      moved = await move.mutateAsync({ id, direction })
    } catch (error) {
      toast.error(messageOf(error))
      return
    }
    const text = moveTexts.moved({ reference: destination, dueDate: moved[0].settlementDate }, moved.length)
    toast(text.title, {
      description: text.description,
      duration: 8000,
      action: {
        label: 'Desfazer',
        onClick: () => {
          move
            .mutateAsync({ id, direction: direction === 'Next' ? 'Previous' : 'Next' })
            .then(() => toast.success(moveTexts.undone))
            .catch((error: unknown) => toast.error(messageOf(error)))
        },
      },
    })
  }

  return (
    <section className="mx-4 mb-5 flex flex-col gap-2">
      <h3 className="px-1 text-sm font-medium text-foreground/75">{moveTexts.heading(options.current.reference)}</h3>
      {options.shifts.length > 0 && (
        // Lado a lado quando cabem; em telas estreitas (320px) um embaixo do outro, sem cortar o texto.
        <div className="flex flex-wrap gap-2">
          {options.shifts.map((shift) => (
            <Button
              key={shift}
              variant="outline"
              className="h-11 min-w-fit flex-[1_1_0%] rounded-2xl"
              disabled={move.isPending}
              onClick={() => run(shift)}
            >
              {moveTexts.button(shiftReference(options.current.reference, shift === 'Next' ? 1 : -1))}
            </Button>
          ))}
        </div>
      )}
      {options.pinned && <p className="px-1 text-xs text-muted-foreground">{moveTexts.pinned}</p>}
    </section>
  )
}

// A série do lançamento: "Todo mês, no dia 25 · próxima 25/10", ou "Encerrada".
function SeriesInfo({ id, today }: { id: string; today: string }) {
  const series = useRecurrences().data?.find((r) => r.id === id)
  if (!series) return <span className="text-muted-foreground">…</span>
  if (series.isEnded) return <span>Encerrada</span>
  // Débito automático (2.15): o dia do vencimento e a data do próximo débito, já no dia útil.
  const autoDebit = series.kind === 'AutoDebit'
  const next = autoDebit ? series.nextTransactionDate : series.nextOccurrence
  return (
    <span className="flex flex-col items-end tabular-nums">
      {autoDebit
        ? `Débito automático, vence dia ${Number(series.startDate.slice(8, 10))}`
        : frequencyText(series.startDate, series.frequency)}
      {next && (
        <span className="text-xs font-normal text-muted-foreground">
          {autoDebit ? 'próximo débito' : 'próxima'} {shortDate(next, today)}
        </span>
      )}
    </span>
  )
}

// A compra que o estorno devolve; se ela foi excluída, o estorno continua valendo (regra 15).
function RefundedPurchase({ id }: { id: string }) {
  const purchase = useTransaction(id)
  if (purchase.isPending) return <span className="text-muted-foreground">…</span>
  if (purchase.isError) return <span className="text-muted-foreground">compra excluída</span>
  return (
    <span className="tabular-nums">
      {purchase.data.description || 'Compra'} · {formatCents(purchase.data.amountCents)}
    </span>
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

const byNumber = (a: Transaction, b: Transaction) => (a.installmentNumber ?? 0) - (b.installmentNumber ?? 0)

// Parcelas da compra, com o vencimento de cada fatura. A próxima a vencer ganha o anel do
// espectro; tocar numa parcela edita só ela (descrição e categoria).
function InstallmentList({
  installments,
  labels,
  onPick,
}: {
  installments: Transaction[]
  labels: Map<string, CategoryLabel>
  onPick: (id: string) => void
}) {
  const today = todayInSaoPaulo()
  const next = installments.find((t) => t.settlementDate >= today)
  const count = installments.length

  return (
    <section className="mx-4 mb-5 flex flex-col gap-2">
      <h3 className="px-1 text-sm font-medium text-foreground/75">Parcelas</h3>
      <ol className="surface flex flex-col divide-y divide-border/60 rounded-2xl">
        {installments.map((t) => {
          const isNext = t === next
          const past = t.settlementDate < today
          const differs = t.description !== installments[0].description || t.categoryId !== installments[0].categoryId
          const note = differs ? t.description || (t.categoryId && labels.get(t.categoryId)?.name) || null : null
          return (
            <li key={t.id}>
              <button
                type="button"
                onClick={() => onPick(t.id)}
                className="flex w-full items-center gap-3 px-4 py-2.5 text-left transition-colors outline-none hover:bg-muted/50 focus-visible:bg-muted active:bg-muted"
              >
                <span
                  className={cn(
                    'flex h-7 min-w-11 items-center justify-center rounded-full border px-2 text-xs font-medium tabular-nums',
                    isNext ? 'spectrum-ring' : past && 'text-muted-foreground',
                  )}
                >
                  {t.installmentNumber}/{count}
                </span>
                <span className="min-w-0 flex-1">
                  <span className={cn('block text-sm', past && 'text-muted-foreground')}>
                    vence {formatShortDate(t.settlementDate)}
                  </span>
                  {(isNext || note) && (
                    <span className="block truncate text-xs text-muted-foreground">
                      {[isNext ? 'Próxima' : null, note].filter(Boolean).join(' · ')}
                    </span>
                  )}
                </span>
                <span className={cn('text-sm tabular-nums', past && 'text-muted-foreground')}>{formatCents(t.amountCents)}</span>
                <ChevronRight className="size-4 text-muted-foreground" />
              </button>
            </li>
          )
        })}
      </ol>
    </section>
  )
}

// Exclui e oferece "Desfazer" no aviso (soft delete + restauração). Usa mutateAsync: o aviso
// sobrevive ao painel fechado, e as opções do useMutation (recarregar a lista) valem mesmo assim.
// Transferência: excluir uma ponta leva as duas, e desfaz o pagamento se for um (docs/fase-1.md, 2.3).
type RemoveTarget = { kind: 'single' | 'purchase' | 'transfer' | 'payment'; id: string }

const removeTexts: Record<RemoveTarget['kind'], { removed: string; restored: string }> = {
  single: { removed: 'Lançamento excluído', restored: 'Lançamento restaurado' },
  purchase: { removed: 'Compra excluída', restored: 'Compra restaurada' },
  transfer: { removed: 'Transferência excluída', restored: 'Transferência restaurada' },
  payment: { removed: 'Pagamento desfeito', restored: 'Fatura paga de novo' },
}

function useRemove(target: RemoveTarget, description: string) {
  const deleteTransaction = useDeleteTransaction()
  const deletePurchase = useDeletePurchase()
  const restoreTransaction = useRestoreTransaction()
  const restorePurchase = useRestorePurchase()
  const isPurchase = target.kind === 'purchase'
  const texts = removeTexts[target.kind]

  const run = async (onClose: () => void) => {
    try {
      await (isPurchase ? deletePurchase : deleteTransaction).mutateAsync(target.id)
    } catch (error) {
      toast.error(messageOf(error))
      return
    }
    onClose()
    toast(texts.removed, {
      description,
      duration: 8000,
      action: {
        label: 'Desfazer',
        onClick: () => {
          ;(isPurchase ? restorePurchase : restoreTransaction)
            .mutateAsync(target.id)
            .then(() => toast.success(texts.restored))
            .catch((error: unknown) => toast.error(messageOf(error)))
        },
      },
    })
  }

  return { run, isPending: deleteTransaction.isPending || deletePurchase.isPending }
}

// Transferência (ou pagamento de fatura): origem, destino, data e meio. Não é editada; excluir
// leva as duas pontas.
function TransferDetails({ entry, accounts, onClose }: { entry: TransferEntry; accounts: Account[]; onClose: () => void }) {
  const leg = (entry.out ?? entry.in)!
  const isPayment = !!entry.in?.statementId
  const from = accounts.find((a) => a.id === entry.out?.accountId)
  const to = accounts.find((a) => a.id === entry.in?.accountId)
  const remove = useRemove(
    { kind: isPayment ? 'payment' : 'transfer', id: leg.id },
    `${leg.description} · ${formatCents(entry.amountCents)}`,
  )

  return (
    <>
      <div className="flex min-h-0 flex-col overflow-y-auto overscroll-contain">
        <header className="flex flex-col items-center gap-3 px-6 pt-4 pb-5 text-center">
          <TransferTile payment={isPayment} size="xl" />
          <SheetTitle className="font-display text-2xl leading-tight font-normal">{leg.description}</SheetTitle>
          <span className="font-display text-5xl leading-none tabular-nums">{formatCents(entry.amountCents)}</span>
          <p className="text-sm text-muted-foreground">Não entra em receita nem despesa</p>
        </header>
        <div className="spectrum-line mx-6 opacity-70" />
        <dl className="surface mx-4 my-5 flex flex-col divide-y divide-border/60 rounded-2xl text-sm">
          {from && (
            <InfoRow label="De">
              <span className="flex items-center gap-2">
                <AccountTile name={from.name} type={from.type} size="sm" />
                {from.name}
              </span>
            </InfoRow>
          )}
          {to && (
            <InfoRow label={isPayment ? 'Fatura do cartão' : 'Para'}>
              <span className="flex items-center gap-2">
                <AccountTile name={to.name} type={to.type} size="sm" />
                {to.name}
              </span>
            </InfoRow>
          )}
          <InfoRow label="Data">{formatLongDate(leg.purchaseDate)}</InfoRow>
          <InfoRow label="Meio">{paymentMethodLabels[leg.method]}</InfoRow>
        </dl>
        <p className="mx-4 mb-5 rounded-2xl bg-muted/60 px-4 py-3 text-xs leading-relaxed text-muted-foreground">
          {isPayment
            ? 'Desfazer o pagamento exclui a transferência e a fatura volta a ficar em aberto.'
            : 'Transferência não é editada. Para corrigir, exclua e lance de novo.'}
        </p>
      </div>
      <SheetFooterBar>
        <Button
          size="lg"
          variant="outline"
          className="h-12 w-full rounded-2xl text-base"
          disabled={remove.isPending}
          onClick={() => remove.run(onClose)}
        >
          <Trash2 />
          {isPayment ? 'Desfazer pagamento' : 'Excluir transferência'}
        </Button>
      </SheetFooterBar>
    </>
  )
}

function messageOf(error: unknown): string {
  return error instanceof ApiError ? error.message : 'Não foi possível conectar. Tente novamente.'
}

// ---------------------------------------------------------------------------------------------
// Edição. O escopo segue docs/fase-1.md, 2.2: fora do cartão tudo muda; compra à vista no cartão
// muda valor, descrição e categoria; compra parcelada muda total, parcelas, descrição e
// categoria; parcela isolada muda só descrição e categoria.

type EditorProps = Common & { mode: Mode; onDone: () => void }

function Editor(props: EditorProps) {
  const { entry, mode } = props
  if (mode.view === 'repeat' && entry.kind === 'single') return <RepeatForm {...props} transaction={entry.transaction} />
  if (mode.view === 'installment' && entry.kind === 'purchase') {
    const installment = entry.installments.find((t) => t.id === mode.id)
    if (installment) return <InstallmentForm {...props} transaction={installment} count={entry.installments.length} />
  }
  if (entry.kind === 'single' && entry.transaction.type === 'Refund')
    return <RefundForm {...props} transaction={entry.transaction} />
  const kind: EditKind = editKind(entry)
  if (kind === 'purchase' && entry.kind === 'purchase') return <PurchaseForm {...props} purchase={entry} />
  if (entry.kind === 'single' && kind === 'card') return <CardForm {...props} transaction={entry.transaction} />
  if (entry.kind === 'single') return <SimpleForm {...props} transaction={entry.transaction} />
  return null
}

function EditShell({
  title,
  subtitle,
  onBack,
  submitting,
  error,
  formId,
  submitLabel = 'Salvar alterações',
  children,
}: {
  title: string
  subtitle?: string
  onBack: () => void
  submitting: boolean
  error?: string
  formId: string
  submitLabel?: string
  children: ReactNode
}) {
  return (
    <>
      <header className="flex shrink-0 items-center gap-2 px-2 pt-1 pb-2">
        <Button variant="ghost" size="icon" className="rounded-full" aria-label="Voltar" onClick={onBack}>
          <ArrowLeft />
        </Button>
        <div className="flex min-w-0 flex-col">
          <SheetTitle className="truncate text-base font-medium">{title}</SheetTitle>
          {subtitle && <SheetDescription className="truncate text-xs">{subtitle}</SheetDescription>}
        </div>
      </header>
      <div className="flex min-h-0 flex-col gap-6 overflow-y-auto overscroll-contain px-4 pt-2 pb-6">
        {error && (
          <Alert variant="destructive">
            <AlertDescription>{error}</AlertDescription>
          </Alert>
        )}
        {children}
      </div>
      <SheetFooterBar>
        <Button type="submit" form={formId} size="lg" disabled={submitting} className="h-12 w-full rounded-2xl text-base">
          {submitting ? 'Salvando…' : submitLabel}
        </Button>
      </SheetFooterBar>
    </>
  )
}

function DescriptionField({
  field,
  value,
  category,
  error,
}: {
  field: UseFormRegisterReturn<'description'>
  value: string
  category: { name: string; icon: string | null; color: string | null } | undefined
  error?: string
}) {
  return (
    <Section title="Descrição">
      <label className="flex items-center gap-3 rounded-2xl border bg-card py-2 pr-3 pl-2 focus-within:ring-2 focus-within:ring-ring/50">
        <EntryTile description={value} category={category} />
        <input
          placeholder="Ex.: Uber, iFood, Pão de Açúcar"
          autoComplete="off"
          className="h-10 min-w-0 flex-1 bg-transparent text-base outline-none placeholder:text-muted-foreground"
          {...field}
        />
      </label>
      <FieldError message={error} />
    </Section>
  )
}

function Note({ children }: { children: ReactNode }) {
  return <p className="rounded-2xl bg-muted/60 px-4 py-3 text-xs leading-relaxed text-muted-foreground">{children}</p>
}

const description = z.string().max(200, 'A descrição deve ter no máximo 200 caracteres.')

// Depois de salvar, volta aos detalhes (que leem a lista recarregada). Se a data da compra foi
// para outro mês, o lançamento sai da lista atual: fecha o painel e a lista vai para o novo mês.
// note: o que mais mudou (o cartão novo, docs/fase-2.md, 2.13), no aviso.
function useSaved({ onDone, onClose, onMoved }: Pick<EditorProps, 'onDone' | 'onClose' | 'onMoved'>) {
  return (before?: string, after?: string, note?: string) => {
    if (before && after && before.slice(0, 7) !== after.slice(0, 7)) {
      const moved = `Compra movida para ${formatShortDate(after)}.`
      toast.success('Alterações salvas', { description: note ? `${moved} ${note}` : moved })
      onClose()
      onMoved(monthOf(after))
      return
    }
    toast.success('Alterações salvas', note ? { description: note } : undefined)
    onDone()
  }
}

// O aviso da troca de cartão, a partir do que a API devolveu (a fatura nova é a do vencimento).
function cardNote(cards: Account[], before: string, after: Pick<Transaction, 'accountId' | 'settlementDate'>) {
  if (after.accountId === before) return undefined
  const card = cards.find((a) => a.id === after.accountId)
  return card ? cardChangeText(card.name, after.settlementDate) : undefined
}

// Seção "Cartão" da compra no cartão: só aparece quando há outro cartão para escolher.
function CardPicker({ choices, value, onChange }: { choices: Account[]; value: string; onChange: (id: string) => void }) {
  if (choices.length < 2) return null
  return (
    <Section title="Cartão">
      <ChipRow>
        {choices.map((a) => (
          <Chip key={a.id} selected={a.id === value} onClick={() => onChange(a.id)}>
            <AccountTile name={a.name} type={a.type} size="sm" />
            {a.name}
          </Chip>
        ))}
      </ChipRow>
    </Section>
  )
}

const simpleSchema = z.object({
  type: z.enum(['Expense', 'Income']),
  amountCents: z.number().int().min(1, 'Informe o valor.'),
  accountId: z.string().min(1, 'Escolha a conta.'),
  categoryId: z.string(),
  method: z.enum(['Pix', 'Debit', 'Credit', 'Boleto', 'Cash', 'Ted']),
  purchaseDate: z.string().min(1, 'Informe a data.'),
  description,
})

function SimpleForm({ transaction, accounts, categories, onDone, onClose, onMoved }: EditorProps & { transaction: Transaction }) {
  const update = useUpdateTransaction()
  const saved = useSaved({ onDone, onClose, onMoved })
  const form = useForm<z.infer<typeof simpleSchema>>({
    resolver: zodResolver(simpleSchema),
    defaultValues: {
      type: transaction.type === 'Income' ? 'Income' : 'Expense',
      amountCents: transaction.amountCents,
      accountId: transaction.accountId,
      categoryId: transaction.categoryId ?? '',
      method: transaction.method,
      purchaseDate: transaction.purchaseDate,
      description: transaction.description,
    },
  })
  const { errors, isSubmitting } = form.formState
  const [type, amountCents, accountId, categoryId, method, purchaseDate, text] = useWatch({
    control: form.control,
    name: ['type', 'amountCents', 'accountId', 'categoryId', 'method', 'purchaseDate', 'description'],
  })
  const set = <K extends Path<z.infer<typeof simpleSchema>>>(field: K, value: PathValue<z.infer<typeof simpleSchema>, K>) =>
    form.setValue(field, value, { shouldValidate: form.formState.isSubmitted })

  // Cartão tem edição própria (a compra no cartão); aqui só contas fora do cartão.
  const choices = accounts.filter((a) => a.type !== 'CreditCard' && (a.isActive || a.id === transaction.accountId))
  const roots = categories.filter((c) => c.type === type)
  const { root: selectedRoot, label } = resolveCategory(roots, categoryId)

  // Categoria de receita não serve para despesa, e vice-versa.
  useEffect(() => {
    if (categoryId && !selectedRoot) form.setValue('categoryId', '')
  }, [categoryId, selectedRoot, form])

  const submit = form.handleSubmit(async (values) => {
    try {
      await update.mutateAsync({
        id: transaction.id,
        body: {
          accountId: values.accountId,
          type: values.type,
          amountCents: values.amountCents,
          purchaseDate: values.purchaseDate,
          categoryId: values.categoryId || null,
          method: values.method,
          description: values.description.trim() || null,
        },
      })
      saved(transaction.purchaseDate, values.purchaseDate)
    } catch (error) {
      form.setError('root', { message: messageOf(error) })
    }
  })

  return (
    <EditShell
      title="Editar lançamento"
      onBack={onDone}
      submitting={isSubmitting}
      error={errors.root?.message}
      formId="edit-entry"
    >
      <form id="edit-entry" onSubmit={submit} noValidate className="contents">
        <div className="flex flex-col items-center gap-4">
          <TypeToggle value={type} incomeDisabled={false} onChange={(value) => value !== 'Refund' && set('type', value)} />
          <AmountField
            value={amountCents}
            onChange={(c) => set('amountCents', c)}
            income={type === 'Income'}
            error={errors.amountCents?.message}
            compact
          />
        </div>
        <DescriptionField
          field={form.register('description')}
          value={text}
          category={label}
          error={errors.description?.message}
        />
        <Section title="Conta">
          <ChipRow>
            {choices.map((a) => (
              <Chip key={a.id} selected={a.id === accountId} onClick={() => set('accountId', a.id)}>
                <AccountTile name={a.name} type={a.type} size="sm" />
                {a.name}
              </Chip>
            ))}
          </ChipRow>
        </Section>
        <Section title="Categoria">
          <CategoryPicker roots={roots} value={categoryId} onChange={(id) => set('categoryId', id)} />
        </Section>
        <Section title="Data">
          <DateChooser value={purchaseDate} onChange={(date) => set('purchaseDate', date)} error={errors.purchaseDate?.message} />
        </Section>
        <Section title="Pagamento">
          <ChipRow>
            {simpleMethods.map((m) => (
              <Chip key={m} selected={m === method} onClick={() => set('method', m)}>
                {paymentMethodLabels[m]}
              </Chip>
            ))}
          </ChipRow>
        </Section>
      </form>
    </EditShell>
  )
}

const cardSchema = z.object({
  amountCents: z.number().int().min(1, 'Informe o valor.'),
  purchaseDate: z.string().min(1, 'Informe a data.'),
  accountId: z.string(),
  categoryId: z.string(),
  description,
})

// Compra à vista no cartão: valor, data, cartão, descrição e categoria. O meio segue o da compra; a
// data ou o cartão novos levam a compra para a fatura do seu ciclo (etapa 1.14b; docs/fase-2.md, 2.13).
function CardForm({ transaction, accounts, categories, onDone, onClose, onMoved }: EditorProps & { transaction: Transaction }) {
  const update = useUpdateTransaction()
  const saved = useSaved({ onDone, onClose, onMoved })
  const form = useForm<z.infer<typeof cardSchema>>({
    resolver: zodResolver(cardSchema),
    defaultValues: {
      amountCents: transaction.amountCents,
      purchaseDate: transaction.purchaseDate,
      accountId: transaction.accountId,
      categoryId: transaction.categoryId ?? '',
      description: transaction.description,
    },
  })
  const { errors, isSubmitting } = form.formState
  const [amountCents, purchaseDate, accountId, categoryId, text] = useWatch({
    control: form.control,
    name: ['amountCents', 'purchaseDate', 'accountId', 'categoryId', 'description'],
  })
  const roots = categories.filter((c) => c.type === 'Expense')
  const { label } = resolveCategory(roots, categoryId)
  const cards = cardChoices(accounts, transaction.accountId)

  const submit = form.handleSubmit(async (values) => {
    try {
      const updated = await update.mutateAsync({
        id: transaction.id,
        body: {
          ...sameAs(transaction, values.categoryId, values.description, values.amountCents),
          accountId: values.accountId,
          purchaseDate: values.purchaseDate,
        },
      })
      saved(transaction.purchaseDate, values.purchaseDate, cardNote(cards, transaction.accountId, updated))
    } catch (error) {
      form.setError('root', { message: messageOf(error) })
    }
  })

  return (
    <EditShell
      title="Editar compra no cartão"
      onBack={onDone}
      submitting={isSubmitting}
      error={errors.root?.message}
      formId="edit-card"
    >
      <form id="edit-card" onSubmit={submit} noValidate className="contents">
        <AmountField
          value={amountCents}
          onChange={(c) => form.setValue('amountCents', c, { shouldValidate: form.formState.isSubmitted })}
          income={false}
          error={errors.amountCents?.message}
          compact
        />
        <DescriptionField
          field={form.register('description')}
          value={text}
          category={label}
          error={errors.description?.message}
        />
        <Section title="Categoria">
          <CategoryPicker roots={roots} value={categoryId} onChange={(id) => form.setValue('categoryId', id)} />
        </Section>
        <CardPicker choices={cards} value={accountId} onChange={(id) => form.setValue('accountId', id)} />
        <Section title="Data da compra" aside={`fatura atual vence ${formatShortDate(transaction.settlementDate)}`}>
          <DateChooser
            value={purchaseDate}
            onChange={(date) => form.setValue('purchaseDate', date)}
            error={errors.purchaseDate?.message}
          />
        </Section>
        <Note>Mudar a data ou o cartão leva a compra para a fatura do novo ciclo.</Note>
      </form>
    </EditShell>
  )
}

const refundSchema = z.object({
  amountCents: z.number().int().min(1, 'Informe o valor.'),
  purchaseDate: z.string().min(1, 'Informe a data.'),
  categoryId: z.string(),
  method: z.enum(['Pix', 'Debit', 'Credit', 'Boleto', 'Cash', 'Ted']),
  description,
})

// Estorno (docs/fase-2.md, 2.5, regra 14): valor, data, categoria, descrição e meio. Conta e compra
// estornada não mudam; no cartão, a data nova pode levar o estorno a outra fatura.
function RefundForm({ transaction, accounts, categories, onDone, onClose, onMoved }: EditorProps & { transaction: Transaction }) {
  const update = useUpdateTransaction()
  const saved = useSaved({ onDone, onClose, onMoved })
  const form = useForm<z.infer<typeof refundSchema>>({
    resolver: zodResolver(refundSchema),
    defaultValues: {
      amountCents: transaction.amountCents,
      purchaseDate: transaction.purchaseDate,
      categoryId: transaction.categoryId ?? '',
      method: transaction.method,
      description: transaction.description,
    },
  })
  const { errors, isSubmitting } = form.formState
  const [amountCents, purchaseDate, categoryId, method, text] = useWatch({
    control: form.control,
    name: ['amountCents', 'purchaseDate', 'categoryId', 'method', 'description'],
  })
  const set = <K extends Path<z.infer<typeof refundSchema>>>(field: K, value: PathValue<z.infer<typeof refundSchema>, K>) =>
    form.setValue(field, value, { shouldValidate: form.formState.isSubmitted })
  const account = accounts.find((a) => a.id === transaction.accountId)
  const isCard = transaction.statementId !== null
  const roots = categories.filter((c) => c.type === 'Expense')
  const { label } = resolveCategory(roots, categoryId)

  const submit = form.handleSubmit(async (values) => {
    try {
      await update.mutateAsync({
        id: transaction.id,
        body: {
          ...sameAs(transaction, values.categoryId, values.description, values.amountCents),
          purchaseDate: values.purchaseDate,
          method: values.method,
        },
      })
      saved(transaction.purchaseDate, values.purchaseDate)
    } catch (error) {
      form.setError('root', { message: messageOf(error) })
    }
  })

  return (
    <EditShell title="Editar estorno" onBack={onDone} submitting={isSubmitting} error={errors.root?.message} formId="edit-refund">
      <form id="edit-refund" onSubmit={submit} noValidate className="contents">
        <AmountField
          value={amountCents}
          onChange={(c) => set('amountCents', c)}
          income={false}
          error={errors.amountCents?.message}
          compact
        />
        <DescriptionField
          field={form.register('description')}
          value={text}
          category={label}
          error={errors.description?.message}
        />
        <Section title="Categoria">
          <CategoryPicker roots={roots} value={categoryId} onChange={(id) => set('categoryId', id)} />
        </Section>
        <Section
          title="Data do estorno"
          aside={isCard ? `fatura atual vence ${formatShortDate(transaction.settlementDate)}` : undefined}
        >
          <DateChooser value={purchaseDate} onChange={(date) => set('purchaseDate', date)} error={errors.purchaseDate?.message} />
        </Section>
        {!isCard && (
          <Section title="Meio">
            <ChipRow>
              {simpleMethods.map((m) => (
                <Chip key={m} selected={m === method} onClick={() => set('method', m)}>
                  {paymentMethodLabels[m]}
                </Chip>
              ))}
            </ChipRow>
          </Section>
        )}
        <Note>
          {account ? `Fica em ${account.name}. ` : ''}
          {isCard ? 'Mudar a data leva o estorno para a fatura aberta na nova data. ' : ''}
          Para trocar a conta ou a compra estornada, exclua e lance de novo.
        </Note>
      </form>
    </EditShell>
  )
}

const purchaseSchema = z.object({
  totalCents: z.number().int().min(1, 'Informe o valor.'),
  installments: z.number().int().min(1).max(maxInstallments),
  purchaseDate: z.string().min(1, 'Informe a data.'),
  categoryId: z.string(),
  accountId: z.string(),
  description,
})

// Compra parcelada inteira: o total é redistribuído entre as parcelas não pagas (docs/fase-1.md, 2.2).
// O cartão novo leva todas as parcelas para as faturas dele (docs/fase-2.md, 2.13).
function PurchaseForm({
  purchase,
  accounts,
  categories,
  onDone,
  onClose,
  onMoved,
}: EditorProps & { purchase: Extract<Entry, { kind: 'purchase' }> }) {
  const update = useUpdatePurchase()
  const saved = useSaved({ onDone, onClose, onMoved })
  const first = [...purchase.installments].sort(byNumber)[0]
  const form = useForm<z.infer<typeof purchaseSchema>>({
    resolver: zodResolver(
      purchaseSchema.refine((v) => v.totalCents >= v.installments, {
        message: 'O valor total deve ter ao menos 1 centavo por parcela.',
        path: ['totalCents'],
      }),
    ),
    defaultValues: {
      totalCents: purchase.totalCents,
      installments: purchase.installments.length,
      purchaseDate: first.purchaseDate,
      categoryId: first.categoryId ?? '',
      accountId: first.accountId,
      description: first.description,
    },
  })
  const { errors, isSubmitting } = form.formState
  const [totalCents, installments, purchaseDate, categoryId, accountId, text] = useWatch({
    control: form.control,
    name: ['totalCents', 'installments', 'purchaseDate', 'categoryId', 'accountId', 'description'],
  })
  const roots = categories.filter((c) => c.type === 'Expense')
  const { label } = resolveCategory(roots, categoryId)
  const cards = cardChoices(accounts, first.accountId)
  const set = <K extends Path<z.infer<typeof purchaseSchema>>>(field: K, value: PathValue<z.infer<typeof purchaseSchema>, K>) =>
    form.setValue(field, value, { shouldValidate: form.formState.isSubmitted })

  const submit = form.handleSubmit(async (values) => {
    try {
      const updated = await update.mutateAsync({
        id: purchase.purchaseId,
        body: {
          totalAmountCents: values.totalCents,
          installmentCount: values.installments,
          categoryId: values.categoryId || null,
          description: values.description.trim() || null,
          purchaseDate: values.purchaseDate,
          accountId: values.accountId,
        },
      })
      const firstNow = [...updated.installments].sort(byNumber)[0]
      saved(first.purchaseDate, values.purchaseDate, firstNow && cardNote(cards, first.accountId, firstNow))
    } catch (error) {
      form.setError('root', { message: messageOf(error) })
    }
  })

  return (
    <EditShell
      title="Editar compra parcelada"
      subtitle={describeInstallments(purchase.totalCents, purchase.installments.length)}
      onBack={onDone}
      submitting={isSubmitting}
      error={errors.root?.message}
      formId="edit-purchase"
    >
      <form id="edit-purchase" onSubmit={submit} noValidate className="contents">
        <AmountField
          label="Valor total"
          value={totalCents}
          onChange={(c) => set('totalCents', c)}
          income={false}
          error={errors.totalCents?.message}
          compact
        />
        <Section
          title="Parcelas"
          aside={installments > 1 && totalCents >= installments ? describeInstallments(totalCents, installments) : undefined}
        >
          <ChipRow>
            {Array.from({ length: maxInstallments }, (_, i) => i + 1).map((n) => (
              <Chip key={n} selected={n === installments} onClick={() => set('installments', n)} className="tabular-nums">
                {n === 1 ? 'À vista' : `${n}x`}
              </Chip>
            ))}
          </ChipRow>
        </Section>
        <DescriptionField
          field={form.register('description')}
          value={text}
          category={label}
          error={errors.description?.message}
        />
        <Section title="Categoria">
          <CategoryPicker roots={roots} value={categoryId} onChange={(id) => set('categoryId', id)} />
        </Section>
        <CardPicker choices={cards} value={accountId} onChange={(id) => set('accountId', id)} />
        <Section title="Data da compra">
          <DateChooser value={purchaseDate} onChange={(date) => set('purchaseDate', date)} error={errors.purchaseDate?.message} />
        </Section>
        <Note>
          O total é dividido de novo entre as parcelas ainda não pagas, sem perder centavo. Parcela em fatura paga mantém o valor.
          Mudar a data ou o cartão leva todas as parcelas para as faturas dos novos ciclos.
        </Note>
      </form>
    </EditShell>
  )
}

const installmentSchema = z.object({ categoryId: z.string(), description })

// Parcela isolada: só descrição e categoria. Valor e data mudam pela compra inteira.
function InstallmentForm({
  transaction,
  count,
  categories,
  onDone,
  onClose,
  onMoved,
}: EditorProps & { transaction: Transaction; count: number }) {
  const update = useUpdateTransaction()
  const saved = useSaved({ onDone, onClose, onMoved })
  const form = useForm<z.infer<typeof installmentSchema>>({
    resolver: zodResolver(installmentSchema),
    defaultValues: { categoryId: transaction.categoryId ?? '', description: transaction.description },
  })
  const { errors, isSubmitting } = form.formState
  const [categoryId, text] = useWatch({ control: form.control, name: ['categoryId', 'description'] })
  const roots = categories.filter((c) => c.type === 'Expense')
  const { label } = resolveCategory(roots, categoryId)

  const submit = form.handleSubmit(async (values) => {
    try {
      await update.mutateAsync({ id: transaction.id, body: sameAs(transaction, values.categoryId, values.description) })
      saved()
    } catch (error) {
      form.setError('root', { message: messageOf(error) })
    }
  })

  return (
    <EditShell
      title={`Parcela ${transaction.installmentNumber} de ${count}`}
      subtitle={`${formatCents(transaction.amountCents)} · vence ${formatShortDate(transaction.settlementDate)}`}
      onBack={onDone}
      submitting={isSubmitting}
      error={errors.root?.message}
      formId="edit-installment"
    >
      <form id="edit-installment" onSubmit={submit} noValidate className="contents">
        <DescriptionField
          field={form.register('description')}
          value={text}
          category={label}
          error={errors.description?.message}
        />
        <Section title="Categoria">
          <CategoryPicker roots={roots} value={categoryId} onChange={(id) => form.setValue('categoryId', id)} />
        </Section>
        <Note>
          Muda só esta parcela. Valor, número de parcelas, data e cartão mudam pela compra inteira, para a soma continuar igual ao
          total.
        </Note>
      </form>
    </EditShell>
  )
}

// O lançamento passa a se repetir e vira a primeira ocorrência (docs/fase-2.md, 2.14, regra 1). Começa em
// "Todo mês", o caso mais comum (o pet de todo dia 25); a data do lançamento é a partida.
function RepeatForm({ transaction, accounts, onDone }: EditorProps & { transaction: Transaction }) {
  const start = useStartRecurrence()
  const today = todayInSaoPaulo()
  const [choice, setChoice] = useState<RepeatChoice>({ frequency: 'Monthly', endDate: null })
  // Débito automático (docs/fase-2.md, 2.15): o débito já lançado numa conta corrente vira a série.
  const accountType = accounts.find((a) => a.id === transaction.accountId)?.type
  const autoDebitAvailable = transaction.type !== 'Transfer' && canAutoDebit({ type: transaction.type, accountType })
  const [endError, setEndError] = useState<string>()
  const [error, setError] = useState<string>()

  const submit = async (event: FormEvent) => {
    event.preventDefault()
    const request = repeatRequest(autoDebitAvailable ? choice : { ...choice, autoDebit: false }, transaction.purchaseDate)
    if (request === 'missing-end') return setEndError(missingEndMessage)
    if (request === null) return
    try {
      await start.mutateAsync({ transactionId: transaction.id, body: request })
    } catch (e) {
      setError(messageOf(e))
      return
    }
    const { dueNow } = describeSeries({ start: request.dueDate ?? transaction.purchaseDate, ...request, today })
    toast.success(request.autoDebit ? 'Agora é débito automático' : 'Agora se repete', {
      description: [
        request.dueDate
          ? autoDebitLine({ due: request.dueDate, endDate: request.endDate, today })
          : seriesLine({ start: transaction.purchaseDate, ...request, today }),
        dueNow.length > 0 && (dueNow.length === 1 ? '1 lançada agora' : `${dueNow.length} lançadas agora`),
      ]
        .filter(Boolean)
        .join(' · '),
    })
    onDone()
  }

  return (
    <EditShell
      title="Se repete"
      subtitle={`${transaction.description || 'Lançamento'} · ${formatCents(transaction.amountCents)} · desde ${formatShortDate(transaction.purchaseDate)}`}
      onBack={onDone}
      submitting={start.isPending}
      error={error}
      formId="start-series"
      submitLabel="Salvar"
    >
      <form id="start-series" onSubmit={submit} noValidate className="contents">
        <RecurrenceChooser
          choice={choice}
          onChange={(next) => {
            setChoice(next)
            setEndError(undefined)
          }}
          start={transaction.purchaseDate}
          today={today}
          error={endError}
          autoDebitAvailable={autoDebitAvailable}
        />
        <Note>Este lançamento vira o primeiro da série. Para mudar valor ou data, edite antes.</Note>
      </form>
    </EditShell>
  )
}

// PATCH de lançamento no cartão: conta, tipo, data e meio seguem os atuais (tipo e meio a API recusa
// mudar; conta e data, quem muda é o formulário da compra).
function sameAs(t: Transaction, categoryId: string, text: string, amountCents = t.amountCents) {
  return {
    accountId: t.accountId,
    type: t.type,
    amountCents,
    purchaseDate: t.purchaseDate,
    categoryId: categoryId || null,
    method: t.method,
    description: text.trim() || null,
  }
}
