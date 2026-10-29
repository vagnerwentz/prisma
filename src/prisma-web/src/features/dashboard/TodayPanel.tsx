import { ChevronRight, Wallet } from 'lucide-react'
import { Link } from 'react-router'
import { AccountTile } from '@/components/brand/Tiles'
import { Skeleton } from '@/components/ui/skeleton'
import { useAccounts } from '@/features/accounts/queries'
import { formatMonth, monthOf, todayInSaoPaulo } from '@/lib/dates'
import { formatCents } from '@/lib/money'
import { cn } from '@/lib/utils'
import { hasInvestments, investedNote, tracksAnyCash, type NetWorth } from './netWorth'
import { useNetWorth, useUpcomingStatements, type UpcomingStatement } from './queries'
import { describeDue } from './upcoming'

// "Em contas" e "Próximas faturas" (docs/fase-2.md, 2.4). Olham para hoje, não para o mês escolhido
// no Resumo. Carregado sob demanda: os ladrilhos de conta trazem os logos de marca.
export default function TodayPanel() {
  const upcoming = useUpcomingStatements()
  const accounts = useAccounts()
  // O patrimônio de hoje (I.9): o Investido vem dos ativos, com ou sem histórico de caixa (docs/patrimonio.md).
  const netWorth = useNetWorth()

  if (upcoming.isPending || accounts.isPending || netWorth.isPending) return <Skeleton className="h-40 w-full rounded-2xl" />
  if (upcoming.isError || accounts.isError || netWorth.isError) return null

  const net = netWorth.data
  const hasCards = accounts.data.some((a) => a.isActive && a.type === 'CreditCard')
  // Sem conta corrente, carteira nem investimento, o bloco não aparece.
  const inAccounts = net.availableCount > 0 || hasInvestments(net)
  if (!inAccounts && !hasCards) return null

  return (
    <section className="flex flex-col gap-3">
      <h2 className="px-1 text-sm font-medium text-foreground/75">Hoje</h2>
      <div
        className={cn(
          'grid grid-cols-1 gap-3',
          inAccounts && hasCards && 'sm:grid-cols-[minmax(0,2fr)_minmax(0,3fr)] sm:items-start',
        )}
      >
        {inAccounts && <InAccounts net={net} />}
        {hasCards && <UpcomingStatements statements={upcoming.data.statements} totalCents={upcoming.data.totalCents} />}
      </div>
    </section>
  )
}

function InAccounts({ net }: { net: NetWorth }) {
  // Sem renda fixa, ação nem conta de investimento, é o saldo de sempre. Com elas, o dinheiro livre e o investido aparecem separados.
  if (!hasInvestments(net)) {
    return (
      <Link to="/contas" className={cardClass}>
        <WalletIcon />
        <div className="flex min-w-0 flex-1 flex-col gap-0.5">
          <span className="text-sm font-medium text-foreground/75">Em contas</span>
          <span className="text-xs text-muted-foreground">
            Saldo de hoje{net.availableCount > 1 ? `, em ${net.availableCount} contas` : ''}
          </span>
        </div>
        <Figure cents={net.availableCents} />
      </Link>
    )
  }

  return (
    <Link to="/contas" className={cardClass}>
      <WalletIcon />
      <div className="flex min-w-0 flex-1 flex-col gap-3 sm:w-full">
        <Line
          title="Disponível"
          note={net.availableCount > 0 ? 'Conta corrente e carteira' : 'Nenhuma conta corrente ou carteira'}
          cents={net.availableCents}
        />
        <Line title="Investido" note={investedNote(net)} cents={net.investedCents} divided />
        {/* Só de quem acompanha o caixa de uma conta de investimento (docs/patrimonio.md, regra 1). */}
        {tracksAnyCash(net) && <Line title="Caixa nas corretoras" note="Parado, ainda não investido" cents={net.brokerCashCents} divided />}
      </div>
    </Link>
  )
}

function Line({ title, note, cents, divided }: { title: string; note: string; cents: number; divided?: boolean }) {
  return (
    <div className={cn('flex items-center justify-between gap-3', divided && 'border-t border-border/60 pt-3')}>
      <div className="flex min-w-0 flex-col gap-0.5">
        <span className="text-sm font-medium text-foreground/75">{title}</span>
        <span className="text-xs text-muted-foreground">{note}</span>
      </div>
      <Figure cents={cents} />
    </div>
  )
}

const cardClass =
  'surface flex items-center gap-3 rounded-2xl p-4 transition-colors hover:bg-muted/40 active:bg-muted/70 sm:flex-col sm:items-start'

function WalletIcon() {
  return (
    <span className="flex size-10 shrink-0 items-center justify-center rounded-full bg-muted text-foreground [&_svg]:size-5">
      <Wallet />
    </span>
  )
}

// Valor que não pode ser cortado: o texto ao lado quebra de linha, o número não.
function Figure({ cents }: { cents: number }) {
  return (
    <span className="shrink-0 text-[clamp(1.05rem,4.8vw,1.35rem)] leading-tight font-semibold tracking-tight tabular-nums">
      {formatCents(cents).replace('-', '−')}
    </span>
  )
}

function UpcomingStatements({ statements, totalCents }: { statements: UpcomingStatement[]; totalCents: number }) {
  const today = todayInSaoPaulo()

  return (
    <div className="surface flex flex-col rounded-2xl py-1.5">
      <div className="flex items-baseline justify-between gap-3 px-4 pt-2.5 pb-1.5">
        <span className="text-sm font-medium text-foreground/75">Próximas faturas</span>
        {statements.length > 1 && (
          <span className="text-sm font-semibold tabular-nums">
            <span className="font-normal text-muted-foreground">Total </span>
            {formatCents(totalCents)}
          </span>
        )}
      </div>
      {statements.length === 0 ? (
        <p className="px-4 pt-1 pb-3 text-sm text-muted-foreground">Nenhuma fatura a pagar.</p>
      ) : (
        <ul className="flex flex-col">
          {statements.map((s) => (
            <Row key={s.statementId} statement={s} today={today} />
          ))}
        </ul>
      )}
    </div>
  )
}

function Row({ statement, today }: { statement: UpcomingStatement; today: string }) {
  const month = formatMonth(monthOf(statement.dueDate)).split(' de ')[0].toLowerCase()
  const due = describeDue(statement.dueDate, today)
  const soon = due === 'Vence hoje' || due === 'Vence amanhã'

  return (
    <li>
      <Link
        to={`/contas/${statement.accountId}`}
        className="flex items-center gap-3 px-4 py-2.5 transition-colors outline-none hover:bg-muted/40 focus-visible:bg-muted/60 active:bg-muted/70"
      >
        <AccountTile name={statement.cardName} type="CreditCard" />
        <div className="flex min-w-0 flex-1 flex-col">
          <span className="truncate text-sm font-medium">{statement.cardName}</span>
          <span className="truncate text-xs text-muted-foreground">
            {/* O vencimento vem primeiro: no celular estreito, o que sobra cortado é o mês. */}
            <span className={cn(soon && 'font-medium text-foreground')}>{due}</span> · fatura de {month}
          </span>
        </div>
        <span className="shrink-0 text-sm font-semibold tabular-nums">{formatCents(statement.totalCents)}</span>
        <ChevronRight className="size-4 shrink-0 text-muted-foreground" />
      </Link>
    </li>
  )
}
