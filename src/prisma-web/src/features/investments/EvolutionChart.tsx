import { Area, ComposedChart, Line, ResponsiveContainer, Tooltip, XAxis, YAxis } from 'recharts'
import { formatShortDate } from '@/lib/dates'
import { formatCents } from '@/lib/money'
import type { EvolutionPoint } from './fixedIncome'

// A evolução de um objetivo (docs/renda-fixa.md, 7.2): o que foi aportado em tinta e, por cima, o saldo bruto como
// luz fria. A distância entre as duas é o rendimento. Sem verde nem vermelho.
export default function EvolutionChart({ points }: { points: EvolutionPoint[] }) {
  return (
    <div className="h-44 text-foreground/80" role="img" aria-label="Evolução do saldo bruto e do valor aportado">
      <ResponsiveContainer width="100%" height="100%">
        <ComposedChart data={points} margin={{ top: 6, right: 0, bottom: 0, left: 0 }}>
          <defs>
            <linearGradient id="fi-balance" x1="0" y1="0" x2="0" y2="1">
              <stop offset="0" stopColor="#8b5cf6" stopOpacity={0.55} />
              <stop offset="1" stopColor="#22d3ee" stopOpacity={0.08} />
            </linearGradient>
          </defs>
          <XAxis
            dataKey="date"
            axisLine={false}
            tickLine={false}
            minTickGap={48}
            tickFormatter={(d: string) => formatShortDate(d).slice(0, 5)}
            tick={{ fill: 'currentColor', fontSize: 11, opacity: 0.7 }}
          />
          <YAxis hide domain={['dataMin', 'dataMax']} />
          <Tooltip cursor={{ stroke: 'currentColor', strokeOpacity: 0.2 }} content={<PointTooltip />} />
          <Area
            type="monotone"
            dataKey="grossBalanceCents"
            stroke="#3b82f6"
            strokeWidth={1.5}
            fill="url(#fi-balance)"
            isAnimationActive={false}
          />
          <Line
            type="stepAfter"
            dataKey="principalCents"
            stroke="currentColor"
            strokeWidth={1.5}
            dot={false}
            isAnimationActive={false}
          />
        </ComposedChart>
      </ResponsiveContainer>
    </div>
  )
}

function PointTooltip({ active, payload }: { active?: boolean; payload?: { payload: EvolutionPoint }[] }) {
  const point = payload?.[0]?.payload
  if (!active || !point) return null
  return (
    <div className="surface flex flex-col gap-1 rounded-xl px-3 py-2 text-xs text-foreground">
      <span className="font-medium">{formatShortDate(point.date)}</span>
      <span className="tabular-nums">Aportado {formatCents(point.principalCents)}</span>
      <span className="tabular-nums">Saldo bruto {formatCents(point.grossBalanceCents)}</span>
      <span className="text-spectrum tabular-nums">Rendimento {formatCents(point.grossYieldCents)}</span>
    </div>
  )
}
