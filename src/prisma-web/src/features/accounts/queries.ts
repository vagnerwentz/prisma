import { useQuery } from '@tanstack/react-query'
import { api, unwrap, type Schemas } from '@/lib/api'

export type Account = Schemas['AccountResponse']

export const accountsKey = ['accounts'] as const

export function useAccounts() {
  return useQuery({
    queryKey: accountsKey,
    queryFn: async () => unwrap(await api.GET('/accounts')),
  })
}
