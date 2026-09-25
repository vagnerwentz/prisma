import { useSearchParams } from 'react-router'
import { monthOf, todayInSaoPaulo, type YearMonth } from '@/lib/dates'

// O mês fica na URL (?mes=2025-11): voltar, recarregar e abrir depois de um lançamento
// antigo mostram o mês certo. Resumo e lista usam o mesmo parâmetro.
export function useMonthParam(): [YearMonth, (next: YearMonth) => void] {
  const [searchParams, setSearchParams] = useSearchParams()
  const month = parseMonthParam(searchParams.get('mes')) ?? monthOf(todayInSaoPaulo())
  // Troca só o mês: outros filtros da URL (a categoria vinda do resumo) continuam.
  const setMonth = (next: YearMonth) =>
    setSearchParams(
      (current) => {
        const params = new URLSearchParams(current)
        params.set('mes', toMonthParam(next))
        return params
      },
      { replace: true },
    )
  return [month, setMonth]
}

// Destino que leva o mês junto (Resumo, Análise e lista, docs/fase-2.md, 4): trocar de tela não
// perde o mês escolhido. Mês ausente ou inválido não vai; a tela abre no mês de hoje.
export function withMonth(path: string, mes: string | null): string {
  return parseMonthParam(mes) ? `${path}?mes=${mes}` : path
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
