import { describe, expect, it } from 'vitest'
import type { Deposit, DepositBalance } from './fixedIncome'
import {
  batchCountLabel,
  batchDestination,
  batchHasEntries,
  batchHasFunding,
  batchNearlyAll,
  batchTotalRows,
  lineIsToggleable,
  lineNote,
  lineState,
  selectableDeposits,
  toggleAll,
  toggleId,
  undoItems,
  type BatchLine,
  type BatchResponse,
} from './redemptionBatch'

const line = (over: Partial<BatchLine> = {}): BatchLine => ({
  depositId: 'a',
  balanceCents: 116453,
  remainingPrincipalCents: 111518,
  skipped: null,
  redemption: null,
  ...over,
})
const redemption = (grossCents: number) => ({ grossCents }) as NonNullable<BatchLine['redemption']>

describe('selecionar aportes', () => {
  const balanceOf = new Map<string, DepositBalance>([
    ['a', { principalCents: 100 } as DepositBalance],
    ['b', { principalCents: 0 } as DepositBalance],
  ])

  it('oferece só os aportes com principal para resgatar', () => {
    expect(selectableDeposits([{ id: 'a' }, { id: 'b' }, { id: 'c' }], balanceOf)).toEqual([{ id: 'a' }])
  })

  it('marca e desmarca sem mexer na ordem dos outros', () => {
    expect(toggleId(['a', 'b'], 'c')).toEqual(['a', 'b', 'c'])
    expect(toggleId(['a', 'b', 'c'], 'b')).toEqual(['a', 'c'])
  })

  it('"Selecionar todos" marca todos e, estando todos marcados, tira todos', () => {
    const all = [{ id: 'a' }, { id: 'b' }]
    expect(toggleAll([], all)).toEqual(['a', 'b'])
    expect(toggleAll(['a'], all)).toEqual(['a', 'b'])
    expect(toggleAll(['a', 'b'], all)).toEqual([])
    expect(toggleAll([], [])).toEqual([])
  })
})

describe('a conta onde cai', () => {
  const accounts = [
    { id: 'itau', type: 'Checking', isActive: true },
    { id: 'nu', type: 'Checking', isActive: true },
  ]
  const deposit = (id: string, depositDate: string, from?: string) =>
    ({ id, depositDate, funding: from ? { fromAccountId: from } : null }) as unknown as Deposit

  it('sugere a conta do aporte mais antigo que saiu de uma conta', () => {
    const lot = [deposit('novo', '2025-07-19', 'itau'), deposit('antigo', '2025-05-12', 'nu'), deposit('solto', '2025-05-01')]
    expect(batchDestination(lot, accounts)).toBe('nu')
  })

  it('não sugere conta se nenhum aporte saiu de uma conta', () => {
    expect(batchDestination([deposit('a', '2025-05-12')], accounts)).toBeNull()
    expect(batchHasFunding([deposit('a', '2025-05-12')])).toBe(false)
    expect(batchHasFunding([deposit('a', '2025-05-12'), deposit('b', '2025-06-01', 'itau')])).toBe(true)
  })

  it('não muda a ordem da lista que recebeu', () => {
    const lot = [deposit('novo', '2025-07-19', 'itau'), deposit('antigo', '2025-05-12', 'nu')]
    batchDestination(lot, accounts)
    expect(lot.map((d) => d.id)).toEqual(['novo', 'antigo'])
  })
})

describe('"tudo" a poucos centavos', () => {
  it('oferece tudo a até 5 centavos abaixo do saldo total, e só abaixo dele', () => {
    expect(batchNearlyAll(539541, 539544)).toBe(true)
    expect(batchNearlyAll(539539, 539544)).toBe(true)
    expect(batchNearlyAll(539538, 539544)).toBe(false)
    expect(batchNearlyAll(539544, 539544)).toBe(false)
    expect(batchNearlyAll(0, 3)).toBe(false)
  })
})

describe('a linha da prévia', () => {
  it('diz se o aporte sai inteiro, em parte, fica de fora ou foi pulado', () => {
    expect(lineState(line({ redemption: redemption(116453) }))).toBe('full')
    expect(lineState(line({ redemption: redemption(100000) }))).toBe('partial')
    expect(lineState(line())).toBe('untouched')
    expect(lineState(line({ skipped: 'NotYetDeposited', balanceCents: null }))).toBe('skipped')
  })

  it('explica cada caso e só deixa marcar o que a API não pulou', () => {
    expect(lineNote(line({ redemption: redemption(116453) }))).toBe('Sai o saldo inteiro.')
    expect(lineNote(line({ redemption: redemption(1) }))).toBe('Sai uma parte; o resto continua rendendo.')
    expect(lineNote(line())).toBe('O valor não chega até este aporte.')
    expect(lineNote(line({ skipped: 'FullyRedeemed' }))).toBe('Já foi resgatado por inteiro.')
    expect(lineNote(line({ skipped: 'NotYetDeposited' }))).toBe('O aporte é depois dessa data.')
    expect(lineIsToggleable(line())).toBe(true)
    expect(lineIsToggleable(line({ skipped: 'FullyRedeemed' }))).toBe(false)
  })
})

describe('os totais e o desfazer', () => {
  const totals = { grossCents: 539541, principalCents: 523809, yieldCents: 15732, taxCents: 3540, netCents: 536001 }

  it('mostra os totais na ordem do resgate de um aporte só', () => {
    expect(batchTotalRows(totals, false)).toEqual([
      { label: 'Valor resgatado', cents: 539541 },
      { label: 'Rendimento bruto que sai', cents: 15732 },
      { label: 'Imposto estimado', cents: 3540 },
      { label: 'Líquido que cai', cents: 536001 },
    ])
    expect(batchTotalRows(totals, true)[2].label).toBe('Imposto pelo extrato')
  })

  it('conta os aportes no singular e no plural', () => {
    expect(batchCountLabel(1)).toBe('1 aporte')
    expect(batchCountLabel(3)).toBe('3 aportes')
  })

  it('desfaz um item por resgate criado, com os lançamentos junto quando houve', () => {
    const response = {
      redemptions: [
        { id: 'r1', depositId: 'a', funding: { destinationAccountId: 'x' } },
        { id: 'r2', depositId: 'b', funding: null },
      ],
    } as unknown as BatchResponse
    expect(undoItems(response)).toEqual([
      { depositId: 'a', redemptionId: 'r1' },
      { depositId: 'b', redemptionId: 'r2' },
    ])
    expect(batchHasEntries(response)).toBe(true)
    expect(batchHasEntries({ redemptions: [{ id: 'r', depositId: 'a', funding: null }] } as unknown as BatchResponse)).toBe(false)
  })
})
