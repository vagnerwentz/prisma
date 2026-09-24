import createClient from 'openapi-fetch'
import type { components, paths } from './api-types'

// Mesma origem do frontend: em desenvolvimento o proxy do Vite repassa /api para a API, e o
// cookie de sessão (httpOnly) vai junto sem CORS. Tipos gerados por `npm run gen:api`.
export const api = createClient<paths>({ baseUrl: '/api' })

export type Schemas = components['schemas']

type Problem = Schemas['ProblemDetails'] & { errors?: Record<string, string[]> }

// Erro de uma chamada à API, com o ProblemDetails que ela devolveu (detail em pt-BR).
export class ApiError extends Error {
  readonly status: number
  readonly problem: Problem | undefined

  constructor(status: number, problem: unknown) {
    const parsed = typeof problem === 'object' && problem !== null ? (problem as Problem) : undefined
    super(parsed?.detail ?? fallbackMessage(status))
    this.status = status
    this.problem = parsed
  }

  // Erros de validação por campo, com o nome do campo como no formulário ("Email" → "email").
  get fieldErrors(): Record<string, string> {
    const errors = this.problem?.errors ?? {}
    return Object.fromEntries(
      Object.entries(errors)
        .filter(([, messages]) => messages.length > 0)
        .map(([field, messages]) => [field.charAt(0).toLowerCase() + field.slice(1), messages[0]]),
    )
  }
}

function fallbackMessage(status: number): string {
  if (status === 429) return 'Muitas tentativas. Aguarde um minuto e tente novamente.'
  if (status >= 500) return 'O servidor está com problemas. Tente novamente em instantes.'
  return 'Não foi possível concluir. Tente novamente.'
}

// Resultado de uma chamada do openapi-fetch: devolve os dados ou lança ApiError.
export function unwrap<T>(result: { data?: T; error?: unknown; response: Response }): T {
  if (result.error !== undefined || result.data === undefined) {
    throw new ApiError(result.response.status, result.error)
  }
  return result.data
}
