import { formatCents } from '@/lib/money'
import { logoSrc, type AssetItem } from './assets'
import type { Holding } from './queries'

// Peças do ativo na carteira usadas por mais de uma tela (o painel do ativo, a aba Carteira).

// O resultado da venda não é receita: sinal em tinta, sem o espectro.
export const signedCents = (cents: number) => (cents < 0 ? formatCents(cents).replace('-', '−') : `+${formatCents(cents)}`)

export const assetOf = (h: Holding): AssetItem => ({
  id: h.assetId,
  symbol: h.symbol,
  name: h.name,
  kind: h.kind,
  isActive: h.isActive,
  hasLogo: h.hasLogo,
})

export const holdingLogo = (h: Holding) => logoSrc({ id: h.assetId, kind: h.kind, hasLogo: h.hasLogo })
