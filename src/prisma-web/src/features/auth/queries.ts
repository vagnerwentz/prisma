import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { api, unwrap, type Schemas } from '@/lib/api'

export type CurrentUser = Schemas['MeResponse']

const meKey = ['auth', 'me'] as const

// Usuário da sessão atual, ou null quando não há sessão (401).
export function useCurrentUser() {
  return useQuery({
    queryKey: meKey,
    queryFn: async (): Promise<CurrentUser | null> => {
      const result = await api.GET('/auth/me')
      if (result.response.status === 401) return null
      return unwrap(result)
    },
    staleTime: Infinity,
  })
}

export type Credentials = { email: string; password: string }

export function useLogin() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: async (body: Credentials) => unwrap(await api.POST('/auth/login', { body })),
    onSuccess: (user) => queryClient.setQueryData(meKey, user),
  })
}

export function useRegister() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: async (body: Credentials) => unwrap(await api.POST('/auth/register', { body })),
    onSuccess: (user) => queryClient.setQueryData(meKey, user),
  })
}

export function useLogout() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: async () => {
      const result = await api.POST('/auth/logout')
      if (!result.response.ok) throw new Error('Não foi possível sair. Tente novamente.')
    },
    // Nada do usuário anterior pode sobrar em cache.
    onSuccess: () => {
      queryClient.clear()
      queryClient.setQueryData(meKey, null)
    },
  })
}
