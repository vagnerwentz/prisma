import { addDays, formatShortDate } from '@/lib/dates'
import type { Schemas } from '@/lib/api'
import { formatCents } from '@/lib/money'

// Renda fixa por objetivo (docs/renda-fixa.md, seção 7). Só bruto: imposto e líquido vêm na R.7. Aqui ficam as
// contas e os textos puros; a tela só desenha.
export type Goal = Schemas['GoalResponse']
export type Deposit = Schemas['DepositResponse']
export type Balances = Schemas['BalancesResponse']
export type RatesStatus = Schemas['RatesStatus']
export type GroupBalance = Schemas['GroupBalance']
export type DepositBalance = Schemas['DepositBalance']
export type EvolutionPoint = Schemas['EvolutionPoint']
export type Redemption = Schemas['RedemptionResponse']
export type RedemptionPreview = Schemas['RedemptionPreviewResponse']
export type DepositRedeemed = NonNullable<DepositBalance['redeemed']>

// O produto (docs/renda-fixa.md, 6.5): CDB, LCI e LCA rendem do mesmo jeito; o que muda é o imposto. As letras são isentas de IR para
// pessoa física (Lei 11.033, art. 3º) e a LCA também de IOF.
export type ProductKind = Deposit['productKind']
export const productKinds: ProductKind[] = ['Cdb', 'Lci', 'Lca']
export const productLabels: Record<ProductKind, string> = { Cdb: 'CDB', Lci: 'LCI', Lca: 'LCA' }
export const isLetter = (product: ProductKind): boolean => product !== 'Cdb'

// Prazo mínimo das letras sem índice de preços: 6 meses (Resolução CMN 5.215/2025). Só avisa: letras antigas tinham 90 dias e a pessoa
// pode cadastrar histórico.
export const letterMinMonths = 6

// Soma meses a uma data AAAA-MM-DD sem passar do fim do mês (31/08 + 6 meses = 28/02).
export function addMonths(date: string, months: number): string {
  const [y, m, d] = date.split('-').map(Number)
  const index = y * 12 + (m - 1) + months
  const year = Math.floor(index / 12)
  const month = (index % 12) + 1
  const last = new Date(Date.UTC(year, month, 0)).getUTCDate()
  return `${String(year).padStart(4, '0')}-${String(month).padStart(2, '0')}-${String(Math.min(d, last)).padStart(2, '0')}`
}

export function maturityWarning(product: ProductKind, depositDate: string, maturity: string): string | null {
  if (!isLetter(product) || !maturity || maturity < depositDate) return null
  return maturity < addMonths(depositDate, letterMinMonths)
    ? `LCI e LCA têm prazo mínimo de ${letterMinMonths} meses. Confira o vencimento.`
    : null
}

// Como o aporte aparece nas listas: "LCI · 90% do CDI".
export function depositTitle(product: ProductKind, kind: IndexKind, rateE4: number): string {
  return `${productLabels[product]} · ${formatRate(kind, rateE4)}`
}

// Quais imposto se explica: o do CDB, o das letras ou os dois. Sem aporte, o do CDB (a explicação de sempre).
export function taxScope(deposits: readonly Pick<DepositBalance, 'productKind'>[]): { cdb: boolean; letters: boolean } {
  const letters = deposits.some((d) => d.productKind !== 'Cdb')
  const cdb = deposits.some((d) => d.productKind === 'Cdb')
  return { cdb: cdb || !letters, letters }
}

// Como o aporte rende (docs/renda-fixa.md, 5.1 e 5.5): X % do CDI, CDI + uma taxa fixa ao ano, ou uma taxa fixa ao ano (pré).
export type IndexKind = Deposit['indexKind']
export const indexKinds: IndexKind[] = ['Cdi', 'CdiPlus', 'Prefixed']
export const indexKindLabels: Record<IndexKind, string> = { Cdi: '% do CDI', CdiPlus: 'CDI + taxa', Prefixed: 'Pré-fixado' }

// A taxa viaja em E4 (100% = 1_000_000) e o que ela é depende do índice: o percentual do CDI (a API aceita de 0,01% a 500%) ou a
// taxa ao ano (de 0,01% a 100%). Um campo só na tela, com o rótulo do índice.
export const percentScale = 10_000
export const defaultIndexPercentE4 = 100 * percentScale

