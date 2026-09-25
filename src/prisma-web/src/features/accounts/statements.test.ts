import { describe, expect, it } from 'vitest'
import { groupStatements, statementStatus, statementTitle, statementTotal } from './statements'

// Cartão que fecha dia 5 e vence dia 12 (mesmo cenário do backend, docs/fase-1.md).
const s = (
  reference: string,
  closing: string,
  due: string,
  totalCents: number,
  extra: Partial<{ isPaid: boolean; datesEditedManually: boolean }> = {},
) => ({
  id: reference,
  reference,
  closingDate: closing,
  dueDate: due,
  totalCents,
  isPaid: extra.isPaid ?? false,
  datesEditedManually: extra.datesEditedManually ?? false,
})

const march = s('2026-03', '2026-03-05', '2026-03-12', 1000)
const april = s('2026-04', '2026-04-05', '2026-04-12', 2000)
const may = s('2026-05', '2026-05-05', '2026-05-12', 3000)
const june = s('2026-06', '2026-06-05', '2026-06-12', 0)
const july = s('2026-07', '2026-07-05', '2026-07-12', 4000)

describe('groupStatements', () => {
  it('a atual é a primeira fatura aberta que fecha hoje ou depois', () => {
    const groups = groupStatements([july, june, may, april, march], '2026-04-01')
    expect(groups.current?.reference).toBe('2026-04')
  })

  it('compra no dia do fechamento ainda entra na fatura: ela continua a atual', () => {
    expect(groupStatements([may, april, march], '2026-04-05').current?.reference).toBe('2026-04')
    expect(groupStatements([may, april, march], '2026-04-06').current?.reference).toBe('2026-05')
  })

  it('próximas em ordem de vencimento; anteriores da mais recente para a mais antiga', () => {
    const groups = groupStatements([march, july, april, may], '2026-04-20')
    expect(groups.current?.reference).toBe('2026-05')
    expect(groups.upcoming.map((x) => x.reference)).toEqual(['2026-07'])
    expect(groups.past.map((x) => x.reference)).toEqual(['2026-04', '2026-03'])
  })

  it('esconde faturas vazias, exceto a atual e as de datas ajustadas à mão', () => {
    const edited = s('2026-08', '2026-08-04', '2026-08-11', 0, { datesEditedManually: true })
    const emptyCurrent = s('2026-05', '2026-05-05', '2026-05-12', 0)
    const groups = groupStatements([edited, july, june, emptyCurrent, april], '2026-04-20')
    expect(groups.current?.reference).toBe('2026-05')
    expect(groups.upcoming.map((x) => x.reference)).toEqual(['2026-07', '2026-08'])
  })

  it('fatura paga nunca é a atual', () => {
    const paidApril = s('2026-04', '2026-04-05', '2026-04-12', 2000, { isPaid: true })
    const groups = groupStatements([may, paidApril], '2026-04-01')
    expect(groups.current?.reference).toBe('2026-05')
    expect(groups.past.map((x) => x.reference)).toEqual(['2026-04'])
  })

  it('sem fatura aberta, não há atual', () => {
    expect(groupStatements([march], '2026-04-01').current).toBeUndefined()
  })
})

describe('statementStatus', () => {
  it.each([
    [april, '2026-04-01', 'Aberta'],
    [april, '2026-04-05', 'Aberta'],
    [april, '2026-04-06', 'Fechada'],
    [may, '2026-04-01', 'Futura'],
    [s('2026-03', '2026-03-05', '2026-03-12', 1000, { isPaid: true }), '2026-04-01', 'Paga'],
    // Estorno maior que as compras (docs/fase-2.md, 2.5, regra 9): fechada e negativa é saldo a favor.
    [s('2026-04', '2026-04-05', '2026-04-12', -10000), '2026-04-06', 'Saldo a favor'],
    [s('2026-04', '2026-04-05', '2026-04-12', -10000), '2026-04-01', 'Aberta'],
  ])('%o em %s → %s', (statement, today, expected) => {
    const current = groupStatements([july, may, april], today).current
    expect(statementStatus(statement, today, current?.id)).toBe(expected)
  })
})

describe('statementTitle', () => {
  // A referência é o mês do vencimento (docs/fase-1.md).
  it('usa o mês do vencimento', () => {
    expect(statementTitle('2026-10')).toBe('Outubro de 2026')
  })
})

describe('statementTotal', () => {
  // Total negativo não é valor a pagar: é saldo a favor, mostrado sem sinal.
  it('mostra o saldo a favor sem sinal', () => {
    expect(statementTotal(-10000)).toEqual({ label: 'Saldo a favor', text: 'R$\u00a0100,00' })
    expect(statementTotal(11000)).toEqual({ label: null, text: 'R$\u00a0110,00' })
    expect(statementTotal(0)).toEqual({ label: null, text: 'R$\u00a00,00' })
  })
})
