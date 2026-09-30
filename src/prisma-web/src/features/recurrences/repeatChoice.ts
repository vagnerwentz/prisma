import type { AccountType } from '@/features/accounts/labels'
import { addDays } from '@/lib/dates'
import type { Frequency } from './schedule'

// "Se repete" (docs/fase-2.md, 2.14, Tela): a frequência e o término. A data do lançamento é a partida.
// endDate '' é "Em uma data" ainda sem a data escolhida. Débito automático (2.15): autoDebit, amountVaries (o
// valor é a estimativa) e dueDaysBefore (o vencimento da primeira ocorrência, em dias antes da data do
// lançamento: guardado assim, trocar a data do lançamento não o deixa fora do intervalo).
export type RepeatChoice = {
  frequency: Frequency | null
  endDate: string | null
  autoDebit?: boolean
  amountVaries?: boolean
  dueDaysBefore?: number
}

export const noRepeat: RepeatChoice = { frequency: null, endDate: null }

export type RepeatBody = {
  frequency: Frequency
  endDate: string | null
  autoDebit?: boolean
  amountVaries?: boolean
  dueDate?: string
}

// A escolha pronta para a API, ou o que falta nela. Débito automático só todo mês; sem ele, só os campos de
// antes, como a tela aberta antes da 2.26 mandava.
export function repeatRequest(choice: RepeatChoice, start?: string): RepeatBody | 'missing-end' | null {
  if (choice.frequency === null) return null
  if (choice.endDate === '') return 'missing-end'
  const body: RepeatBody = { frequency: choice.frequency, endDate: choice.endDate }
  if (!choice.autoDebit || choice.frequency !== 'Monthly' || start === undefined) return body
  return { ...body, autoDebit: true, amountVaries: !!choice.amountVaries, dueDate: addDays(start, -(choice.dueDaysBefore ?? 0)) }
}

export const missingEndMessage = 'Escolha a data do término.'

export type RepeatContext = {
  type: 'Expense' | 'Income' | 'Refund'
  isCard: boolean
  installments: number
  accountType?: AccountType
}

// A linha "se repete" aparece no Novo lançamento: não em estorno nem em compra parcelada no cartão.
export const canRepeat = ({ type, isCard, installments }: RepeatContext) => type !== 'Refund' && !(isCard && installments > 1)

// Débito automático: só despesa, só conta corrente (2.15, regra 2).
export const canAutoDebit = ({ type, accountType }: { type: RepeatContext['type']; accountType?: AccountType }) =>
  type === 'Expense' && accountType === 'Checking'

// O que vai para a API: a escolha só vale enquanto a linha aparece (escolher "Todo mês" e depois 3x não manda
// a série), e o débito automático só enquanto a conta e o tipo o permitem.
export function repeatToSend(context: RepeatContext, choice: RepeatChoice, start?: string) {
  if (!canRepeat(context)) return null
  return repeatRequest(canAutoDebit(context) ? choice : { ...choice, autoDebit: false }, start)
}
