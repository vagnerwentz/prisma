import type { Schemas } from '@/lib/api'
import { canRedeem, NEARLY_ALL_CENTS, redemptionDestination, type Deposit, type DepositBalance } from './fixedIncome'

// Resgate em lote (docs/renda-fixa-resgate.md, 15): as contas e os textos puros da tela. A divisão do valor entre os aportes e o imposto
// de cada um vêm da API (a prévia); aqui só se escolhem os aportes, se lê a prévia e se montam as frases.
export type BatchPreview = Schemas['BatchPreviewResponse']
export type BatchLine = Schemas['BatchLine']
export type BatchResponse = Schemas['BatchResponse']
export type BatchTotals = Schemas['BatchTotals']

// Os aportes que o modo "Selecionar" oferece: os que ainda têm principal para resgatar.
export const selectableDeposits = <T extends { id: string }>(
  deposits: readonly T[],
  balanceOf: ReadonlyMap<string, DepositBalance>,
): T[] => deposits.filter((d) => canRedeem(balanceOf.get(d.id)))

// Marca ou desmarca um id, sem mexer na ordem dos outros.
export const toggleId = (ids: readonly string[], id: string): string[] =>
  ids.includes(id) ? ids.filter((x) => x !== id) : [...ids, id]

// Todos marcados, ou nenhum: o botão "Selecionar todos" alterna.
export const toggleAll = (selected: readonly string[], selectable: readonly { id: string }[]): string[] =>
  selectable.length > 0 && selectable.every((d) => selected.includes(d.id)) ? [] : selectable.map((d) => d.id)

// A conta onde sugerir que o dinheiro caiu: a do aporte mais antigo que saiu de uma conta pelo app (é por onde a divisão começa).
type AccountChoice = Parameters<typeof redemptionDestination>[1][number]

export function batchDestination(deposits: readonly Deposit[], accounts: readonly AccountChoice[]): string | null {
  const oldest = deposits.filter((d) => d.funding).sort((a, b) => a.depositDate.localeCompare(b.depositDate))[0]
  return oldest ? redemptionDestination(oldest.funding, accounts) : null
}

// Só há o que perguntar sobre a conta se algum aporte do lote saiu de uma conta pelo app; os outros entram como registro.
export const batchHasFunding = (deposits: readonly Deposit[]): boolean => deposits.some((d) => d.funding)

// O valor digitado fica a poucos centavos do saldo de todos os aportes: oferece "tudo" para não deixar centavos soltos.
export function batchNearlyAll(grossCents: number, totalBalanceCents: number): boolean {
  const gap = totalBalanceCents - grossCents
  return grossCents > 0 && gap > 0 && gap <= NEARLY_ALL_CENTS
}

// O que a linha da prévia diz do aporte. `skipped` vem da API: o aporte é depois da data, ou já foi resgatado por inteiro.
export type LineState = 'skipped' | 'full' | 'partial' | 'untouched'

export function lineState(line: BatchLine): LineState {
  if (line.skipped) return 'skipped'
  if (!line.redemption) return 'untouched'
  return line.redemption.grossCents >= (line.balanceCents ?? 0) ? 'full' : 'partial'
}

export const skippedNotes: Record<string, string> = {
  NotYetDeposited: 'O aporte é depois dessa data.',
  FullyRedeemed: 'Já foi resgatado por inteiro.',
}

export function lineNote(line: BatchLine): string {
  switch (lineState(line)) {
    case 'skipped':
      return skippedNotes[line.skipped ?? ''] ?? 'Fica fora do resgate.'
    case 'full':
      return 'Sai o saldo inteiro.'
    case 'partial':
      return 'Sai uma parte; o resto continua rendendo.'
    case 'untouched':
      return 'O valor não chega até este aporte.'
  }
}

// Só o que dá para marcar ou desmarcar na prévia: o aporte que a API pulou não entra em nenhum resgate.
export const lineIsToggleable = (line: BatchLine): boolean => !line.skipped

// Os totais do rodapé da prévia, como o resgate de um aporte só os mostra.
export function batchTotalRows(totals: BatchTotals, informed: boolean): { label: string; cents: number }[] {
  return [
    { label: 'Valor resgatado', cents: totals.grossCents },
    { label: 'Rendimento bruto que sai', cents: totals.yieldCents },
    { label: informed ? 'Imposto pelo extrato' : 'Imposto estimado', cents: totals.taxCents },
    { label: 'Líquido que cai', cents: totals.netCents },
  ]
}

export const batchCountLabel = (count: number): string => (count === 1 ? '1 aporte' : `${count} aportes`)

// Os itens do "Desfazer lote": o resgate criado em cada aporte.
export const undoItems = (response: BatchResponse): { depositId: string; redemptionId: string }[] =>
  response.redemptions.map((r) => ({ depositId: r.depositId, redemptionId: r.id }))

// Algum resgate do lote caiu numa conta: ao desfazer, os lançamentos saem junto (o "Desfazer" é do que acabou de ser feito).
export const batchHasEntries = (response: BatchResponse): boolean => response.redemptions.some((r) => r.funding)
