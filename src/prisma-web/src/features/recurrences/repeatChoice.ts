import type { Frequency } from './schedule'

// "Se repete" (docs/fase-2.md, 2.14, Tela): a frequência e o término. A data do lançamento é a partida.
// endDate '' é "Em uma data" ainda sem a data escolhida.
export type RepeatChoice = { frequency: Frequency | null; endDate: string | null }

export const noRepeat: RepeatChoice = { frequency: null, endDate: null }

// A escolha pronta para a API, ou o que falta nela.
export function repeatRequest(choice: RepeatChoice): { frequency: Frequency; endDate: string | null } | 'missing-end' | null {
  if (choice.frequency === null) return null
  if (choice.endDate === '') return 'missing-end'
  return { frequency: choice.frequency, endDate: choice.endDate }
}

export const missingEndMessage = 'Escolha a data do término.'

export type RepeatContext = { type: 'Expense' | 'Income' | 'Refund'; isCard: boolean; installments: number }

// A linha "se repete" aparece no Novo lançamento: não em estorno nem em compra parcelada no cartão.
export const canRepeat = ({ type, isCard, installments }: RepeatContext) => type !== 'Refund' && !(isCard && installments > 1)

// O que vai para a API: a escolha só vale enquanto a linha aparece (escolher "Todo mês" e depois 3x não manda
// a série).
export const repeatToSend = (context: RepeatContext, choice: RepeatChoice) => (canRepeat(context) ? repeatRequest(choice) : null)
