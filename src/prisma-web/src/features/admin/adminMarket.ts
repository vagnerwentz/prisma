import type { ApiError, Schemas } from '@/lib/api'
import { addDays, formatShortDate, todayInSaoPaulo } from '@/lib/dates'

// Tela de admin dos dados de mercado (docs/admin-mercado.md, seção 7): rótulos, estados e textos de cada situação. Só leitura do que
// a API devolveu; quem decide é o servidor.
export type MarketSeries = Schemas['MarketSeriesStatus']
export type MarketRun = Schemas['MarketSyncRunResponse']
export type MarketOutcome = MarketRun['outcome']
export type MarketKind = MarketRun['kind']

export type SeriesState = 'empty' | 'stale' | 'incomplete' | 'ok'

// Do pior para o melhor: sem taxas, defasado (a última é anterior à esperada), incompleto (falta dia útil no meio), em dia.
export function seriesState(series: Pick<MarketSeries, 'count' | 'stale' | 'complete'>): SeriesState {
  if (series.count === 0) return 'empty'
  if (series.stale) return 'stale'
  if (!series.complete) return 'incomplete'
  return 'ok'
}

export const stateLabels: Record<SeriesState, string> = {
  empty: 'Sem taxas',
  stale: 'Defasado',
  incomplete: 'Incompleto',
  ok: 'Em dia',
}

export function stateDescription(series: MarketSeries): string {
  switch (seriesState(series)) {
    case 'empty':
      return 'Ainda não há nenhuma taxa gravada.'
    case 'stale':
      return `A última taxa é de ${formatShortDate(series.last!)}; já deveria haver a de ${formatShortDate(series.expectedLast)}.`
    case 'incomplete':
      return series.missingBusinessDayCount === 1
        ? 'Falta a taxa de 1 dia útil no meio do histórico.'
        : `Faltam as taxas de ${series.missingBusinessDayCount} dias úteis no meio do histórico.`
    case 'ok':
      return `A última taxa é de ${formatShortDate(series.last!)}.`
  }
}

export const outcomeLabels: Record<MarketOutcome, string> = {
  Succeeded: 'Deu certo',
  SourceUnavailable: 'Banco Central indisponível',
  Failed: 'Falhou',
  TimedOut: 'Passou do tempo',
}

export const kindLabels: Record<MarketKind, string> = {
  Scheduled: 'Horário automático',
  Startup: 'Ao subir a API',
  Manual: 'Manual',
}

export function formatWindow(from: string, to: string): string {
  return from === to ? formatShortDate(from) : `${formatShortDate(from)} a ${formatShortDate(to)}`
}

const instantFormat = new Intl.DateTimeFormat('pt-BR', {
  timeZone: 'America/Sao_Paulo',
  day: '2-digit',
  month: '2-digit',
  year: 'numeric',
  hour: '2-digit',
  minute: '2-digit',
  hourCycle: 'h23',
})

// "2026-10-19T22:30:00Z" → "19/10/2026 19:30" (São Paulo).
export function formatInstant(iso: string): string {
  return instantFormat.format(new Date(iso)).replace(', ', ' ')
}

function timeOf(iso: string): string {
  const [hour, minute] = formatInstant(iso).split(' ')[1].split(':')
  return minute === '00' ? `${Number(hour)}h` : `${hour}h${minute}`
}

// "hoje às 19h30", "amanhã às 19h30" ou "21/10 às 19h30", no horário de São Paulo.
export function nextRunText(schedule: MarketSeries['schedule'], now: Date = new Date()): string {
  if (!schedule.enabled) return 'A execução automática está desligada.'
  const day = todayInSaoPaulo(new Date(schedule.nextRunUtc))
  const today = todayInSaoPaulo(now)
  const when = day === today ? 'hoje' : day === addDays(today, 1) ? 'amanhã' : formatShortDate(day).slice(0, 5)
  return `Próxima execução automática: ${when} às ${timeOf(schedule.nextRunUtc)}.`
}

export function scheduleText(schedule: MarketSeries['schedule']): string {
  return `Roda todo dia às ${schedule.runTimes.map((time) => time.replace(':', 'h').replace(/h00$/, 'h')).join(' e ')} (São Paulo) e ao subir a API, se o histórico estiver vazio ou atrasado.`
}

// O que a execução fez, em uma linha. Quando falhou, o motivo e, se o Banco Central mandou, o número que ele pede num chamado.
export function runSummary(run: MarketRun): string {
  if (run.outcome === 'Succeeded') {
    const parts = [`${run.fetched} lidas`, `${run.added} novas`]
    if (run.revised > 0) parts.push(run.revised === 1 ? '1 revisada' : `${run.revised} revisadas`)
    return parts.join(' · ')
  }
  const reason = run.errorSummary ?? outcomeLabels[run.outcome]
  return run.sourceErrorCode ? `${reason} Código de erro do Banco Central: ${run.sourceErrorCode}.` : reason
}

export function isFailure(run: Pick<MarketRun, 'outcome'>): boolean {
  return run.outcome !== 'Succeeded'
}

// O aviso depois de um disparo: o que foi feito, e se deu certo.
export function runToast(run: MarketRun): string {
  return run.outcome === 'Succeeded'
    ? run.added === 0 && run.revised === 0
      ? 'Sincronizado: nenhuma taxa nova.'
      : `Sincronizado: ${run.added} novas, ${run.revised} revisadas.`
    : runSummary(run)
}

// Erro do disparo: o texto do servidor (fixo por desfecho) e o código do Banco Central, quando veio.
export function syncErrorMessage(error: unknown): string {
  const api = error as Partial<ApiError> | null
  const problem = api?.problem as { sourceErrorCode?: string | null } | undefined
  const message = api?.message ?? 'Não foi possível concluir. Tente novamente.'
  return problem?.sourceErrorCode ? `${message} Código de erro do Banco Central: ${problem.sourceErrorCode}.` : message
}

// Os mesmos limites do servidor (SyncWindow), para avisar antes de enviar. O servidor continua sendo quem decide.
export const maxWindowYears = 10

export function windowProblem(from: string, to: string, today: string): string | null {
  if (!from || !to) return 'Escolha as duas datas.'
  if (from > to) return 'A data inicial não pode ser depois da final.'
  if (to > today) return 'A data final não pode estar no futuro.'
  // Comparar o texto basta: 29/02 + 10 anos em ano comum vira "…-02-29", que fica entre 28/02 e 01/03, como o servidor leva ao fim do mês.
  if (to > `${Number(from.slice(0, 4)) + maxWindowYears}${from.slice(4)}`)
    return `O período pode ter no máximo ${maxWindowYears} anos.`
  return null
}

// A janela que a tela sugere: os últimos 30 dias.
export function suggestedWindow(today: string): { from: string; to: string } {
  return { from: addDays(today, -30), to: today }
}
