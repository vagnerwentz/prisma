import type { Schemas } from '@/lib/api'

// Proventos (docs/investimentos.md, etapa 5b): somas e agrupamentos da tela, a partir da lista da API.
export type Payout = Schemas['PayoutResponse']
export type PayoutKind = NonNullable<Schemas['PayoutKind']>

export const payoutKindLabels: Record<PayoutKind, string> = {
  Dividend: 'Dividendo',
  InterestOnEquity: 'JCP',
  FundIncome: 'Rendimento',
}

export const payoutKinds: PayoutKind[] = ['Dividend', 'InterestOnEquity', 'FundIncome']

// O tipo que o form sugere: fundo paga rendimento; ação, unit e BDR, dividendo.
export function suggestedKind(assetKind: Schemas['AssetKind']): PayoutKind {
  return assetKind === 'Stock' || assetKind === 'Unit' || assetKind === 'Bdr' || assetKind === 'Unknown'
    ? 'Dividend'
    : 'FundIncome'
}

// Soma do ano pela data do pagamento ("2026-09-30" → 2026).
export function yearTotal(payouts: Payout[], year: number): number {
  const prefix = `${year}-`
  return payouts.reduce((sum, p) => (p.date.startsWith(prefix) ? sum + p.amountCents : sum), 0)
}

// Quanto cada ativo já pagou, desde sempre.
export function totalsByAsset(payouts: Payout[]): Map<string, number> {
  const totals = new Map<string, number>()
  for (const p of payouts) totals.set(p.assetId, (totals.get(p.assetId) ?? 0) + p.amountCents)
  return totals
}

export type PayoutDay = { date: string; items: Payout[]; totalCents: number }

// Os proventos por dia de pagamento, do mais recente ao mais antigo (a API já manda nessa ordem).
export function groupByDay(payouts: Payout[]): PayoutDay[] {
  const days: PayoutDay[] = []
  for (const p of payouts) {
    let day = days.at(-1)
    if (day?.date !== p.date) {
      day = { date: p.date, items: [], totalCents: 0 }
      days.push(day)
    }
    day.items.push(p)
    day.totalCents += p.amountCents
  }
  return days
}

type AccountOption = { id: string; type: Schemas['AccountType']; isActive: boolean }

// Onde o provento cai: contas ativas, menos cartão de crédito.
export function payoutAccounts<T extends AccountOption>(accounts: T[]): T[] {
  return accounts.filter((a) => a.isActive && a.type !== 'CreditCard')
}

// A conta já escolhida no form: a do último provento (quem recebe no Itaú recebe sempre no Itaú); senão a
// primeira conta de investimento; senão a primeira que serve.
export function defaultPayoutAccount<T extends AccountOption>(accounts: T[], payouts: Payout[]): T | undefined {
  const options = payoutAccounts(accounts)
  const last = payouts[0] && options.find((a) => a.id === payouts[0].accountId)
  return last ?? options.find((a) => a.type === 'Investment') ?? options[0]
}
