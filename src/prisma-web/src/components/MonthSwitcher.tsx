import { ChevronLeft, ChevronRight } from 'lucide-react'
import { useSearchParams } from 'react-router'
import { Button } from '@/components/ui/button'
import { formatMonth, monthOf, shiftMonth, todayInSaoPaulo, type YearMonth } from '@/lib/dates'

// O mês fica na URL (?mes=2025-11): voltar, recarregar e abrir depois de um lançamento
// antigo mostram o mês certo. Resumo e lista usam o mesmo parâmetro.
export function useMonthParam(): [YearMonth, (next: YearMonth) => void] {
  const [searchParams, setSearchParams] = useSearchParams()
  const month = parseMonthParam(searchParams.get('mes')) ?? monthOf(todayInSaoPaulo())
  const setMonth = (next: YearMonth) => setSearchParams({ mes: toMonthParam(next) }, { replace: true })
  return [month, setMonth]
}

export function toMonthParam({ year, month }: YearMonth): string {
  return `${year}-${String(month).padStart(2, '0')}`
}

function parseMonthParam(value: string | null): YearMonth | null {
  const match = value?.match(/^(\d{4})-(\d{2})$/)
  if (!match) return null
  const month = Number(match[2])
  return month >= 1 && month <= 12 ? { year: Number(match[1]), month } : null
}

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