export function percentToE4(percent: number): number {
  return Math.round(percent * percentScale)
}

export function percentFromE4(e4: number): number {
  return e4 / percentScale
}

// "100", "112,5" → 100, 112.5. Texto inválido ou fora de 0,01% a `max` (500% no CDI): null.
export function parsePercent(text: string, max = 500): number | null {
  const normalized = text.trim().replace('%', '').replace(/\s/g, '').replace(',', '.')
  if (!/^\d+(\.\d{1,4})?$/.test(normalized)) return null
  const value = Number(normalized)
  return value >= 0.01 && value <= max ? value : null
}

type RateField = { max: number; suffix: string; label: string; hint: string | null; error: string }

export const rateFields: Record<IndexKind, RateField> = {
  Cdi: { max: 500, suffix: '% do CDI', label: 'Percentual do CDI', hint: null, error: 'Use um percentual entre 0,01% e 500%.' },
  CdiPlus: {
    max: 100,
    suffix: '% ao ano, mais o CDI',
    label: 'Taxa ao ano',
    hint: 'Rende o CDI mais esta taxa fixa por ano, como "CDI + 2%".',
    error: 'Use uma taxa entre 0,01% e 100% ao ano.',
  },
  Prefixed: {
    max: 100,
    suffix: '% ao ano',
    label: 'Taxa ao ano',
    hint: 'Taxa fixa por ano, contada em dias úteis (252 por ano).',
    error: 'Use uma taxa entre 0,01% e 100% ao ano.',
  },
}

export const parseRate = (text: string, kind: IndexKind): number | null => parsePercent(text, rateFields[kind].max)

// O texto inicial do campo: no CDI, o padrão do objetivo; nos outros, vazio (não há taxa padrão a sugerir).
export function initialRateText(kind: IndexKind, defaultE4: number): string {
  return kind === 'Cdi' ? formatPercent(percentFromE4(defaultE4)).replace('%', '') : ''
}

// 100 → "100%"; 112.5 → "112,5%".
export function formatPercent(percent: number): string {
  return `${percent.toLocaleString('pt-BR', { maximumFractionDigits: 4 })}%`
}

export function formatPercentE4(e4: number): string {
  return `${formatPercent(percentFromE4(e4))} do CDI`
}

// O que o aporte rende, como se lê: "110% do CDI", "CDI + 2% a.a.", "14% a.a. (pré)".
export function formatRate(kind: IndexKind, rateE4: number): string {
  const rate = formatPercent(percentFromE4(rateE4))
  if (kind === 'CdiPlus') return `CDI + ${rate} a.a.`
  if (kind === 'Prefixed') return `${rate} a.a. (pré)`
  return formatPercentE4(rateE4)
}

// De onde vêm os números de um conjunto de aportes: só do CDI, só da taxa contratada (pré) ou dos dois. Sem aporte, vale o CDI
// (o aviso de taxas ausentes continua aparecendo antes do primeiro aporte).
export type RatesScope = 'cdi' | 'prefixed' | 'both'

export function ratesScope(deposits: readonly Pick<DepositBalance, 'indexKind'>[]): RatesScope {
  const prefixed = deposits.filter((d) => d.indexKind === 'Prefixed').length
  if (prefixed === 0) return 'cdi'
  return prefixed === deposits.length ? 'prefixed' : 'both'
}

export function allDeposits(balances: Balances): DepositBalance[] {
  return [...balances.goals.flatMap((g) => g.deposits), ...balances.unassigned.deposits]
}

// Quanto da meta já foi: 0 a 1, sem passar de 1. Sem meta: null (a barra nem aparece).
export function goalProgress(balanceCents: number, targetCents: number | null): number | null {
  if (targetCents === null || targetCents <= 0) return null
  return Math.min(1, Math.max(0, balanceCents / targetCents))
}

export function progressLabel(balanceCents: number, targetCents: number | null): string | null {
  const progress = goalProgress(balanceCents, targetCents)
  return progress === null ? null : `${Math.floor(progress * 100)}% da meta`
}

