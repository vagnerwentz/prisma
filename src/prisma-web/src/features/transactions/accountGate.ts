// O que falta para lançar (etapa 2.19): nenhuma conta ainda, contas só desativadas, ou nada.
export type AccountGate = 'none' | 'inactive' | 'ready'

export function accountGate(accounts: readonly { isActive: boolean }[]): AccountGate {
  if (accounts.length === 0) return 'none'
  return accounts.some((a) => a.isActive) ? 'ready' : 'inactive'
}
