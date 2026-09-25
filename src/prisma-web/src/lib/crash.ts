export type CrashKind = 'crash' | 'update'

// Mensagens que os navegadores dão quando um import() sob demanda não encontra o arquivo: depois
// de publicar uma versão nova, os nomes com hash mudam e a aba antiga pede arquivos que não existem.
const missingChunk = [
  'Failed to fetch dynamically imported module', // Chrome, Edge
  'Importing a module script failed', // Safari
  'error loading dynamically imported module', // Firefox
]

export function crashKind(error: unknown): CrashKind {
  const message = error instanceof Error ? error.message : ''
  return missingChunk.some((text) => message.includes(text)) ? 'update' : 'crash'
}