export type RatesNotice = { kind: 'unavailable' | 'stale' | 'incomplete'; text: string }

// Os avisos de taxa, cada um com texto próprio (seção 7.3). Sem taxa nenhuma, nada foi calculado; defasada, o saldo
// pode estar abaixo do real; incompleta, faltam dias no meio.
export function ratesNotice(rates: RatesStatus): RatesNotice | null {
  if (!rates.available || !rates.through)
    return {
      kind: 'unavailable',
      text: 'Ainda não temos as taxas do CDI. Os valores abaixo são só o que você aportou.',
    }
  if (!rates.complete)
    return {
      kind: 'incomplete',
      text: 'Faltam taxas do CDI em alguns dias. Nesses dias o saldo entra pelo valor aportado.',
    }
  if (rates.stale)
    return {
      kind: 'stale',
      text: `As taxas do CDI estão sem atualizar desde ${formatShortDate(rates.through)}. O saldo pode estar abaixo do real.`,
    }
  return null
}

// A nota que fica sempre visível (seção 7.3). O pré-fixado não usa o CDI: rende pela taxa contratada, em dias úteis, até hoje.
export function ratesFootnote(rates: RatesStatus, scope: RatesScope = 'cdi'): string {
  if (scope === 'prefixed') return 'Cálculo pela taxa contratada, em dias úteis, até hoje. Estimativa; o extrato do banco manda.'
  const cdi = rates.through
    ? `Cálculo pelo CDI publicado pelo Banco Central até ${formatShortDate(rates.through)}`
    : 'Cálculo pelo CDI publicado pelo Banco Central'
  return scope === 'both'
    ? `${cdi}; o pré-fixado, pela taxa contratada, em dias úteis, até hoje. Estimativa; o extrato do banco manda.`
    : `${cdi}. Estimativa; o extrato do banco manda.`
}

export function depositCountLabel(count: number): string {
  return count === 1 ? '1 aporte' : `${count} aportes`
}

// O aporte sem taxa não some: entra pelo principal e avisa (decisão do dono).
export function depositUnavailableNote(deposit: DepositBalance): string | null {
  if (deposit.unavailable === null) return null
  return deposit.unavailable === 'BeforeFirstRate'
    ? 'Aporte antes da primeira taxa que temos: entra pelo valor aportado.'
    : 'Sem taxa do CDI para este aporte: entra pelo valor aportado.'
}

export type Period = '1M' | '3M' | '6M' | 'ALL'
export const periods: Period[] = ['1M', '3M', '6M', 'ALL']
export const periodLabels: Record<Period, string> = { '1M': '1M', '3M': '3M', '6M': '6M', ALL: 'Tudo' }

// O recorte do gráfico: de quando até hoje, e de quanto em quanto tempo mostrar um ponto.
export function periodRange(period: Period, today: string, firstDeposit: string | null): { from: string; step: 'day' | 'week' | 'month' } {
  const days = { '1M': 31, '3M': 92, '6M': 183, ALL: 0 }[period]
  if (period === 'ALL') {
    const from = firstDeposit ?? today
    const span = daysBetween(from, today)
    return { from, step: span > 365 ? 'month' : span > 120 ? 'week' : 'day' }
  }
  return { from: addDays(today, -days), step: period === '6M' ? 'week' : 'day' }
}

function daysBetween(from: string, to: string): number {
  return Math.round((Date.parse(`${to}T00:00:00Z`) - Date.parse(`${from}T00:00:00Z`)) / 86_400_000)
}

// "all", "none" ou o id do objetivo, como a API entende.
export type GoalScope = 'all' | 'none' | string

// Quem o endereço chama de objetivo: ?objetivo=<id> ou ?objetivo=sem (os aportes soltos).
export const looseGoalParam = 'sem'

// Bruto ou líquido estimado (R.7). O líquido é o bruto menos o imposto de resgatar hoje; nunca "lucro" nem "ganho" (7.3).
export type ValueMode = 'gross' | 'net'
export const valueModes: ValueMode[] = ['gross', 'net']
export const valueModeLabels: Record<ValueMode, string> = { gross: 'Bruto', net: 'Líquido estimado' }
export type DepositTax = NonNullable<DepositBalance['tax']>

