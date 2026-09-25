import { describe, expect, it } from 'vitest'
import { crashKind } from './crash'

// Tela de erro: quando um pedaço do app sumiu do servidor (versão nova publicada com o app aberto),
// a saída é recarregar; qualquer outro erro é "algo quebrou".
describe('crashKind', () => {
  it('recognizes a missing chunk in Chrome, Safari and Firefox', () => {
    expect(crashKind(new TypeError('Failed to fetch dynamically imported module: https://x/assets/Page-a1.js'))).toBe('update')
    expect(crashKind(new TypeError('Importing a module script failed.'))).toBe('update')
    expect(crashKind(new TypeError('error loading dynamically imported module: https://x/assets/Page-a1.js'))).toBe('update')
  })

  it('treats anything else as a crash', () => {
    expect(crashKind(new TypeError("Cannot read properties of undefined (reading 'map')"))).toBe('crash')
    expect(crashKind('texto solto')).toBe('crash')
    expect(crashKind(undefined)).toBe('crash')
  })
})
