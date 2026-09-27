import { api, ApiError } from './api'
import { crashKind } from './crash'

// Erros do navegador relatados ao servidor (etapa H.3b), que os registra com o userId da sessão:
// "crash" é a tela que quebrou (ErrorBoundary); "silent", o erro fora da montagem da tela (clique,
// promessa) que antes sumia sem ninguém saber. Só mensagem, pilha, tela, tipo e versão: nunca o que
// estava na tela.
export type ClientErrorKind = 'crash' | 'silent'

export type ClientErrorReport = {
  message: string
  stack: string | null
  screen: string
  kind: ClientErrorKind
  appVersion: string
}

// Limites da API (ReportClientError): o excesso é cortado aqui, não recusado lá.
const maxMessage = 2000
const maxStack = 8000

// Sem conexão não é bug, e o relato nem chegaria.
const networkMessages = ['Failed to fetch', 'Load failed', 'NetworkError when attempting to fetch resource']

export function isReportable(error: unknown): boolean {
  if (error instanceof ApiError) return false // o servidor já registrou a resposta de erro
  if (crashKind(error) === 'update') return false // versão nova publicada com o app aberto
  const message = messageOf(error)
  return !networkMessages.some((text) => message.startsWith(text))
}

export function buildReport(error: unknown, kind: ClientErrorKind, pathname: string, appVersion: string): ClientErrorReport {
  return {
    message: messageOf(error).slice(0, maxMessage),
    stack: error instanceof Error && error.stack ? error.stack.slice(0, maxStack) : null,
    // Só o caminho: a consulta (?estorno=…) e o fragmento podem levar dados.
    screen: pathname.split(/[?#]/)[0] || '/',
    kind,
    appVersion,
  }
}

// Um valor qualquer rejeitado numa promessa pode levar dados da tela: vai só o tipo dele.
function messageOf(error: unknown): string {
  if (error instanceof Error) return error.message || error.name
  if (typeof error === 'string') return error
  return `Non-error value (${typeof error})`
}

// Cada erro uma vez por carregamento da página, e o envio que falha não vira outro erro. Devolve o
// envio já tratado (nunca rejeita); quem relata não precisa esperar.
export function createReporter(
  send: (report: ClientErrorReport) => Promise<unknown>,
  currentPath: () => string,
  appVersion: string,
): (error: unknown, kind: ClientErrorKind) => Promise<void> {
  const seen = new Set<string>()
  return async (error, kind) => {
    if (!isReportable(error)) return
    const report = buildReport(error, kind, currentPath(), appVersion)
    const key = `${report.message}|${report.stack?.split('\n')[1] ?? ''}`
    if (seen.has(key)) return
    seen.add(key)
    await send(report).catch(() => {})
  }
}

export const reportClientError = createReporter(
  (report) => api.POST('/client-errors', { body: report }),
  () => window.location.pathname,
  __APP_VERSION__,
)
