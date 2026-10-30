import type { Schemas } from '@/lib/api'

// Tela de Pessoas do admin (docs/admin-email.md, 3.1 e 3.2): os textos e a validação do campo de dias. Só leitura do que a API
// devolveu; quem decide é o servidor (ActivityWindow).
export type UserSummary = Schemas['UserSummaryResponse']

// Os mesmos limites de ActivityWindow no servidor, para avisar antes de consultar.
export const minDays = 1
export const maxDays = 365
export const defaultDays = 30

// Atalhos do campo: só preenchem o número. Não existem na API, que aceita qualquer N de 1 a 365.
export const quickDays = [7, 30, 90] as const

const rangeProblem = `Informe de ${minDays} a ${maxDays} dias.`

// Só número inteiro e sem sinal: "3,5", "+7" e "1e2" não valem. Espaços em volta são ignorados.
export function parseDays(text: string): { days: number } | { problem: string } {
  const trimmed = text.trim()
  if (!/^\d+$/.test(trimmed)) return { problem: rangeProblem }
  const days = Number(trimmed)
  return days >= minDays && days <= maxDays ? { days } : { problem: rangeProblem }
}

const dayWord = (days: number) => (days === 1 ? 'dia' : 'dias')

// Inativa = vista há N dias ou mais (dia civil de São Paulo); ativa = há menos de N dias, contando hoje como o primeiro.
export const inactiveLabel = (days: number) => `Sem acesso há ${days} ${dayWord(days)} ou mais`

export const activeLabel = (days: number) => (days === 1 ? 'Acessaram hoje' : `Acessaram nos últimos ${days} dias`)

export function peopleCount(count: number): string {
  return `${count.toLocaleString('pt-BR')} ${count === 1 ? 'pessoa' : 'pessoas'}`
}

// As contas de antes do registro de acesso foram carimbadas pela migration (docs/admin-email.md, 2): até passar o prazo da janela
// desde a atualização, "sem acesso" pode esconder quem usou o app antes dela.
export const reliabilityNote = (days: number) =>
  `As contas criadas antes do registro de acesso aparecem com a data da atualização. A contagem de quem está sem acesso só é ` +
  `confiável depois de ${days} ${dayWord(days)} dessa atualização.`
