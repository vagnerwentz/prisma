// Conveniências por aparelho. O armazenamento pode falhar (aba anônima, dados bloqueados):
// nesse caso a tela funciona sem a preferência.
const lastAccountKey = 'prisma.lastAccountId'

export function readLastAccountId(): string | null {
  try {
    return localStorage.getItem(lastAccountKey)
  } catch {
    return null
  }
}

export function saveLastAccountId(accountId: string): void {
  try {
    localStorage.setItem(lastAccountKey, accountId)
  } catch {
    // sem preferência salva; nada a fazer
  }
}
