import { defaultPaymentMethod, type PaymentMethod } from '@/features/accounts/labels'
import type { Account } from '@/features/accounts/queries'
import type { Transaction } from './queries'

// "Lançar de novo" (docs/fase-1.md, 5.1): o painel e o aviso de "salvo" passam ao /lancar o que
// já têm na tela, pelo estado da navegação. Nenhuma requisição.

export type RepeatDraft = {
  type: 'Expense' | 'Income'
  amountCents: number
  accountId: string
  categoryId: string
  method: PaymentMethod
  description: string
  installments: number
}

export type RepeatSource = Pick<
  Transaction,
  'type' | 'amountCents' | 'accountId' | 'categoryId' | 'method' | 'description' | 'purchaseDate'
>
export type RepeatAccount = Pick<Account, 'id' | 'type'>

type RepeatEntry =
  { kind: 'single'; transaction: RepeatSource } | { kind: 'purchase'; totalCents: number; installments: RepeatSource[] }

// O que se repete é o que o painel mostra: na compra parcelada, o total e o número de parcelas.
// Estorno não se repete (seria outro estorno da mesma compra).
export function repeatDraftOf(entry: RepeatEntry): RepeatDraft | null {
  const first = entry.kind === 'single' ? entry.transaction : entry.installments[0]
  if (!first || (first.type !== 'Expense' && first.type !== 'Income')) return null
  return {
    type: first.type,
    amountCents: entry.kind === 'single' ? first.amountCents : entry.totalCents,
    accountId: first.accountId,
    categoryId: first.categoryId ?? '',
    method: first.method,
    description: first.description ?? '',
    installments: entry.kind === 'single' ? 1 : entry.installments.length,
  }
}

// Valores iniciais do formulário. A data é sempre hoje; com a conta desativada, vale a conta padrão
// do formulário, com o meio dela, à vista fora do cartão, e sem receita no cartão.
export function repeatValues(draft: RepeatDraft, accounts: RepeatAccount[], fallback: RepeatAccount, today: string) {
  const original = accounts.find((a) => a.id === draft.accountId)
  const account = original ?? fallback
  const isCard = account.type === 'CreditCard'
  return {
    type: draft.type === 'Income' && isCard ? ('Expense' as const) : draft.type,
    amountCents: draft.amountCents,
    accountId: account.id,
    categoryId: draft.categoryId,
    method: original ? draft.method : defaultPaymentMethod(account.type),
    purchaseDate: today,
    installments: isCard ? draft.installments : 1,
    description: draft.description,
  }
}

// O estado da navegação sobrevive a recarregar a página e pode vir de outra versão do app.
export function isRepeatDraft(value: unknown): value is RepeatDraft {
  if (typeof value !== 'object' || value === null) return false
  const v = value as Record<string, unknown>
  return (
    (v.type === 'Expense' || v.type === 'Income') &&
    Number.isSafeInteger(v.amountCents) &&
    typeof v.accountId === 'string' &&
    typeof v.categoryId === 'string' &&
    typeof v.method === 'string' &&
    typeof v.description === 'string' &&
    Number.isInteger(v.installments)
  )
}
