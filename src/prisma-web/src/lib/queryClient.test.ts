import { QueryObserver } from '@tanstack/react-query'
import { describe, expect, it, vi } from 'vitest'
import { ApiError } from './api'
import { meKey } from './queryKeys'
import { createQueryClient, shouldRetry } from './queryClient'

const failWith = (error: unknown) => () => Promise.reject(error)

describe('shouldRetry', () => {
  it('does not retry what a second try cannot fix', () => {
    for (const status of [400, 401, 403, 404, 409, 429]) expect(shouldRetry(0, new ApiError(status, undefined))).toBe(false)
  })

  it('retries a server or network failure once', () => {
    expect(shouldRetry(0, new ApiError(500, undefined))).toBe(true)
    expect(shouldRetry(0, new TypeError('Failed to fetch'))).toBe(true)
    expect(shouldRetry(1, new TypeError('Failed to fetch'))).toBe(false)
  })
})

// A sessão expira com o app aberto: o primeiro 401 limpa o cache e marca "sem usuário", e o
// RequireAuth leva para /entrar (docs: revisão antes da Fase 3).
describe('session expired', () => {
  it('forgets the user and everything cached when a request comes back 401', async () => {
    const onSessionExpired = vi.fn()
    const client = createQueryClient(onSessionExpired)
    client.setQueryData(meKey, { id: '1', email: 'a@b.com' })
    client.setQueryData(['transactions'], [{ id: 'x' }])
    // Como o RequireAuth: uma tela já observando o usuário precisa ficar sabendo.
    const guard = new QueryObserver(client, { queryKey: meKey, queryFn: () => null, staleTime: Infinity })
    const unsubscribe = guard.subscribe(() => {})

    await expect(client.fetchQuery({ queryKey: ['accounts'], queryFn: failWith(new ApiError(401, undefined)) })).rejects.toThrow()

    expect(guard.getCurrentResult().data).toBeNull()
    unsubscribe()
    expect(client.getQueryData(meKey)).toBeNull()
    expect(client.getQueryData(['transactions'])).toBeUndefined()
    expect(onSessionExpired).toHaveBeenCalledOnce()
  })

  it('also reacts to a mutation that comes back 401', async () => {
    const onSessionExpired = vi.fn()
    const client = createQueryClient(onSessionExpired)
    client.setQueryData(meKey, { id: '1', email: 'a@b.com' })

    const mutation = client.getMutationCache().build(client, { mutationFn: failWith(new ApiError(401, undefined)) })
    await expect(mutation.execute(undefined)).rejects.toThrow()

    expect(client.getQueryData(meKey)).toBeNull()
    expect(onSessionExpired).toHaveBeenCalledOnce()
  })

  it('ignores a 401 when nobody is logged in, such as a wrong password', async () => {
    const onSessionExpired = vi.fn()
    const client = createQueryClient(onSessionExpired)
    client.setQueryData(meKey, null)

    await expect(client.fetchQuery({ queryKey: ['x'], queryFn: failWith(new ApiError(401, undefined)) })).rejects.toThrow()

    expect(onSessionExpired).not.toHaveBeenCalled()
  })

  it('keeps the session on other errors', async () => {
    const client = createQueryClient()
    client.setQueryData(meKey, { id: '1', email: 'a@b.com' })

    await expect(client.fetchQuery({ queryKey: ['x'], queryFn: failWith(new ApiError(404, undefined)) })).rejects.toThrow()

    expect(client.getQueryData(meKey)).toEqual({ id: '1', email: 'a@b.com' })
  })
})

// Validade do cache: voltar a uma tela em menos de um minuto usa o que já veio, sem ir à rede.
// Qualquer mudança feita no app invalida o que afeta, então o dado não fica velho por isso.
describe('cache freshness', () => {
  const counting = () => {
    let calls = 0
    return { queryFn: () => Promise.resolve(++calls), calls: () => calls }
  }

  it('reuses a response for a minute', async () => {
    vi.useFakeTimers()
    try {
      const client = createQueryClient()
      const api = counting()

      await client.fetchQuery({ queryKey: ['dashboard', 'summary'], queryFn: api.queryFn })
      vi.advanceTimersByTime(59_000)
      await client.fetchQuery({ queryKey: ['dashboard', 'summary'], queryFn: api.queryFn })
      expect(api.calls()).toBe(1)

      vi.advanceTimersByTime(2_000)
      await client.fetchQuery({ queryKey: ['dashboard', 'summary'], queryFn: api.queryFn })
      expect(api.calls()).toBe(2)
    } finally {
      vi.useRealTimers()
    }
  })

  it('goes back to the server right after a change invalidates the data', async () => {
    const client = createQueryClient()
    const api = counting()

    await client.fetchQuery({ queryKey: ['dashboard', 'summary'], queryFn: api.queryFn })
    await client.invalidateQueries({ queryKey: ['dashboard'] })
    await client.fetchQuery({ queryKey: ['dashboard', 'summary'], queryFn: api.queryFn })

    expect(api.calls()).toBe(2)
  })
})
