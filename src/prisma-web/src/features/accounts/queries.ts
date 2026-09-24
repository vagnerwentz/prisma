import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { api, unwrap, type Schemas } from '@/lib/api'

export type Account = Schemas['AccountResponse']
export type NewAccount = Schemas['CreateAccountRequest']

export const accountsKey = ['accounts'] as const

export function useAccounts() {
  return useQuery({
    queryKey: accountsKey,
    queryFn: async () => unwrap(await api.GET('/accounts')),
  })
}

export function useCreateAccount() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: async (body: NewAccount) => unwrap(await api.POST('/accounts', { body })),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: accountsKey }),
  })
}
