import { describe, expect, it } from 'vitest'
import { movedLabel, moveOptions, moveTexts, shiftReference, type MovableTransaction, type StatementInfo } from './statementMove'

// Mover uma compra no cartão de fatura (docs/fase-2.md, 2.9, tarefa 7): o painel só oferece o que a
// API aceitaria, e os textos dizem para onde a compra foi.
const statement = (reference: string, isPaid = false, dueDate = `${reference}-05`): StatementInfo => ({
  id: `s-${reference}`,
  reference,
  dueDate,
  isPaid,
})

const purchase = (reference: string, overrides: Partial<MovableTransaction> = {}): MovableTransaction => ({
  type: 'Expense',
  statementId: `s-${reference}`,
  statementPinned: false,
  ...overrides,
})

const october = statement('2026-10')
const november = statement('2026-11')
const december = statement('2026-12')

describe('shiftReference', () => {
  it('anda meses, virando o ano', () => {
    expect(shiftReference('2026-11', 1)).toBe('2026-12')
    expect(shiftReference('2026-12', 1)).toBe('2027-01')
    expect(shiftReference('2027-01', -1)).toBe('2026-12')
  })
})

describe('moveOptions', () => {
  it('compra no cartão pode ir para a seguinte e para a anterior', () => {
    const options = moveOptions([purchase('2026-10')], [october, november])

    expect(options).toEqual({ current: october, pinned: false, shifts: ['Previous', 'Next'] })
  })

  it('a fatura de destino que ainda não existe não impede (a API a abre)', () => {
    expect(moveOptions([purchase('2026-10')], [october])?.shifts).toEqual(['Previous', 'Next'])
  })

  it('destino pago tira só aquela direção', () => {
    const paidNovember = statement('2026-11', true)
    const paidSeptember = statement('2026-09', true)

    expect(moveOptions([purchase('2026-10')], [october, paidNovember])?.shifts).toEqual(['Previous'])
    expect(moveOptions([purchase('2026-10')], [paidSeptember, october])?.shifts).toEqual(['Next'])
  })

  it('parcelada: vale a fatura de cada parcela, e a atual é a da primeira', () => {
    const installments = [purchase('2026-10'), purchase('2026-11'), purchase('2026-12')]
    const paidJanuary = statement('2027-01', true)

    const options = moveOptions(installments, [october, november, december, paidJanuary])

    expect(options?.current).toBe(october)
    expect(options?.shifts).toEqual(['Previous'])
  })

  it('parcela em fatura paga: nenhuma direção', () => {
    const paidOctober = statement('2026-10', true)

    expect(moveOptions([purchase('2026-10'), purchase('2026-11')], [paidOctober, november])?.shifts).toEqual([])
  })

  it('mostra se a compra foi movida à mão', () => {
    expect(moveOptions([purchase('2026-11', { statementPinned: true })], [november])?.pinned).toBe(true)
  })

  it('estorno, lançamento fora do cartão e faturas ainda carregando: nada a oferecer', () => {
    expect(moveOptions([purchase('2026-10', { type: 'Refund' })], [october])).toBeNull()
    expect(moveOptions([purchase('2026-10', { type: 'Income' })], [october])).toBeNull()
    expect(moveOptions([{ type: 'Expense', statementId: null, statementPinned: false }], [])).toBeNull()
    expect(moveOptions([purchase('2026-10')], undefined)).toBeNull()
    expect(moveOptions([purchase('2026-10')], [november])).toBeNull()
    expect(moveOptions([], [october])).toBeNull()
  })
})

describe('moveTexts', () => {
  // Verbo + mês de destino: "Próxima fatura" com seta lia como navegar, não como mover (dono, 2.20).
  it('botão diz a ação e o mês de destino', () => {
    expect(moveTexts.button('2026-12')).toBe('Mover para dezembro')
    expect(moveTexts.button('2026-10')).toBe('Mover para outubro')
    expect(moveTexts.button('2027-01')).toBe('Mover para janeiro')
  })

  it('título diz em que fatura a compra está', () => {
    expect(moveTexts.heading('2026-11')).toBe('Na fatura de novembro')
  })

  it('aviso diz a fatura e o vencimento novos', () => {
    expect(moveTexts.moved({ reference: '2026-11', dueDate: '2026-11-05' }, 1)).toEqual({
      title: 'Movida para a fatura de novembro',
      description: 'Vence em 05/11/2026.',
    })
  })

  it('na parcelada, avisa que todas as parcelas andaram', () => {
    expect(moveTexts.moved({ reference: '2026-11', dueDate: '2026-11-05' }, 3).description).toBe(
      'Vence em 05/11/2026. As 3 parcelas andaram uma fatura.',
    )
  })

  it('volta ao desfazer', () => {
    expect(moveTexts.undone).toBe('A compra voltou para a fatura de antes')
  })
})

describe('movedLabel', () => {
  // A lista segue a data da compra; a compra movida diz em que fatura está (vence no mês da fatura).
  it('compra movida mostra a fatura em que está', () => {
    expect(movedLabel({ statementPinned: true, settlementDate: '2026-11-05' })).toBe('fatura de novembro')
    expect(movedLabel({ statementPinned: true, settlementDate: '2027-01-05' })).toBe('fatura de janeiro')
  })

  it('compra na fatura prevista não mostra nada', () => {
    expect(movedLabel({ statementPinned: false, settlementDate: '2026-10-05' })).toBeNull()
  })
})

describe('texto da compra movida', () => {
  it('diz o que a pessoa fez, sem jargão', () => {
    expect(moveTexts.pinned).toBe('Você moveu esta compra para esta fatura.')
  })
})

