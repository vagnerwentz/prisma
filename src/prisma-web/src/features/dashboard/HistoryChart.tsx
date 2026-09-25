import { Bar, BarChart, Cell, ResponsiveContainer, Tooltip, XAxis } from 'recharts'
import { Skeleton } from '@/components/ui/skeleton'
import { formatMonth, type YearMonth } from '@/lib/dates'
import { formatCents } from '@/lib/money'
import { describeExpenseChange, expenseChange } from './history'
import { useMonthlyHistory, type MonthlySummary } from './queries'

type Point = { month: YearMonth; label: string; name: string; income: number; expense: number; current: boolean }

// Comparativo dos 6 meses que terminam no escolhido (docs/fase-2.md, 2.3): receitas em luz fria,
// despesas em tinta. O mês escolhido em destaque; tocar numa barra abre aquele mês.
export default function HistoryChart({ month, onSelect }: { month: string; onSelect: (month: YearMonth) => void }) {
  const history = useMonthlyHistory(month)

  if (history.isPending) return <Skeleton className="h-64 w-full rounded-2xl" />
  if (history.isError) return null

  const points = history.data.map((m) => toPoint(m, month))
  const [previous, current] = history.data.slice(-2)
  const change = previous && current ? expenseChange(current.expenseCents, previous.expenseCents) : null
  const previousName = points.at(-2)?.name.toLowerCase() ?? ''

  return (
    <section className="flex flex-col gap-3">
      <h2 className="px-1 text-sm font-medium text-foreground/75">Últimos 6 meses</h2>
      <div className="surface flex flex-col gap-4 rounded-2xl p-4">
        <div className="flex flex-wrap items-center justify-between gap-x-4 gap-y-2">
          <p className="text-sm font-medium">
            {change ? describeExpenseChange(change, previousName) : `Sem gastos em ${previousName} para comparar`}
          </p>
          <div className="flex items-center gap-3 text-xs text-muted-foreground">
            <Legend swatch="bg-[linear-gradient(#8b5cf6,#22d3ee)]" label="Entrou" />
            <Legend swatch="bg-foreground/80" label="Saiu" />
          </div>
        </div>
        {/* As barras de despesa usam currentColor: a cor do texto, que segue o tema. */}
        <div className="h-44 text-foreground/80">
          <ResponsiveContainer width="100%" height="100%">
            <BarChart data={points} barGap={3} margin={{ top: 4, right: 0, bottom: 0, left: 0 }}>
              <defs>
                <linearGradient id="income-bar" x1="0" y1="0" x2="0" y2="1">
                  <stop offset="0" stopColor="#8b5cf6" />
                  <stop offset="1" stopColor="#22d3ee" />
                </linearGradient>
              </defs>
              <XAxis
                dataKey="label"
                axisLine={false}
                tickLine={false}
                interval={0}
                tick={{ fill: 'currentColor', fontSize: 12, opacity: 0.7 }}
              />
              <Tooltip cursor={{ fill: 'currentColor', opacity: 0.06 }} content={<MonthTooltip />} />
              <Bar dataKey="income" radius={[5, 5, 0, 0]} maxBarSize={18} onClick={(_, i) => onSelect(points[i].month)}>
                {points.map((p) => (
                  <Cell key={p.label} fill="url(#income-bar)" fillOpacity={p.current ? 1 : 0.4} className="cursor-pointer" />
                ))}
              </Bar>
              <Bar dataKey="expense" radius={[5, 5, 0, 0]} maxBarSize={18} onClick={(_, i) => onSelect(points[i].month)}>
                {points.map((p) => (
                  <Cell key={p.label} fill="currentColor" fillOpacity={p.current ? 1 : 0.35} className="cursor-pointer" />
                ))}
              </Bar>
            </BarChart>
          </ResponsiveContainer>
        </div>
      </div>
    </section>
  )
}

function toPoint(summary: MonthlySummary, selected: string): Point {
  const [year, number] = summary.month.split('-').map(Number)
  const month = { year, month: number }
  const name = formatMonth(month).split(' de ')[0]
  return {
    month,
    label: name.slice(0, 3).toLowerCase(),
    name,
    income: summary.incomeCents,
    expense: summary.expenseCents,
    current: summary.month === selected,
  }
}

function Legend({ swatch, label }: { swatch: string; label: string }) {
  return (
    <span className="flex items-center gap-1.5">
      <span className={`size-2.5 rounded-full ${swatch}`} />
      {label}
    </span>
  )
}

function MonthTooltip({ active, payload }: { active?: boolean; payload?: { payload: Point }[] }) {
  const point = payload?.[0]?.payload
  if (!active || !point) return null
  return (
    <div className="surface flex flex-col gap-1 rounded-xl px-3 py-2 text-xs text-foreground">
      <span className="font-medium">
        {point.name} de {point.month.year}
      </span>
      <span className="tabular-nums">Entrou {formatCents(point.income)}</span>
      <span className="tabular-nums">Saiu {formatCents(point.expense)}</span>
    </div>
  )
}
