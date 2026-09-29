import type { Schemas } from '@/lib/api'
import { statementMonth } from './statementMove'

// Trocar o cartão de uma compra (docs/fase-2.md, 2.13): só de cartão para cartão. A API leva a compra
// inteira para as faturas do cartão novo.
type CardAccount = Pick<Schemas['AccountResponse'], 'id' | 'type' | 'isActive'>

// Os cartões ativos e o atual da compra (mesmo inativo), como a seção "Conta" do lançamento fora do cartão.
export function cardChoices<T extends CardAccount>(accounts: T[], currentId: string): T[] {
  return accounts.filter((a) => a.type === 'CreditCard' && (a.isActive || a.id === currentId))
}

// O aviso depois de salvar diz para onde a compra foi. A fatura é a do mês em que vence.
export const cardChangeText = (card: string, settlementDate: string) =>
  `Agora no ${card}, na fatura de ${statementMonth(settlementDate)}.`
