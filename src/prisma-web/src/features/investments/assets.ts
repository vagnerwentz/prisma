import type { Schemas } from '@/lib/api'

// Ativos da bolsa (docs/investimentos.md, seção 8, etapa 3).
// O gerador marca o tipo como anulável porque a lista de lançamentos o traz só nos proventos.
export type AssetKind = NonNullable<Schemas['AssetKind']>
export type AssetItem = Schemas['SearchAssetsItem']

export const assetKindLabels: Record<AssetKind, string> = {
  Stock: 'Ação',
  Unit: 'Unit',
  Fii: 'FII',
  Etf: 'ETF',
  FiInfra: 'FI-Infra',
  FiAgro: 'Fiagro',
  Fip: 'FIP',
  Fidc: 'FIDC',
  OtherFund: 'Fundo',
  Bdr: 'BDR',
  Unknown: 'Ativo',
}

export function assetKindLabel(kind: Schemas['AssetKind']): string {
  return kind ? assetKindLabels[kind] : ''
}

// O código em duas partes, para o ladrilho: a raiz em destaque e o número menor. "MXRF11" → MXRF + 11,
// "03BK11" → 03BK + 11, "BDOM" → BDOM sem número.
export function splitSymbol(symbol: string): { root: string; suffix: string } {
  const match = /^(.+?)(\d+)$/.exec(symbol)
  return match ? { root: match[1], suffix: match[2] } : { root: symbol, suffix: '' }
}

// Quem mostra o logo (docs/investimentos.md, etapa 4, decisão do dono): ação, unit e BDR, com o logo da empresa. O
// fundo mostra o código: o logo que o fornecedor dá a ele é o da gestora, igual para todos os fundos dela.
export function showsLogo(kind: Schemas['AssetKind']): boolean {
  return kind === 'Stock' || kind === 'Unit' || kind === 'Bdr'
}

// O endereço do logo guardado pelo Prisma, ou nada (o ladrilho mostra o código).
export function logoSrc(asset: { id: string; kind: Schemas['AssetKind']; hasLogo: boolean }): string | undefined {
  return asset.hasLogo && showsLogo(asset.kind) ? `/api/assets/${asset.id}/logo` : undefined
}
