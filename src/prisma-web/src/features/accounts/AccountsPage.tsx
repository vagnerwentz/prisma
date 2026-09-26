import { ChevronRight, Plus } from 'lucide-react'
import { useState } from 'react'
import { Link, useNavigate } from 'react-router'
import { BottomSheet } from '@/components/BottomSheet'
import { AccountTile } from '@/components/brand/Tiles'
import { PrismLogo } from '@/components/brand/PrismLogo'
import { Alert, AlertDescription } from '@/components/ui/alert'
import { Button } from '@/components/ui/button'
import { SheetTitle } from '@/components/ui/sheet'
import { Skeleton } from '@/components/ui/skeleton'
import { formatCents } from '@/lib/money'
import { cn } from '@/lib/utils'
import { AccountForm } from './AccountForm'
import { accountTypeLabels } from './labels'
import { useAccountBalances, useAccounts, type Account, type AccountBalance } from './queries'

export function AccountsPage() {
  const accounts = useAccounts()
  const balances = useAccountBalances()
  const navigate = useNavigate()
  const [creating, setCreating] = useState(false)
  // Ativas primeiro; as inativas continuam na lista, para consultar o histórico.
  const sorted = [...(accounts.data ?? [])].sort((a, b) => Number(b.isActive) - Number(a.isActive))

  return (
    <main className="mx-auto flex max-w-2xl flex-col gap-6 px-4 pt-5 pb-32">
      <div className="flex flex-col gap-3">
        <div className="flex items-end justify-between">
          <h1 className="font-display text-4xl leading-none">Contas</h1>
          {accounts.isSuccess && accounts.data.length > 0 && (
            <Button size="sm" variant="secondary" className="rounded-full" onClick={() => setCreating(true)}>
              <Plus />
              Nova conta
            </Button>
          )}
        </div>
        <div className="spectrum-line opacity-80" />
      </div>

      {accounts.isPending && <Skeleton className="h-40 w-full rounded-2xl" />}
      {accounts.isError && (
        <Alert variant="destructive">
          <AlertDescription>Não foi possível carregar as contas.</AlertDescription>
        </Alert>
      )}
      {accounts.isSuccess && accounts.data.length === 0 && (
        <div className="flex flex-col items-center gap-4 rounded-3xl border border-dashed px-6 py-12 text-center">
          <PrismLogo className="size-14 text-muted-foreground" />
          <div className="flex flex-col gap-1">
            <p className="font-display text-2xl">Primeiro, uma conta</p>
            <p className="text-sm text-muted-foreground">Lançamentos acontecem numa conta: corrente, cartão, carteira…</p>
          </div>
          <Button className="rounded-full" onClick={() => setCreating(true)}>
            <Plus />
            Criar conta
          </Button>
        </div>
      )}
      {sorted.length > 0 && (
        <ul className="surface overflow-hidden rounded-2xl">
          {sorted.map((account) => (
            <AccountRow key={account.id} account={account} balance={balances.data?.get(account.id)} />
          ))}
        </ul>
      )}

      <BottomSheet open={creating} onClose={() => setCreating(false)}>
        <header className="px-4 pt-2 pb-3">
          <SheetTitle className="font-display text-2xl font-normal">Nova conta</SheetTitle>
        </header>
        <AccountForm
          formId="new-account"
          submitLabel="Criar conta"
          onSaved={(account) => {
            setCreating(false)
            navigate(`/contas/${account.id}`)
          }}
        />
      </BottomSheet>
    </main>
  )
}

function AccountRow({ account, balance }: { account: Account; balance: AccountBalance | undefined }) {
  // Cartão: fechamento e vencimento, sem repetir "Cartão" (o "Disponível" e o logo já dizem). Curto
  // e com espaço inseparável dentro de cada dado, para a linha quebrar só entre eles em 320px.
  const details =
    account.type === 'CreditCard'
      ? [`Fecha\u00a0${account.closingDay}`, `vence\u00a0${account.dueDay}`]
      : [accountTypeLabels[account.type]]

  return (
    <li className="[&+&]:border-t [&+&]:border-border/60">
      <Link
        to={`/contas/${account.id}`}
        className={cn(
          'flex items-center gap-3 px-4 py-3 transition-colors outline-none hover:bg-muted/40 focus-visible:bg-muted/60 active:bg-muted/70',
          !account.isActive && 'opacity-60',
        )}
      >
        <AccountTile name={account.name} type={account.type} />
        <div className="min-w-0 flex-1">
          <p className="flex items-center gap-2 truncate font-medium">
            {account.name}
            {!account.isActive && (
              <span className="rounded-full border px-2 text-[0.65rem] font-normal text-muted-foreground">inativa</span>
            )}
          </p>
          {/* Quebra em vez de cortar: fechamento e vencimento são o que se procura aqui. */}
          <p className="text-sm text-pretty text-muted-foreground">{details.join(' · ')}</p>
        </div>
        {balance && <BalanceFigure account={account} balance={balance} />}
        <ChevronRight className="size-4 shrink-0 text-muted-foreground" />
      </Link>
    </li>
  )
}

// Conta: saldo atual. Cartão: limite disponível, ou o que falta pagar quando não há limite.
function BalanceFigure({ account, balance }: { account: Account; balance: AccountBalance }) {
  const [label, cents] =
    account.type !== 'CreditCard'
      ? ['Saldo', balance.balanceCents]
      : balance.availableCreditCents != null
        ? ['Disponível', balance.availableCreditCents]
        : ['A pagar', balance.owedCents]
  if (cents == null) return null
  return (
    <div className="shrink-0 text-right">
      <p className="text-xs text-muted-foreground">{label}</p>
      <p className="font-medium tabular-nums">{formatCents(cents)}</p>
    </div>
  )
}
