import { ChevronLeft, ChevronRight } from 'lucide-react'
import { Button } from '@/components/ui/button'
import { formatMonth, shiftMonth, type YearMonth } from '@/lib/dates'

export function MonthSwitcher({ month, onChange }: { month: YearMonth; onChange: (m: YearMonth) => void }) {
  const [name, year] = formatMonth(month).split(' de ')
  return (
    <div className="flex flex-col gap-3">
      <div className="flex items-end justify-between">
        <h1 className="flex items-baseline gap-2">
          <span className="font-display text-4xl leading-none">{name}</span>
          <span className="text-sm text-muted-foreground tabular-nums">{year}</span>
        </h1>
        <div className="flex gap-1">
          <Button
            variant="ghost"
            size="icon"
            className="rounded-full"
            aria-label="Mês anterior"
            onClick={() => onChange(shiftMonth(month, -1))}
          >
            <ChevronLeft />
          </Button>
          <Button
            variant="ghost"
            size="icon"
            className="rounded-full"
            aria-label="Próximo mês"
            onClick={() => onChange(shiftMonth(month, 1))}
          >
            <ChevronRight />
          </Button>
        </div>
      </div>
      <div className="spectrum-line opacity-80" />
    </div>
  )
}
