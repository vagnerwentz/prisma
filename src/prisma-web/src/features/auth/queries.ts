import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { api, unwrap, type Schemas } from '@/lib/api'
import { meKey } from '@/lib/queryKeys'

export type CurrentUser = Schemas['MeResponse']

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

// O cadastro não entra: a conta espera a confirmação do e-mail (docs/email.md, 5.1).
export function useRegister() {
  return useMutation({
    mutationFn: async (body: Credentials) => unwrap(await api.POST('/auth/register', { body })),
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

export type ConfirmEmailRequest = { userId: string; token: string; password: string }

// O link do e-mail de confirmação, com a senha do cadastro: confirma e já entra (docs/email.md, 5.1). Nada do usuário anterior fica em cache.
export function useConfirmEmail() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: async (body: ConfirmEmailRequest) => unwrap(await api.POST('/auth/confirm-email', { body })),
    onSuccess: (user) => {
      queryClient.clear()
      queryClient.setQueryData(meKey, user)
    },
  })
}

// Pede outro e-mail de confirmação. A API responde igual exista a conta ou não.
export function useResendConfirmation() {
  return useMutation({
    mutationFn: async (email: string) => {
      const result = await api.POST('/auth/resend-confirmation', { body: { email } })
      if (!result.response.ok) unwrap(result)
    },
  })
}
