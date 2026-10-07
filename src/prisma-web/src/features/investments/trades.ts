import type { Schemas } from '@/lib/api'
import { formatShortDate } from '@/lib/dates'
import { formatCents } from '@/lib/money'
import type { AssetItem } from './assets'

// Notas de corretagem e carteira (docs/investimentos.md, I.7b2): textos e somas da tela. O rateio dos custos e os
// preços médios vêm prontos da API; a tela não refaz a conta.
export type TradeNote = Schemas['TradeNoteResponse']
export type Position = Schemas['ListPositionsPositionResponse']
export type PositionStep = Schemas['ListPositionsStepResponse']
export type TradeSide = Schemas['TradeSide']
export type TradeNoteRequest = Schemas['SaveTradeNoteRequest']
type AssetKind = NonNullable<Schemas['AssetKind']>

// Corretoras oferecidas na nota; o ícone sai do reconhecimento de marca (findBrand). "Outra" deixa digitar.
export const brokers = [
  'Íon',
  'XP',
  'BTG Pactual',
  'Rico',
  'Clear',
  'Nubank',
  'Inter',
  'Toro',
  'Ágora',
  'Itaú',
  'Banco do Brasil',
  'Bradesco',
  'Santander',
  'Caixa',
  'Safra',
  'Modalmais',
  'Genial',
  'C6 Bank',
]

// A última corretora usada vem primeiro (e já escolhida), mesmo que tenha sido digitada.
export function brokerChoices(lastUsed: string | null): string[] {
  return lastUsed ? [lastUsed, ...brokers.filter((b) => b !== lastUsed)] : brokers
}

const units: Record<AssetKind, [string, string]> = {
  Stock: ['ação', 'ações'],
  Unit: ['ação', 'ações'],
  Bdr: ['BDR', 'BDRs'],
  Fii: ['cota', 'cotas'],
  Etf: ['cota', 'cotas'],
  FiInfra: ['cota', 'cotas'],
  FiAgro: ['cota', 'cotas'],
  Fip: ['cota', 'cotas'],
  Fidc: ['cota', 'cotas'],
  OtherFund: ['cota', 'cotas'],
  Unknown: ['unidade', 'unidades'],
}

// "80 ações", "1 cota", "5 BDRs".
export function unitsLabel(kind: AssetKind, quantity: number): string {
  const [one, many] = units[kind]
  return `${quantity.toLocaleString('pt-BR')} ${quantity === 1 ? one : many}`
}

// Os dois preços médios: o que se compara com a cotação e o da Receita. Em duas partes, para a linha quebrar
// entre elas e nunca no meio de um valor (320 px).
export function priceParts(p: { averagePriceCents: number | null; averageCostCents: number | null }): [string, string] | null {
  if (p.averagePriceCents === null || p.averageCostCents === null) return null
  return [`PM ${formatCents(p.averagePriceCents)}`, `com custos ${formatCents(p.averageCostCents)}`]
}

export const sideLabels: Record<TradeSide, string> = { Buy: 'Compra', Sell: 'Venda' }

type LineValue = { side: TradeSide; quantity: number; unitPriceCents: number }

export function noteTotals(lines: LineValue[]): { buyCents: number; sellCents: number } {
  let buyCents = 0
  let sellCents = 0
  for (const l of lines) {
    if (l.side === 'Buy') buyCents += l.quantity * l.unitPriceCents
    else sellCents += l.quantity * l.unitPriceCents
  }
  return { buyCents, sellCents }
}

// "Compra · ITUB3, BBSE3", "Compra e venda · …": o que a nota fez e com quais ativos.
export function noteTitle(lines: { side: TradeSide; symbol: string }[]): string {
  const sides = new Set(lines.map((l) => l.side))
  const action = sides.size === 2 ? 'Compra e venda' : sideLabels[lines[0]?.side ?? 'Buy']
  const symbols = [...new Set(lines.map((l) => l.symbol))]
  return `${action} · ${symbols.join(', ')}`
}

// O mesmo teto da API (SaveTradeNote): 100 milhões de unidades.
const maxQuantity = 100_000_000

export function parseQuantity(text: string): number {
  const digits = text.replace(/\D/g, '').slice(0, 10)
  return digits === '' ? 0 : Math.min(Number(digits), maxQuantity)
}

// A operação sendo digitada no mini-form, antes de entrar na nota.
export type DraftLine = { asset: AssetItem | null; side: TradeSide; quantity: number; unitPriceCents: number }

export type DraftErrors = Partial<Record<'asset' | 'quantity' | 'price', string>>

export function draftErrors(draft: DraftLine): DraftErrors {
  const errors: DraftErrors = {}
  if (!draft.asset) errors.asset = 'Escolha o ativo.'
  if (draft.quantity <= 0) errors.quantity = 'Digite a quantidade.'
  if (draft.unitPriceCents <= 0) errors.price = 'Digite o preço.'
  return errors
}

// Ação e unit pagam corretagem; para os outros tipos, a nota precisa da marcação (I.7a).
export const isStock = (kind: AssetKind | null) => kind === 'Stock' || kind === 'Unit'

// Um passo do histórico do ativo: uma ordem da nota ou um evento da empresa (I.7c1).
export function stepTitle(step: PositionStep, kind: AssetKind): string {
  switch (step.kind) {
    case 'Split': {
      const from = step.fromQuantity ?? 1
      const to = step.toQuantity ?? 1
      return `${to >= from ? 'Desdobramento' : 'Grupamento'} ${from}:${to}`
    }
    case 'ConversionOut':
      return `Virou ${step.relatedSymbol}`
    case 'ConversionIn':
      return `Veio de ${step.relatedSymbol}`
    default:
      return `${sideLabels[step.side ?? 'Buy']} · ${unitsLabel(kind, step.quantity)}`
  }
}

// Na busca: o ativo que virou outro diz o quê e quando; o que só saiu da bolsa, só isso.
export function assetStatus(asset: {
  isActive: boolean
  convertedToSymbol?: string | null
  convertedOn?: string | null
}): string | null {
  if (asset.convertedToSymbol && asset.convertedOn)
    return `virou ${asset.convertedToSymbol} em ${formatShortDate(asset.convertedOn)}`
  return asset.isActive ? null : 'saiu da bolsa'
}

type HoldingLike = {
  id: string
  assetId: string
  symbol: string
  name: string
  kind: Schemas['AssetKind']
  isActive: boolean
  addedAt: string
  hasLogo: boolean
}
type PositionLike = {
  assetId: string
  symbol: string
  name: string
  kind: Schemas['AssetKind']
  hasLogo: boolean
  isActive: boolean
  quantity: number
}

// "Meus ativos": a carteira e, junto, o ativo com posição que nunca entrou nela (o recebido numa conversão). Esse não
// tem registro de carteira (fromPositionOnly): não se tira, porque tem posição.
export function withPositions<H extends HoldingLike>(
  holdings: H[],
  positions: PositionLike[],
): (H | (HoldingLike & { fromPositionOnly: true }))[] {
  const held = new Set(holdings.map((h) => h.assetId))
  const extra = positions
    .filter((p) => p.quantity > 0 && !held.has(p.assetId))
    .map((p) => ({
      id: `position-${p.assetId}`,
      assetId: p.assetId,
      symbol: p.symbol,
      name: p.name,
      kind: p.kind,
      isActive: p.isActive,
      addedAt: '',
      hasLogo: p.hasLogo,
      fromPositionOnly: true as const,
    }))
  return [...holdings, ...extra].sort((a, b) => (a.symbol < b.symbol ? -1 : a.symbol > b.symbol ? 1 : 0))
}
