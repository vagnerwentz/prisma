import type { Schemas } from '@/lib/api'

// A carteira em um ano (docs/investimentos.md, I.7e): grupos, pesos, o espectro e o endereço da tela.
export type Portfolio = Schemas['GetPortfolioPortfolioResponse']
export type PortfolioAsset = Portfolio['assets'][number]
export type PortfolioSlice = Portfolio['groups'][number]
export type PortfolioGroup = Schemas['PortfolioGroup']
// A renda fixa da carteira (R.9): uma linha por objetivo; Name nulo é "Sem objetivo".
export type FixedIncomeLine = Portfolio['fixedIncome'][number]
type AssetKind = Schemas['AssetKind']

// Os mesmos grupos do domínio (PortfolioGroups): unit com as ações, os fundos menores juntos.
export function groupOf(kind: AssetKind | null | undefined): PortfolioGroup {
  switch (kind) {
    case 'Stock':
    case 'Unit':
      return 'Stocks'
    case 'Fii':
      return 'Fiis'
    case 'Etf':
      return 'Etfs'
    case 'Bdr':
      return 'Bdrs'
    case 'FiInfra':
    case 'FiAgro':
    case 'Fip':
    case 'Fidc':
    case 'OtherFund':
      return 'OtherFunds'
    default:
      return 'Other'
  }
}

export const groupOrder: PortfolioGroup[] = ['Stocks', 'Fiis', 'Etfs', 'Bdrs', 'OtherFunds', 'FixedIncome', 'Other']

export const groupLabels: Record<PortfolioGroup, string> = {
  Stocks: 'Ações',
  Fiis: 'FIIs',
  Etfs: 'ETFs',
  Bdrs: 'BDRs',
  OtherFunds: 'Outros fundos',
  FixedIncome: 'Renda fixa',
  Other: 'Outros',
}

// Uma faixa fixa do espectro por grupo: a mesma no espectro, na legenda e na barra de cada ativo.
export const groupColors: Record<PortfolioGroup, string> = {
  Fiis: '#8b5cf6',
  Stocks: '#3b82f6',
  Etfs: '#22d3ee',
  OtherFunds: '#84cc16',
  FixedIncome: '#64748b',
  Bdrs: '#f59e0b',
  Other: '#f43f5e',
}

// 6000 → "60%", 3333 → "33,3%". Abaixo de 0,1% que não é zero: "< 0,1%", para não ler como nada.
export function formatWeight(basisPoints: number): string {
  if (basisPoints > 0 && basisPoints < 5) return '< 0,1%'
  return `${(basisPoints / 100).toLocaleString('pt-BR', { maximumFractionDigits: 1 })}%`
}

// "1 ativo", "3 ativos".
export const assetCountLabel = (count: number) => `${count} ${count === 1 ? 'ativo' : 'ativos'}`

// Na renda fixa o que se conta são os objetivos: "1 objetivo", "3 objetivos".
export const groupCountLabel = (group: PortfolioGroup, count: number) =>
  group === 'FixedIncome' ? `${count} ${count === 1 ? 'objetivo' : 'objetivos'}` : assetCountLabel(count)

export const looseGoalName = 'Sem objetivo'

export function fixedIncomeName(line: FixedIncomeLine): string {
  return line.name ?? looseGoalName
}

// O endereço do objetivo na aba Objetivos: o id, ou "sem" para os aportes soltos (mesmo parâmetro de GoalsTab).
export function fixedIncomeGoalParam(line: FixedIncomeLine): string {
  return line.goalId ?? 'sem'
}

// O rendimento bruto da renda fixa toda, somado das linhas.
export function fixedIncomeYield(lines: FixedIncomeLine[]): number {
  return lines.reduce((sum, l) => sum + l.grossYieldCents, 0)
}

export type Band = { group: PortfolioGroup; y0: number; y1: number }

