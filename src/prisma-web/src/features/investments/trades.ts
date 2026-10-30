import type { Schemas } from '@/lib/api'
import { formatShortDate } from '@/lib/dates'
import { formatCents } from '@/lib/money'
import { assetKindLabels, type AssetItem } from './assets'

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

// O comprado e o vendido de várias notas juntas (as notas do ano, I.7e).
export const notesTotals = (notes: { trades: LineValue[] }[]) => noteTotals(notes.flatMap((n) => n.trades))

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

// Corretagem por tipo (docs/investimentos.md, I.7b3), como na API: unit conta como ação.
export const brokerageKindOf = (kind: AssetKind | null): AssetKind => (kind === 'Unit' ? 'Stock' : (kind ?? 'Unknown'))

// O que vem marcado: a regra de antes, só ação.
export const defaultBrokerageKinds: AssetKind[] = ['Stock']

// As opções de "Corretagem cobrada em": os tipos da nota, sem repetir, na ordem em que aparecem.
export function brokerageKindOptions(kinds: (AssetKind | null)[]): AssetKind[] {
  return [...new Set(kinds.map(brokerageKindOf))]
}

export function brokerageKindLabel(kind: AssetKind): string {
  return kind === 'Stock' ? 'Ação' : assetKindLabels[kind]
}

// A escolha aparece com corretagem e algo a decidir: um tipo que não é ação, ou um tipo da nota desmarcado (nota
// editada). Nota só com ações e "Ação" marcada: nada aparece.
export function showsBrokerageKinds(brokerageCents: number, options: AssetKind[], marked: AssetKind[]): boolean {
  return brokerageCents > 0 && (options.some((k) => k !== 'Stock') || options.some((k) => !marked.includes(k)))
}

// A mesma recusa da API, antes de mandar: corretagem sem nenhuma ordem de tipo marcado.
export function brokerageKindsError(brokerageCents: number, options: AssetKind[], marked: AssetKind[]): string | undefined {
  return brokerageCents > 0 && !options.some((k) => marked.includes(k))
    ? 'Marque em quais ativos a corretagem foi cobrada.'
    : undefined
}

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
    case 'LiquidationIn':
      return `Veio de ${step.relatedSymbol}`
    case 'LiquidationOut':
      return `Liquidado em ${step.relatedSymbol}`
    default:
      return `${sideLabels[step.side ?? 'Buy']} · ${unitsLabel(kind, step.quantity)}`
  }
}

// O que o evento fez com a quantidade: "110 → 880 cotas", "880 cotas saíram", "704 cotas entraram". Na liquidação (I.7c4),
// o destino diz o preço: "716 cotas a R$ 10,25" (o preço de referência, que é o custo).
// A segunda linha da liquidação na origem (I.7c4): o que recebeu e o resultado, como numa venda.
export function liquidationLine(step: PositionStep): string | null {
  if (step.kind !== 'LiquidationOut' || step.realizedCents === null) return null
  const result = step.realizedCents < 0 ? `−${formatCents(-step.realizedCents)}` : `+${formatCents(step.realizedCents)}`
  return `recebeu ${formatCents(step.grossCents)} · resultado ${result}`
}

export function eventDetail(step: PositionStep, kind: AssetKind): string {
  switch (step.kind) {
    case 'Split':
      return `${step.quantity.toLocaleString('pt-BR')} → ${unitsLabel(kind, step.quantityAfter)}`
    case 'ConversionOut':
    case 'LiquidationOut':
      return `${unitsLabel(kind, step.quantity)} saíram`
    case 'LiquidationIn':
      return `${unitsLabel(kind, step.quantity)} a ${formatCents(Math.round(step.grossCents / step.quantity))}`
    default:
      return `${unitsLabel(kind, step.quantity)} entraram`
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

// --- Conta da corretora (I.8, docs/investimentos.md): a nota debita (compra) ou credita (venda) o caixa de uma conta ---

type SettlementAccount = { id: string; name: string; type: string; isActive: boolean }

// O que a corretora liquida: compras e custos, menos as vendas. É a mesma conta da API (`netCashCents`), feita aqui para
// mostrar antes de gravar; positivo sai da conta, negativo entra.
export function settlementNetCents(totals: { buyCents: number; sellCents: number }, costsCents: number): number {
  return totals.buyCents - totals.sellCents + costsCents
}

// Cartão não liquida nota; conta inativa só aparece se for a que a nota já tem.
export function settlementAccountChoices<T extends SettlementAccount>(accounts: T[], currentId: string | null): T[] {
  return accounts.filter((a) => a.type !== 'CreditCard' && (a.isActive || a.id === currentId))
}

const plain = (text: string) => text.normalize('NFD').replace(/\p{M}/gu, '').trim().toLowerCase()

// A conta de uma nota nova: a última escolhida (se ainda existe), senão a que tem o nome da corretora, senão nenhuma.
// 'none' é a escolha de não vincular, lembrada como qualquer outra: quem lança notas antigas não precisa desmarcar toda vez.
export function defaultSettlementAccount(choices: SettlementAccount[], broker: string | null, last: string | null): string | null {
  if (last === 'none') return null
  if (last && choices.some((a) => a.id === last)) return last
  const name = broker ? plain(broker) : ''
  return (name && choices.find((a) => plain(a.name) === name)?.id) || null
}

// A frase sob a escolha da conta: o que a nota faz no caixa dela.
export function settlementLine(netCents: number, accountName: string): string {
  if (netCents > 0) return `A compra de ${formatCents(netCents)} sai de ${accountName}.`
  if (netCents < 0) return `A venda de ${formatCents(-netCents)} entra em ${accountName}.`
  return 'Compra e venda se anulam: nada é lançado na conta.'
}

// A conta ficaria negativa? `previousNetCents` é o que a nota já liquidava nesta mesma conta (o saldo atual já o inclui);
// zero para nota nova ou conta diferente. Só a compra pode deixar a conta abaixo de zero. Aviso, nunca bloqueio.
export function goesNegative(balanceCents: number | null, previousNetCents: number, netCents: number): boolean {
  return balanceCents !== null && netCents > 0 && balanceCents + previousNetCents - netCents < 0
}