export function shownBalance(group: GroupBalance, mode: ValueMode): number {
  return mode === 'net' ? group.netBalanceCents : group.grossBalanceCents
}

export function shownYield(group: GroupBalance, mode: ValueMode): number {
  return mode === 'net' ? group.netYieldCents : group.grossYieldCents
}

// O rendimento de um aporte no modo escolhido; sem taxa (ou sem imposto calculado), nada a mostrar.
export function depositYield(deposit: DepositBalance, mode: ValueMode): number | null {
  if (deposit.grossYieldCents === null) return null
  return mode === 'net' ? (deposit.tax?.netYieldCents ?? null) : deposit.grossYieldCents
}

export const yieldLabels: Record<ValueMode, string> = { gross: 'Rendimento bruto', net: 'Rendimento líquido estimado' }
export const balanceLabels: Record<ValueMode, string> = { gross: 'Saldo bruto estimado', net: 'Saldo líquido estimado' }

// O que a renda fixa do Prisma ainda não faz (R.8): sempre visível, para ninguém ler o número como mais do que ele é.
export const limitationsNote =
  'Por enquanto só CDB, LCI e LCA que rendem um percentual do CDI, o CDI mais uma taxa ou uma taxa fixa ao ano: sem IPCA e sem projeção do que vai render.'

// O imposto das letras: a LCI e a LCA são isentas de IR para pessoa física (Lei 11.033, art. 3º); a LCA também de IOF, e a LCI paga o
// IOF dos primeiros 29 dias, como o CDB.
export const lettersTaxFootnote =
  'LCI e LCA: isentas de IR para pessoa física (Lei 11.033, art. 3º), então o líquido é o bruto. A LCA também não paga IOF; a LCI paga o IOF dos primeiros 29 dias, como o CDB.'

// A explicação do imposto, sempre a mesma: é estimativa de resgate hoje, e quem retém é o banco.
export const taxFootnote =
  'Imposto estimado como se você resgatasse hoje: IOF nos primeiros 29 dias e IR de 22,5% a 15% conforme o prazo de cada aporte, pela tabela regressiva da B3. O banco retém no resgate; o valor real pode diferir em centavos.'

// Quando o aporte rende e a regra de imposto diz quanto: "IR 22,5% · 87 dias".
export function depositTaxNote(tax: DepositTax | null | undefined): string | null {
  if (!tax) return null
  const parts = []
  if (tax.iofCents > 0) parts.push('IOF')
  const exempt = tax.irRateBasisPoints === 0
  if (!exempt) parts.push(`IR ${formatPercent(tax.irRateBasisPoints / 100)}`)
  // LCI e LCA: sem IR. "Isento de IR" sozinho, ou "IOF, isento de IR" quando ainda há IOF (LCI com menos de 30 dias).
  const label = exempt ? (parts.length ? `${parts.join(' + ')}, isento de IR` : 'Isento de IR') : parts.join(' + ')
  return `${label} · ${tax.holdingDays} ${tax.holdingDays === 1 ? 'dia' : 'dias'}`
}

// ---------- Resgate (docs/renda-fixa-resgate.md, 4 e 6) ----------
// A API entrega tudo calculado (saldo do que sobra, o que já saiu, o imposto congelado e a prévia); aqui ficam só a escolha do que mostrar
// e os textos. O imposto nunca é refeito na tela.

// Aporte resgatado por inteiro: já houve resgate e não sobra principal. Vai para a seção "Resgatados", fora do saldo.
export const isFullyRedeemed = (b: Pick<DepositBalance, 'principalCents' | 'redeemed'>): boolean =>
  b.redeemed != null && b.principalCents === 0

// Os aportes com saldo (ou sem saldo calculado ainda) e os resgatados por inteiro, na ordem em que vieram.
export function splitByRedemption<T extends { id: string }>(
  deposits: readonly T[],
  balanceOf: ReadonlyMap<string, DepositBalance>,
): { open: T[]; redeemed: T[] } {
  const open: T[] = []
  const redeemed: T[] = []
  for (const deposit of deposits) {
    const balance = balanceOf.get(deposit.id)
    ;(balance && isFullyRedeemed(balance) ? redeemed : open).push(deposit)
  }
  return { open, redeemed }
}

