import { ArrowLeft, ChevronRight, Pencil, Power, Trash2 } from 'lucide-react'
import { useState, type ReactNode } from 'react'
import { Link, Navigate, useNavigate, useParams } from 'react-router'
import { toast } from 'sonner'
import { BottomSheet } from '@/components/BottomSheet'
import { AccountTile } from '@/components/brand/Tiles'
import { Alert, AlertDescription } from '@/components/ui/alert'
import { Button } from '@/components/ui/button'
import { SheetTitle } from '@/components/ui/sheet'
import { Skeleton } from '@/components/ui/skeleton'
import { ApiError } from '@/lib/api'
import { formatShortDate, todayInSaoPaulo } from '@/lib/dates'
import { formatCents } from '@/lib/money'
import { AccountForm } from './AccountForm'
import { accountTypeLabels } from './labels'
import {
  useAccountBalances,
  useAccounts,
  useDeleteAccount,
  useStatements,
  useUpdateAccount,
  type Account,
  type Statement,
} from './queries'
import { StatementSheet, StatusBadge } from './StatementSheet'
import { groupStatements, statementStatus, statementTitle, type StatementStatus } from './statements'

export function AccountDetailPage() {
  const { id } = useParams()
  const accounts = useAccounts()
  const account = accounts.data?.find((a) => a.id === id)

  if (accounts.isPending) {
    return (
      <main className="mx-auto flex max-w-2xl flex-col gap-4 px-4 pt-5">
        <Skeleton className="h-9 w-40" />
        <Skeleton className="h-48 w-full rounded-3xl" />
      </main>
    )
  }
  if (accounts.isError) {
    return (
      <main className="mx-auto max-w-2xl px-4 pt-5">
        <Alert variant="destructive">
          <AlertDescription>Não foi possível carregar a conta.</AlertDescription>
        </Alert>
      </main>
    )
  }
  // Conta excluída ou de outro usuário: volta para a lista.
  if (!account) return <Navigate to="/contas" replace />

  return <Detail account={account} />
}

function Detail({ account }: { account: Account }) {
  const isCard = account.type === 'CreditCard'
  const [editing, setEditing] = useState(false)
  const balance = useAccountBalances().data?.get(account.id)

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

      <header className="flex items-center gap-4">
        <AccountTile name={account.name} type={account.type} size="xl" />
        <div className="flex min-w-0 flex-col gap-1">
          <h1 className="truncate font-display text-4xl leading-none">{account.name}</h1>
          <p className="flex items-center gap-2 text-sm text-muted-foreground">
            {accountTypeLabels[account.type]}
            {!account.isActive && <span className="rounded-full border px-2 text-xs">inativa</span>}
          </p>
        </div>
      </header>
      <div className="spectrum-line -mt-2 opacity-80" />

      {!isCard && balance?.balanceCents != null && (
        <section className="spectrum-ring flex flex-col gap-2 rounded-3xl p-5">
          <span className="text-xs font-medium tracking-wide text-muted-foreground uppercase">Saldo atual</span>
          <span className="font-display text-5xl leading-none tabular-nums">{formatCents(balance.balanceCents)}</span>
          {balance.projectedBalanceCents != null && balance.projectedBalanceCents !== balance.balanceCents && (
            <span className="text-sm text-muted-foreground">
              previsto{' '}
              <span className="font-medium text-foreground tabular-nums">{formatCents(balance.projectedBalanceCents)}</span> com
              os lançamentos futuros
            </span>
          )}
        </section>
      )}

      <dl className="flex flex-col divide-y rounded-2xl border bg-card text-sm">
        {isCard ? (
          <>
            <Info label="Fechamento">todo dia {account.closingDay}</Info>
            <Info label="Vencimento">todo dia {account.dueDay}</Info>
            <Info label="Limite">{account.creditLimitCents ? formatCents(account.creditLimitCents) : 'não informado'}</Info>
            {balance?.owedCents != null && <Info label="A pagar (faturas em aberto)">{formatCents(balance.owedCents)}</Info>}
            {balance?.availableCreditCents != null && (
              <Info label="Limite disponível">{formatCents(balance.availableCreditCents)}</Info>
            )}
          </>
        ) : (
          <Info label="Saldo inicial">{formatCents(account.initialBalanceCents)}</Info>
        )}
      </dl>

      <Actions account={account} onEdit={() => setEditing(true)} />

      {isCard && <Statements account={account} />}

      <BottomSheet open={editing} onClose={() => setEditing(false)}>
        <header className="px-4 pt-2 pb-3">
          <SheetTitle className="font-display text-2xl font-normal">Editar conta</SheetTitle>
        </header>
        <AccountForm account={account} formId="edit-account" submitLabel="Salvar alterações" onSaved={() => setEditing(false)} />
      </BottomSheet>
    </main>
  )
}

