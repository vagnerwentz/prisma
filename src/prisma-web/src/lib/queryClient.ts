import { MutationCache, QueryCache, QueryClient } from '@tanstack/react-query'
import { ApiError } from './api'
import { meKey } from './queryKeys'

// Repetir só o que pode dar certo na segunda vez: falha de rede ou do servidor. Um 4xx (sessão,
// validação, não encontrado) volta igual, e repetir só atrasa a mensagem de erro.
export function shouldRetry(failureCount: number, error: unknown): boolean {
  if (error instanceof ApiError && error.status < 500) return false
  return failureCount < 1
}

// Por quanto tempo uma resposta vale sem ir de novo ao servidor. Sem isso, voltar a uma tela
// (Resumo → Contas → Resumo) refazia todos os pedidos dela, ainda que nada tivesse mudado. Não deixa
// número velho depois de um lançamento: toda mutação invalida o que afeta (invalidateMoney e as
// mutações de conta e fatura), e o invalidado vai ao servidor na hora. O que pode ficar até um
// minuto atrasado é só o que mudou fora desta aba, como um lançamento feito no celular.
export const freshFor = 60_000

// A sessão (cookie de 14 dias) pode expirar com o app aberto. O primeiro 401 de qualquer consulta
// ou mutação esquece o usuário e tudo o que estava em cache, como no "Sair"; o RequireAuth então
// leva para /entrar e volta à mesma tela depois do login. Sem usuário na sessão (senha errada no
// login, por exemplo), o 401 é só o erro daquele pedido.
export function createQueryClient(onSessionExpired: () => void = () => {}): QueryClient {
  const handleError = (error: unknown) => {
    if (!(error instanceof ApiError) || error.status !== 401) return
    if (!client.getQueryData(meKey)) return
    // Marca "sem usuário" na consulta que o RequireAuth já observa (um clear() a trocaria por outra,
    // e a tela nunca saberia), e só então descarta o resto do cache.
    client.setQueryData(meKey, null)
    client.removeQueries({ predicate: (query) => query.queryHash !== JSON.stringify(meKey) })
    onSessionExpired()
  }

  const client = new QueryClient({
    queryCache: new QueryCache({ onError: handleError }),
    mutationCache: new MutationCache({ onError: handleError }),
    defaultOptions: {
      queries: { retry: shouldRetry, refetchOnWindowFocus: false, staleTime: freshFor },
    },
  })
  return client
}
