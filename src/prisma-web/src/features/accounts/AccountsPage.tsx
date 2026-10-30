import { ChevronRight, Plus, Repeat, TrendingUp } from 'lucide-react'
import { useState } from 'react'
import { Link, useNavigate } from 'react-router'
import { AccountTile } from '@/components/brand/Tiles'
import { PrismLogo } from '@/components/brand/PrismLogo'
import { Alert, AlertDescription } from '@/components/ui/alert'
import { Button } from '@/components/ui/button'
import { Skeleton } from '@/components/ui/skeleton'
import { freeCashOf } from '@/features/dashboard/netWorth'
import { useNetWorth } from '@/features/dashboard/queries'
import { useHoldings } from '@/features/investments/queries'
import { seriesCount } from '@/features/recurrences/listing'
import { useRecurrences } from '@/features/recurrences/queries'
import { formatCents } from '@/lib/money'
import { cn } from '@/lib/utils'
import { accountTypeLabels } from './labels'
import { NewAccountSheet } from './NewAccountSheet'
import { useAccountBalances, useAccounts, type Account, type AccountBalance } from './queries'

export function AccountsPage() {
  const accounts = useAccounts()
  const balances = useAccountBalances()
  // O caixa livre de cada conta de investimento que acompanha o caixa (I.9, docs/patrimonio.md). Não segura a tela.
  const netWorth = useNetWorth().data
  const navigate = useNavigate()
  const [creating, setCreating] = useState(false)
  // Lançamentos que se repetem (docs/fase-2.md, 2.14): a entrada da tela, só quando já há alguma série.
  const series = seriesCount(useRecurrences().data ?? [])
  // Investimentos (docs/investimentos.md, etapa 5a): sempre à vista, porque é por aqui que a área é descoberta.
  const holdings = useHoldings().data
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
            <AccountRow key={account.id} account={account} balance={balances.data?.get(account.id)} freeCashCents={netWorth ? freeCashOf(netWorth, account.id) : null} />
          ))}
        </ul>
      )}
      {series && (
        <Link
          to="/contas/recorrencias"
          className="surface flex items-center gap-3 rounded-2xl px-4 py-3 transition-colors outline-none hover:bg-muted/40 focus-visible:bg-muted/60 active:bg-muted/70"
        >
          <span className="flex size-10 shrink-0 items-center justify-center rounded-xl bg-muted">
            <Repeat className="size-5 text-muted-foreground" />
          </span>
          <span className="min-w-0 flex-1">
            <span className="block font-medium">Recorrências</span>
            <span className="block text-sm text-muted-foreground">{series}</span>
          </span>
          <ChevronRight className="size-4 shrink-0 text-muted-foreground" />
        </Link>
      )}

      <Link
        to="/contas/investimentos"
        className="surface flex items-center gap-3 rounded-2xl px-4 py-3 transition-colors outline-none hover:bg-muted/40 focus-visible:bg-muted/60 active:bg-muted/70"
      >
        <span className="flex size-10 shrink-0 items-center justify-center rounded-xl bg-muted">
          <TrendingUp className="size-5 text-muted-foreground" />
        </span>
        <span className="min-w-0 flex-1">
          <span className="block font-medium">Investimentos</span>
          <span className="block text-sm text-muted-foreground">
            {holdings?.length ? `${holdings.length} ${holdings.length === 1 ? 'ativo' : 'ativos'}` : 'Ações, FIIs e proventos'}
          </span>
        </span>
        <ChevronRight className="size-4 shrink-0 text-muted-foreground" />
      </Link>

      <NewAccountSheet
        open={creating}
        onClose={() => setCreating(false)}
        onCreated={(account) => {
          setCreating(false)
          navigate(`/contas/${account.id}`)
        }}
      />
    </main>
  )
}

// freeCashCents: o caixa livre da conta de investimento que acompanha o caixa (I.9); nulo nas outras e enquanto não chega.
function AccountRow({ account, balance, freeCashCents }: { account: Account; balance: AccountBalance | undefined; freeCashCents: number | null }) {
  // Cartão: fechamento e vencimento, sem repetir "Cartão" (o "Disponível" e o logo já dizem). Curto
  // e com espaço inseparável dentro de cada dado, para a linha quebrar só entre eles em 320px.
  const untracked = account.type === 'Investment' && !account.tracksCash
  const details =
    account.type === 'CreditCard'
      ? [`Fecha\u00a0${account.closingDay}`, `vence\u00a0${account.dueDay}`]
      : [accountTypeLabels[account.type], untracked ? 'caixa não acompanhado' : null].filter(Boolean)

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
        {/* Conta de investimento que não acompanha o caixa não mostra valor: o que está investido é dos ativos (Resumo e Investimentos). */}
        {!untracked && balance && <BalanceFigure account={account} balance={balance} freeCashCents={freeCashCents} />}
        <ChevronRight className="size-4 shrink-0 text-muted-foreground" />
      </Link>
    </li>
  )
}

// Conta: saldo atual. Cartão: limite disponível, ou o que falta pagar quando não há limite. Conta de investimento que acompanha o caixa:
// "Caixa livre", o saldo menos o principal da renda fixa que ela financiou (o saldo da conta, na tela dela, segue batendo com o extrato).
function BalanceFigure({ account, balance, freeCashCents }: { account: Account; balance: AccountBalance; freeCashCents: number | null }) {
  const [label, cents] =
    account.type === 'Investment'
      ? ['Caixa livre', freeCashCents ?? balance.balanceCents]
      : account.type !== 'CreditCard'
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