function Info({ label, children }: { label: string; children: ReactNode }) {
  return (
    <div className="flex items-center justify-between gap-4 px-4 py-3">
      <dt className="text-muted-foreground">{label}</dt>
      <dd className="text-right font-medium tabular-nums">{children}</dd>
    </div>
  )
}

// Editar, desativar (encerra a conta sem perder o histórico) e excluir (só sem lançamentos: a
// API responde 409 com a orientação). Excluir não tem "Desfazer", então pede confirmação.
function Actions({ account, onEdit }: { account: Account; onEdit: () => void }) {
  const navigate = useNavigate()
  const update = useUpdateAccount()
  const remove = useDeleteAccount()
  const [confirming, setConfirming] = useState(false)

  const toggleActive = async () => {
    try {
      await update.mutateAsync({
        id: account.id,
        body: {
          name: account.name,
          initialBalanceCents: account.initialBalanceCents,
          closingDay: account.closingDay ?? null,
          dueDay: account.dueDay ?? null,
          creditLimitCents: account.creditLimitCents ?? null,
          isActive: !account.isActive,
        },
      })
      toast.success(account.isActive ? 'Conta desativada' : 'Conta reativada', {
        description: account.isActive ? 'Ela some do lançamento rápido; o histórico continua.' : account.name,
      })
    } catch (error) {
      toast.error(messageOf(error))
    }
  }

  const deleteAccount = async () => {
    try {
      await remove.mutateAsync(account.id)
      toast.success('Conta excluída', { description: account.name })
      navigate('/contas', { replace: true })
    } catch (error) {
      setConfirming(false)
      toast.error(messageOf(error))
    }
  }

  if (confirming) {
    return (
      <div className="flex flex-col gap-3 rounded-2xl border bg-card p-4">
        <p className="text-sm">
          Excluir <strong>{account.name}</strong>? Só é possível se a conta não tiver lançamentos.
        </p>
        <div className="grid grid-cols-2 gap-2">
          <Button variant="outline" className="h-11 rounded-xl" onClick={() => setConfirming(false)}>
            Cancelar
          </Button>
          <Button className="h-11 rounded-xl" disabled={remove.isPending} onClick={deleteAccount}>
            {remove.isPending ? 'Excluindo…' : 'Excluir conta'}
          </Button>
        </div>
      </div>
    )
  }

  return (
    <div className="grid grid-cols-3 gap-2">
      <ActionButton icon={<Pencil />} label="Editar" onClick={onEdit} />
      <ActionButton
        icon={<Power />}
        label={account.isActive ? 'Desativar' : 'Reativar'}
        onClick={toggleActive}
        disabled={update.isPending}
      />
      <ActionButton icon={<Trash2 />} label="Excluir" onClick={() => setConfirming(true)} />
    </div>
  )
}

function ActionButton({
  icon,
  label,
  onClick,
  disabled,
}: {
  icon: ReactNode
  label: string
  onClick: () => void
  disabled?: boolean
}) {
  return (
    <button
      type="button"
      onClick={onClick}
      disabled={disabled}
      className="flex flex-col items-center gap-1.5 rounded-2xl border bg-card py-3 text-xs font-medium transition-colors hover:bg-muted/50 active:scale-[0.98] disabled:opacity-50 [&_svg]:size-5 [&_svg]:text-muted-foreground"
    >
      {icon}
      {label}
    </button>
  )
}