// Onde cada faixa do espectro chega na borda direita: de cima para baixo, na ordem dos grupos (do maior para o menor),
// com um vão entre elas. A espessura é proporcional ao custo, e nenhuma fica abaixo da mínima: o grupo pequeno não
// some. O que as pequenas ganham sai das grandes, na proporção delas. Só grupos com custo.
export function prismBands(groups: PortfolioSlice[], height: number, gap: number, minimum: number): Band[] {
  const slices = groups.filter((g) => g.investedCents > 0)
  if (slices.length === 0) return []
  const available = height - gap * (slices.length - 1)
  // Grupos demais para a altura: a mínima cede, e todos ficam iguais no pior caso.
  const minThickness = Math.min(minimum, available / slices.length)
  const total = slices.reduce((sum, g) => sum + g.investedCents, 0)

  let thickness = slices.map((g) => (available * g.investedCents) / total)
  const pinned = new Set<number>()
  // Cada volta fixa quem ficou abaixo da mínima e redistribui o resto; termina em no máximo uma volta por grupo.
  for (;;) {
    const below = thickness.findIndex((t, i) => !pinned.has(i) && t < minThickness)
    if (below === -1) break
    pinned.add(below)
    const free = available - pinned.size * minThickness
    const freeTotal = slices.reduce((sum, g, i) => (pinned.has(i) ? sum : sum + g.investedCents), 0)
    thickness = slices.map((g, i) => (pinned.has(i) ? minThickness : freeTotal === 0 ? 0 : (free * g.investedCents) / freeTotal))
  }

  let y = 0
  return slices.map((g, i) => {
    const band = { group: g.group, y0: y, y1: y + thickness[i] }
    y = band.y1 + gap
    return band
  })
}

export type Tab = 'carteira' | 'proventos' | 'notas' | 'objetivos'
const tabs: Tab[] = ['carteira', 'proventos', 'notas', 'objetivos']

// O endereço guarda a aba e o ano (?aba=proventos&ano=2020). Sem aba: a carteira; sem ano, ou ano inválido: o corrente.
export function readView(search: URLSearchParams, currentYear: number): { tab: Tab; year: number } {
  const tab = tabs.find((t) => t === search.get('aba')) ?? 'carteira'
  const raw = search.get('ano')
  const year = raw && /^\d{4}$/.test(raw) && Number(raw) <= currentYear ? Number(raw) : currentYear
  return { tab, year }
}

// O inverso: só o que difere do padrão vai para o endereço.
export function viewSearch(tab: Tab, year: number, currentYear: number, goal: string | null = null): string {
  const params = new URLSearchParams()
  if (tab !== 'carteira') params.set('aba', tab)
  if (year !== currentYear) params.set('ano', String(year))
  if (tab === 'objetivos' && goal) params.set('objetivo', goal)
  const text = params.toString()
  return text ? `?${text}` : ''
}

// "hoje" no ano corrente; "em 31/12/2020" num ano passado.
export function asOfLabel(asOf: string, today: string): string {
  if (asOf === today) return 'hoje'
  const [y, m, d] = asOf.split('-')
  return `em ${d}/${m}/${y}`
}

// A lista da carteira: o que tem quantidade (com peso) e, à parte, o que saiu ou só rendeu no ano.
export function splitAssets(assets: PortfolioAsset[], group: PortfolioGroup | null) {
  const visible = group ? assets.filter((a) => a.group === group) : assets
  return { held: visible.filter((a) => a.quantity > 0), gone: visible.filter((a) => a.quantity === 0) }
}

// "Sem notas" (I.7e, revisão de 2026-10-12): só os ativos da carteira que nunca tiveram nota nem evento (postos à mão, ou
// que só têm proventos) e não estão na lista do ano. O que zerou num ano passado (vendido, convertido, liquidado) é
// histórico: aparece em "Saíram em {ano}" no ano em que saiu, não no ano corrente.
export function holdingsWithoutNotes<H extends { assetId: string }>(
  holdings: H[],
  listed: Set<string>,
  tracked: Set<string>,
): H[] {
  return holdings.filter((h) => !listed.has(h.assetId) && !tracked.has(h.assetId))
}
