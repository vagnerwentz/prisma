import type { Schemas } from '@/lib/api'
import { formatShortDate } from '@/lib/dates'
import type { AssetItem } from './assets'

// "Não encontrou? Avise a gente" (docs/investimentos.md, I.7c3). O pedido é só o código; se o ativo chegou, quem diz é
// a API, pelo catálogo.
export type AssetRequest = Schemas['AssetRequestResponse']

// O código como a API o guarda (AssetRequest.Normalize): sem espaços, em maiúsculas, sem o "F" do fracionário. Nulo
// quando o texto não tem formato de código (busca pelo nome).
export function requestableSymbol(typed: string): string | null {
  const text = typed.replace(/\s+/g, '').toUpperCase()
  const symbol = /^([A-Z0-9]{4}[0-9]{1,2}[A-Z]?)F$/.exec(text)?.[1] ?? text
  return /^[A-Z0-9]{4}[0-9]{1,2}[A-Z]?$/.test(symbol) ? symbol : null
}

// O que a busca sem resultado oferece: avisar que falta, lembrar que já avisou, ou pedir o código.
export type MissingAction =
  | { kind: 'request'; symbol: string }
  | { kind: 'already'; symbol: string; requestedOn: string }
  | { kind: 'hint' }

export function missingAction(typed: string, requests: AssetRequest[]): MissingAction {
  const symbol = requestableSymbol(typed)
  if (!symbol) return { kind: 'hint' }
  const request = requests.find((r) => r.symbol === symbol)
  return request ? { kind: 'already', symbol, requestedOn: request.requestedOn } : { kind: 'request', symbol }
}

// "02/10" no ano corrente; "02/10/2025" em outro.
export function requestedLabel(date: string, today: string): string {
  return date.slice(0, 4) === today.slice(0, 4) ? formatShortDate(date).slice(0, 5) : formatShortDate(date)
}

// O ativo que chegou, como a nota o escolhe.
export function arrivedItem(request: AssetRequest): AssetItem | null {
  const asset = request.asset
  if (request.status !== 'Arrived' || !asset) return null
  return { id: asset.id, symbol: request.symbol, name: asset.name, kind: asset.kind, isActive: asset.isActive, hasLogo: asset.hasLogo }
}
