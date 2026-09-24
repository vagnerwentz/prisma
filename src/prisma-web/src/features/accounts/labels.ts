import type { Schemas } from '@/lib/api'

export type AccountType = Schemas['AccountType']
export type PaymentMethod = Schemas['PaymentMethod']

export const accountTypeLabels: Record<AccountType, string> = {
  Checking: 'Conta corrente',
  CreditCard: 'Cartão de crédito',
  Cash: 'Dinheiro',
  Investment: 'Investimento',
}

export const paymentMethodLabels: Record<PaymentMethod, string> = {
  Pix: 'Pix',
  Debit: 'Débito',
  Credit: 'Crédito',
  Boleto: 'Boleto',
  Cash: 'Dinheiro',
  Ted: 'TED',
}

// Meio de pagamento mais provável para cada tipo de conta, para lançar sem mexer no campo.
export function defaultPaymentMethod(type: AccountType): PaymentMethod {
  if (type === 'CreditCard') return 'Credit'
  if (type === 'Cash') return 'Cash'
  if (type === 'Investment') return 'Ted'
  return 'Pix'
}