// Só se resgata o que tem principal; sem o saldo carregado, o botão não aparece.
export const canRedeem = (balance: DepositBalance | undefined): boolean => balance !== undefined && balance.principalCents > 0

// O que já saiu de um grupo, no que a pessoa recebeu: o líquido e o imposto pago. Nunca "lucro" nem "ganho".
export function redeemedLine(
  group: Pick<GroupBalance, 'redeemedGrossCents' | 'redeemedTaxCents' | 'redeemedNetCents'>,
): string | null {
  const { redeemedGrossCents, redeemedTaxCents, redeemedNetCents } = group
  if (!redeemedGrossCents) return null
  return `Já resgatado: líquido ${formatCents(redeemedNetCents ?? 0)} · imposto pago ${formatCents(redeemedTaxCents ?? 0)}`
}

export function depositRedeemedNote(redeemed: DepositRedeemed, remainingPrincipalCents: number): string {
  const count = redeemed.count === 1 ? '1 resgate' : `${redeemed.count} resgates`
  const left = remainingPrincipalCents > 0 ? `sobra ${formatCents(remainingPrincipalCents)} de principal` : 'nada sobra'
  return `Resgatado ${formatCents(redeemed.grossCents)} em ${count} · ${left}`
}

// De onde veio o imposto do resgate: calculado (IOF e IR) ou o total que a pessoa leu no extrato.
export function redemptionTaxNote(redemption: Redemption): string {
  if (redemption.taxSource === 'Informed') return `Imposto ${formatCents(redemption.taxCents)}, pelo extrato`
  const parts: string[] = []
  if (redemption.iofCents) parts.push(`IOF ${formatCents(redemption.iofCents)}`)
  if (redemption.irCents) parts.push(`IR ${formatCents(redemption.irCents)}`)
  return parts.length > 0 ? parts.join(' · ') : 'Sem imposto'
}

// O valor do resgate no modo "Resgatar tudo": o saldo da data escolhida, se a API já o deu para essa data (um saldo de outra data não
// vale: é o que deixava o valor velho depois de trocar a data). Fora do modo, o que a pessoa digitou. Sem saldo da data, zero.
export function allGross(all: boolean, balanceAt: { date: string; cents: number } | null, date: string, typedCents: number): number {
  if (!all) return typedCents
  return balanceAt !== null && balanceAt.date === date ? balanceAt.cents : 0
}

// Valor digitado a poucos centavos do saldo (o extrato do banco difere do app em centavos): oferece "tudo" para não deixar centavos soltos.
export const NEARLY_ALL_CENTS = 5
export function nearlyAll(grossCents: number, balanceCents: number): boolean {
  const gap = balanceCents - grossCents
  return grossCents > 0 && gap > 0 && gap <= NEARLY_ALL_CENTS
}

// As linhas da prévia, como seriam gravadas.
export function previewRows(redemption: Redemption): { label: string; cents: number }[] {
  return [
    { label: 'Rendimento bruto que sai', cents: redemption.yieldCents },
    { label: redemption.taxSource === 'Informed' ? 'Imposto pelo extrato' : 'Imposto estimado', cents: redemption.taxCents },
    { label: 'Líquido que cai', cents: redemption.netCents },
  ]
}

// A conta onde sugerir que o dinheiro caiu: a de onde o aporte saiu, se ainda serve; senão a primeira corrente ou a carteira. Aporte que
// não saiu de uma conta pelo app não oferece conta (a API recusa: não há de onde devolver o principal).
type AccountChoice = { id: string; type: string; isActive: boolean }

export const redemptionAccountTypes = ['Checking', 'Cash']

export const usableForRedemption = (account: AccountChoice): boolean =>
  account.isActive && redemptionAccountTypes.includes(account.type)

export function redemptionDestination(
  funding: Deposit['funding'] | undefined,
  accounts: readonly AccountChoice[],
): string | null {
  if (!funding) return null
  const usable = accounts.filter(usableForRedemption)
  return usable.find((a) => a.id === funding.fromAccountId)?.id ?? usable[0]?.id ?? null
}
