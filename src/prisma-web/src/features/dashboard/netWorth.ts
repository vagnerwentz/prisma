import type { Schemas } from '@/lib/api'
import { formatCents } from '@/lib/money'

// O patrimônio de hoje (I.9, docs/patrimonio.md): Disponível, Investido e Caixa nas corretoras. A conta é da API (`NetWorthSplit`); aqui só se
// decide o que mostrar.
export type NetWorth = Schemas['GetNetWorthResponse']

// Sem renda fixa, ação nem conta de investimento, o Resumo mostra só o saldo de sempre ("Em contas").
export function hasInvestments(net: NetWorth): boolean {
  return net.investedCents !== 0 || net.brokers.length > 0
}

// O "Caixa nas corretoras" só aparece se alguma conta de investimento acompanha o caixa.
export function tracksAnyCash(net: NetWorth): boolean {
  return net.brokers.some((b) => b.tracksCash)
}

// O que explica o "Investido": vem dos ativos (renda fixa inteira e ações, estas ao custo), com o rendimento da renda fixa dentro. Curto, para
// caber em duas linhas em 320px.
export function investedNote(net: Pick<NetWorth, 'yieldCents'>): string {
  return net.yieldCents > 0 ? `Inclui ${formatCents(net.yieldCents)} de rendimento` : 'Renda fixa e ações'
}

// O caixa livre de uma conta de investimento (saldo menos o principal da renda fixa que ela financiou); nulo se ela não acompanha o caixa.
export function freeCashOf(net: NetWorth, accountId: string): number | null {
  const broker = net.brokers.find((b) => b.accountId === accountId)
  return broker?.tracksCash ? (broker.freeCashCents ?? null) : null
}
