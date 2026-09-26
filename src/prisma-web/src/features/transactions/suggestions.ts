import { defaultPaymentMethod, type AccountType, type PaymentMethod } from '@/features/accounts/labels'
import type { Schemas } from '@/lib/api'

// Autocompletar da descrição (docs/fase-2.md, 2.8). O vocabulário vem uma vez do backend; filtrar e
// ordenar a cada tecla é aqui, no aparelho, sem requisição.

export type DescriptionSuggestion = Schemas['ListDescriptionsItem']

export type SuggestionMatch = { suggestion: DescriptionSuggestion; start: number; length: number }

export const maxSuggestions = 5
export const maxRecent = 6

const marks = /[̀-ͯ]/g

// Sem acento e em minúsculas, letra a letra: o texto dobrado tem o mesmo tamanho do original, então
// a posição encontrada nele serve para destacar o trecho na grafia original.
export function fold(text: string): string {
  let folded = ''
  for (const ch of text) {
    const plain = ch.normalize('NFD').replace(marks, '').toLowerCase()
    folded += plain.length === ch.length ? plain : ch
  }
  return folded
}

function daysBetween(from: string, to: string): number {
  const [fy, fm, fd] = from.split('-').map(Number)
  const [ty, tm, td] = to.split('-').map(Number)
  return Math.round((Date.UTC(ty, tm - 1, td) - Date.UTC(fy, fm - 1, fd)) / 86_400_000)
}

// Uso pesado pela recência: o que foi usado semana passada vale mais do que o de meses atrás.
export function frecency(suggestion: DescriptionSuggestion, today: string): number {
  const days = daysBetween(suggestion.lastUsedOn, today)
  const weight = days <= 30 ? 2 : days <= 90 ? 1 : 0.5
  return suggestion.count * weight
}

const portuguese = new Intl.Collator('pt-BR', { sensitivity: 'base' })

function byUse(today: string) {
  return (a: DescriptionSuggestion, b: DescriptionSuggestion) =>
    frecency(b, today) - frecency(a, today) ||
    b.lastUsedOn.localeCompare(a.lastUsedOn) ||
    portuguese.compare(a.description, b.description)
}

const suggestible = (type: string) => type === 'Expense' || type === 'Income'

// Onde a busca aparece: 3 = início da descrição, 2 = início de uma palavra, 1 = qualquer trecho (a
// partir de 2 letras), 0 = não aparece.
function locate(text: string, query: string): { level: number; start: number } {
  if (text.startsWith(query)) return { level: 3, start: 0 }
  for (let i = text.indexOf(query); i >= 0; i = text.indexOf(query, i + 1)) {
    if (!/[\p{L}\p{N}]/u.test(text[i - 1])) return { level: 2, start: i }
  }
  const inside = query.length >= 2 ? text.indexOf(query) : -1
  return inside >= 0 ? { level: 1, start: inside } : { level: 0, start: -1 }
}

export function suggest(vocabulary: DescriptionSuggestion[], query: string, type: string, today: string): SuggestionMatch[] {
  const q = fold(query.trim().replace(/\s+/g, ' '))
  if (!q || !suggestible(type)) return []
  const order = byUse(today)
  return vocabulary
    .filter((s) => s.type === type)
    .map((suggestion) => ({ suggestion, ...locate(fold(suggestion.description), q) }))
    .filter((m) => m.level > 0)
    .sort((a, b) => b.level - a.level || order(a.suggestion, b.suggestion))
    .slice(0, maxSuggestions)
    .map(({ suggestion, start }) => ({ suggestion, start, length: q.length }))
}

// Campo vazio e focado: as de maior uso, em chips.
export function recentSuggestions(vocabulary: DescriptionSuggestion[], type: string, today: string): DescriptionSuggestion[] {
  if (!suggestible(type)) return []
  return vocabulary
    .filter((s) => s.type === type)
    .sort(byUse(today))
    .slice(0, maxRecent)
}

export type SuggestionFill = {
  description: string
  categoryId?: string
  accountId?: string
  method?: PaymentMethod
  // A pessoa já tinha escolhido e a sugestão era outra: fica a dela.
  keptAccount: boolean
  keptCategory: boolean
}

// O que escolher uma sugestão preenche. Nunca desfaz uma escolha da pessoa neste lançamento, não
// preenche conta ou categoria que não existem mais e nunca toca no valor.
export function suggestionFill(
  suggestion: DescriptionSuggestion,
  touched: { accountTouched: boolean; categoryTouched: boolean },
  accounts: { id: string; type: AccountType }[],
  categoryExists: (id: string) => boolean,
  current?: { accountId: string; categoryId: string },
): SuggestionFill {
  const fill: SuggestionFill = { description: suggestion.description, keptAccount: false, keptCategory: false }

  const category = suggestion.categoryId
  if (category && categoryExists(category)) {
    if (!touched.categoryTouched) fill.categoryId = category
    else fill.keptCategory = current ? current.categoryId !== category : true
  }

  const account = accounts.find((a) => a.id === suggestion.accountId)
  if (account) {
    if (!touched.accountTouched) {
      fill.accountId = account.id
      // No cartão só existe crédito; fora dele, crédito não vale e volta ao meio padrão da conta.
      fill.method =
        account.type === 'CreditCard'
          ? 'Credit'
          : suggestion.method === 'Credit'
            ? defaultPaymentMethod(account.type)
            : suggestion.method
    } else fill.keptAccount = current ? current.accountId !== account.id : true
  }
  return fill
}
