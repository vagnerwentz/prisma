import { useQuery } from '@tanstack/react-query'
import { api, unwrap, type Schemas } from '@/lib/api'

export type Transaction = Schemas['TransactionResponse']

export const transactionsKey = ['transactions'] as const

// Período pela data da compra (docs/fase-1.md): a lista mostra o que aconteceu em cada dia.
export function useTransactions(range: { from: string; to: string }) {
  return useQuery({
    queryKey: [...transactionsKey, range],
    queryFn: async () => unwrap(await api.GET('/transactions', { params: { query: range } })),
  })
}