function messageOf(error: unknown): string {
  return error instanceof ApiError ? error.message : 'Não foi possível conectar. Tente novamente.'
}

// Faturas do cartão: a atual em destaque, depois as próximas e as anteriores.
function Statements({ account }: { account: Account }) {
  const statements = useStatements(account.id, true)
  const today = todayInSaoPaulo()
  const [openId, setOpenId] = useState<string | null>(null)

  if (statements.isPending) return <Skeleton className="h-40 w-full rounded-3xl" />
  if (statements.isError) {
    return (
      <Alert variant="destructive">
        <AlertDescription>Não foi possível carregar as faturas.</AlertDescription>
      </Alert>
    )
  }

  const { current, upcoming, past } = groupStatements(statements.data, today)
  const open = statements.data.find((s) => s.id === openId)
  const statusOf = (s: Statement) => statementStatus(s, today, current?.id)

  return (
    <section className="flex flex-col gap-5">
      {current ? (
        <button
          type="button"
          onClick={() => setOpenId(current.id)}
          className="spectrum-ring flex flex-col gap-3 rounded-3xl p-5 text-left"
        >
          <div className="flex items-center justify-between">
            <span className="text-xs font-medium tracking-wide text-muted-foreground uppercase">Fatura atual</span>
            <StatusBadge status={statusOf(current)} />
          </div>
          <div className="flex items-end justify-between gap-3">
            <div className="flex flex-col gap-1">
              <span className="font-display text-2xl leading-none">{statementTitle(current.reference)}</span>
              <span className="text-sm text-muted-foreground">
                fecha {dayMonth(current.closingDate)} · vence {dayMonth(current.dueDate)}
              </span>
            </div>
            <span className="font-display text-4xl leading-none tabular-nums">{formatCents(current.totalCents)}</span>
          </div>
        </button>
      ) : (
        <p className="rounded-2xl border border-dashed px-4 py-6 text-center text-sm text-muted-foreground">
          Nenhuma fatura aberta. Ela aparece com a primeira compra no cartão.
        </p>
      )}

      <StatementList title="Próximas faturas" items={upcoming} statusOf={statusOf} onOpen={setOpenId} />
      <StatementList title="Faturas anteriores" items={past} statusOf={statusOf} onOpen={setOpenId} />

      {openId && open && <StatementSheet statement={open} status={statusOf(open)} onClose={() => setOpenId(null)} />}
    </section>
  )
}

function StatementList({
  title,
  items,
  statusOf,
  onOpen,
}: {
  title: string
  items: Statement[]
  statusOf: (s: Statement) => StatementStatus
  onOpen: (id: string) => void
}) {
  if (items.length === 0) return null
  return (
    <div className="flex flex-col gap-2">
      <h2 className="px-1 text-xs font-medium tracking-wide text-muted-foreground uppercase">{title}</h2>
      <ul className="overflow-hidden rounded-2xl border bg-card">
        {items.map((s) => (
          <li key={s.id} className="[&+&]:border-t">
            <button
              type="button"
              onClick={() => onOpen(s.id)}
              className="flex w-full items-center gap-3 px-4 py-3 text-left transition-colors outline-none hover:bg-muted/40 focus-visible:bg-muted/60 active:bg-muted/70"
            >
              <div className="min-w-0 flex-1">
                <p className="font-medium">{statementTitle(s.reference)}</p>
                <p className="text-sm text-muted-foreground tabular-nums">
                  vence {formatShortDate(s.dueDate)}
                  {s.datesEditedManually && ' · datas ajustadas'}
                </p>
              </div>
              <div className="flex shrink-0 flex-col items-end gap-1">
                <span className="tabular-nums">{formatCents(s.totalCents)}</span>
                <StatusBadge status={statusOf(s)} />
              </div>
              <ChevronRight className="size-4 shrink-0 text-muted-foreground" />
            </button>
          </li>
        ))}
      </ul>
    </div>
  )
}

// O ano já está no título da fatura: "2026-10-05" → "05/10".
function dayMonth(date: string): string {
  return formatShortDate(date).slice(0, 5)
}
