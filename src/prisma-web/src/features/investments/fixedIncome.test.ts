import { describe, expect, it } from 'vitest'
import {
  defaultIndexPercentE4,
  balanceLabels,
  depositCountLabel,
  depositTaxNote,
  depositYield,
  formatPercent,
  formatPercentE4,
  goalProgress,
  limitationsNote,
  addMonths,
  allDeposits,
  depositTitle,
  isLetter,
  lettersTaxFootnote,
  maturityWarning,
  productKinds,
  productLabels,
  taxScope,
  formatRate,
  indexKindLabels,
  indexKinds,
  initialRateText,
  parseRate,
  ratesScope,
  rateFields,
  parsePercent,
  percentFromE4,
  percentToE4,
  periodRange,
  progressLabel,
  ratesFootnote,
  ratesNotice,
  shownBalance,
  shownYield,
  taxFootnote,
  yieldLabels,
  type Balances,
  type DepositBalance,
  type GroupBalance,
  type IndexKind,
  type ProductKind,
  type RatesStatus,
} from './fixedIncome'

const rates = (over: Partial<RatesStatus> = {}): RatesStatus => ({
  available: true,
  through: '2026-10-15',
  stale: false,
  complete: true,
  expectedLast: '2026-10-15',
  ...over,
})

describe('percentual do CDI em E4', () => {
  it('100% é 1.000.000', () => {
    expect(defaultIndexPercentE4).toBe(1_000_000)
    expect(percentToE4(100)).toBe(1_000_000)
    expect(percentToE4(112.5)).toBe(1_125_000)
    expect(percentFromE4(1_125_000)).toBe(112.5)
  })

  it('não perde casas por causa do ponto flutuante', () => {
    expect(percentToE4(100.07)).toBe(1_000_700)
    expect(percentToE4(0.0001 * 3)).toBe(3)
  })

  it('lê vírgula, ponto e o sinal de porcento', () => {
    expect(parsePercent('100')).toBe(100)
    expect(parsePercent('112,5')).toBe(112.5)
    expect(parsePercent(' 98.75% ')).toBe(98.75)
  })

  it('recusa texto inválido e fora de 0,01% a 500%', () => {
    expect(parsePercent('')).toBeNull()
    expect(parsePercent('abc')).toBeNull()
    expect(parsePercent('1,23456')).toBeNull()
    expect(parsePercent('0')).toBeNull()
    expect(parsePercent('500,01')).toBeNull()
    expect(parsePercent('-5')).toBeNull()
    expect(parsePercent('500')).toBe(500)
  })

  it('mostra no formato brasileiro', () => {
    expect(formatPercent(100)).toBe('100%')
    expect(formatPercent(112.5)).toBe('112,5%')
    expect(formatPercentE4(1_000_000)).toBe('100% do CDI')
  })
})

describe('taxa do aporte: CDI, CDI + taxa e pré-fixado', () => {
  it('os três jeitos de render, na ordem da tela', () => {
    expect(indexKinds).toEqual(['Cdi', 'CdiPlus', 'Prefixed'])
    expect(indexKindLabels).toEqual({ Cdi: '% do CDI', CdiPlus: 'CDI + taxa', Prefixed: 'Pré-fixado' })
  })

  it('cada índice tem a sua faixa: o percentual do CDI vai a 500% e a taxa ao ano, a 100%', () => {
    expect(parseRate('110', 'Cdi')).toBe(110)
    expect(parseRate('500', 'Cdi')).toBe(500)
    expect(parseRate('500,01', 'Cdi')).toBeNull()
    expect(parseRate('2', 'CdiPlus')).toBe(2)
    expect(parseRate('14', 'Prefixed')).toBe(14)
    expect(parseRate('100', 'Prefixed')).toBe(100)
    expect(parseRate('110', 'Prefixed')).toBeNull()
    expect(parseRate('100,01', 'CdiPlus')).toBeNull()
    expect(parseRate('0', 'Prefixed')).toBeNull()
    expect(parseRate('abc', 'CdiPlus')).toBeNull()
  })

  it('a taxa viaja em E4, igual nos três', () => {
    expect(percentToE4(14)).toBe(140_000)
    expect(percentToE4(2)).toBe(20_000)
    expect(percentFromE4(140_000)).toBe(14)
  })

  it('lê como o mercado escreve', () => {
    expect(formatRate('Cdi', 1_100_000)).toBe('110% do CDI')
    expect(formatRate('CdiPlus', 20_000)).toBe('CDI + 2% a.a.')
    expect(formatRate('CdiPlus', 25_000)).toBe('CDI + 2,5% a.a.')
    expect(formatRate('Prefixed', 140_000)).toBe('14% a.a. (pré)')
  })

  it('o campo diz o que é a taxa e o erro cita a faixa certa', () => {
    expect(rateFields.Cdi.suffix).toBe('% do CDI')
    expect(rateFields.CdiPlus.suffix).toContain('ao ano')
    expect(rateFields.CdiPlus.suffix).toContain('CDI')
    expect(rateFields.Prefixed.suffix).toBe('% ao ano')
    expect(rateFields.Cdi.error).toContain('500%')
    expect(rateFields.Prefixed.error).toContain('100% ao ano')
    expect(rateFields.Cdi.hint).toBeNull()
    expect(rateFields.Prefixed.hint).toContain('252')
  })

  it('só o CDI começa preenchido (com o padrão do objetivo); os outros começam vazios', () => {
    expect(initialRateText('Cdi', 1_100_000)).toBe('110')
    expect(initialRateText('CdiPlus', 1_100_000)).toBe('')
    expect(initialRateText('Prefixed', 1_100_000)).toBe('')
  })
})

describe('produto: CDB, LCI e LCA', () => {
  it('três produtos, e só as letras são isentas', () => {
    expect(productKinds).toEqual(['Cdb', 'Lci', 'Lca'])
    expect(productLabels).toEqual({ Cdb: 'CDB', Lci: 'LCI', Lca: 'LCA' })
    expect(isLetter('Cdb')).toBe(false)
    expect(isLetter('Lci')).toBe(true)
    expect(isLetter('Lca')).toBe(true)
  })

  it('soma meses sem passar do fim do mês e vira o ano', () => {
    expect(addMonths('2026-01-15', 6)).toBe('2026-07-15')
    expect(addMonths('2026-08-31', 6)).toBe('2027-02-28')
    expect(addMonths('2027-08-31', 6)).toBe('2028-02-29')
    expect(addMonths('2026-10-18', 6)).toBe('2027-04-18')
    expect(addMonths('2026-12-31', 1)).toBe('2027-01-31')
  })

  it('avisa o vencimento menor que 6 meses nas letras, e só nelas', () => {
    expect(maturityWarning('Lci', '2026-10-18', '2027-04-17')).toContain('prazo mínimo de 6 meses')
    expect(maturityWarning('Lca', '2026-10-18', '2027-01-01')).toBe('LCI e LCA têm prazo mínimo de 6 meses. Confira o vencimento.')
    expect(maturityWarning('Lci', '2026-10-18', '2027-04-18')).toBeNull()
    expect(maturityWarning('Lci', '2026-10-18', '2028-10-18')).toBeNull()
    expect(maturityWarning('Cdb', '2026-10-18', '2026-11-18')).toBeNull()
    expect(maturityWarning('Lci', '2026-10-18', '')).toBeNull()
  })

  it('não avisa de um vencimento antes do aporte: o formulário já recusa', () => {
    expect(maturityWarning('Lci', '2026-10-18', '2026-10-01')).toBeNull()
  })

  it('o título da linha traz o produto e a taxa', () => {
    expect(depositTitle('Cdb', 'Cdi', 1_100_000)).toBe('CDB · 110% do CDI')
    expect(depositTitle('Lci', 'Cdi', 900_000)).toBe('LCI · 90% do CDI')
    expect(depositTitle('Lca', 'Prefixed', 110_000)).toBe('LCA · 11% a.a. (pré)')
  })

  it('a explicação do imposto: do CDB, das letras ou das duas; sem aporte, a do CDB', () => {
    const of = (...p: ProductKind[]) => taxScope(p.map((productKind) => ({ productKind })))
    expect(of()).toEqual({ cdb: true, letters: false })
    expect(of('Cdb')).toEqual({ cdb: true, letters: false })
    expect(of('Lci', 'Lca')).toEqual({ cdb: false, letters: true })
    expect(of('Cdb', 'Lci')).toEqual({ cdb: true, letters: true })
  })

  it('a nota das letras cita a lei e diz o que cada uma paga', () => {
    expect(lettersTaxFootnote).toContain('isentas de IR')
    expect(lettersTaxFootnote).toContain('Lei 11.033')
    expect(lettersTaxFootnote).toContain('LCA também não paga IOF')
    expect(lettersTaxFootnote).toContain('LCI paga o IOF')
    expect(lettersTaxFootnote.toLowerCase()).not.toContain('lucro')
  })

  it('a nota do imposto da linha: IR do CDB, isenção das letras e IOF da LCI nova', () => {
    const base = {
      iofCents: 0,
      irCents: 0,
      totalCents: 0,
      irRateBasisPoints: 2_250,
      holdingDays: 87,
      ruleVersion: 'v',
      netYieldCents: 0,
      netBalanceCents: 0,
    }
    const tax = (over: Partial<typeof base>) => ({ ...base, ...over })
    expect(depositTaxNote(tax({ irRateBasisPoints: 2_250, iofCents: 0, holdingDays: 87 }))).toBe('IR 22,5% · 87 dias')
    expect(depositTaxNote(tax({ irRateBasisPoints: 2_250, iofCents: 100, holdingDays: 10 }))).toBe('IOF + IR 22,5% · 10 dias')
    expect(depositTaxNote(tax({ irRateBasisPoints: 0, iofCents: 0, holdingDays: 87 }))).toBe('Isento de IR · 87 dias')
    expect(depositTaxNote(tax({ irRateBasisPoints: 0, iofCents: 100, holdingDays: 10 }))).toBe('IOF, isento de IR · 10 dias')
    expect(depositTaxNote(tax({ irRateBasisPoints: 0, iofCents: 0, holdingDays: 1 }))).toBe('Isento de IR · 1 dia')
    expect(depositTaxNote(null)).toBeNull()
  })
})

describe('de onde vêm os números', () => {
  const kinds = (...k: IndexKind[]) => k.map((indexKind) => ({ indexKind }))

  it('só CDI, só pré ou os dois; sem aporte vale o CDI', () => {
    expect(ratesScope([])).toBe('cdi')
    expect(ratesScope(kinds('Cdi', 'CdiPlus'))).toBe('cdi')
    expect(ratesScope(kinds('Prefixed'))).toBe('prefixed')
    expect(ratesScope(kinds('Prefixed', 'Prefixed'))).toBe('prefixed')
    expect(ratesScope(kinds('Cdi', 'Prefixed'))).toBe('both')
    expect(ratesScope(kinds('CdiPlus', 'Prefixed'))).toBe('both')
  })

  it('a nota de rodapé fala só do que foi usado', () => {
    const status = rates()
    expect(ratesFootnote(status)).toContain('CDI publicado pelo Banco Central até 15/10')
    expect(ratesFootnote(status, 'cdi')).not.toContain('pré-fixado')
    expect(ratesFootnote(status, 'prefixed')).toContain('taxa contratada')
    expect(ratesFootnote(status, 'prefixed')).not.toContain('Banco Central')
    expect(ratesFootnote(status, 'both')).toContain('Banco Central até 15/10')
    expect(ratesFootnote(status, 'both')).toContain('pré-fixado')
    for (const scope of ['cdi', 'prefixed', 'both'] as const) expect(ratesFootnote(status, scope)).toContain('o extrato do banco manda')
  })

  it('junta os aportes de todos os objetivos e dos soltos', () => {
    const d = (id: string) => ({ depositId: id }) as DepositBalance
    const balances = {
      goals: [{ deposits: [d('a'), d('b')] }, { deposits: [d('c')] }],
      unassigned: { deposits: [d('d')] },
    } as unknown as Balances
    expect(allDeposits(balances).map((x) => x.depositId)).toEqual(['a', 'b', 'c', 'd'])
  })
})

describe('progresso da meta', () => {
  it('sem meta, não há barra', () => {
    expect(goalProgress(500, null)).toBeNull()
    expect(goalProgress(500, 0)).toBeNull()
    expect(progressLabel(500, null)).toBeNull()
  })

  it('vai de 0 a 1 e não passa da meta', () => {
    expect(goalProgress(0, 1000)).toBe(0)
    expect(goalProgress(250, 1000)).toBe(0.25)
    expect(goalProgress(1500, 1000)).toBe(1)
  })

  it('o texto arredonda para baixo: 99,9% não vira 100%', () => {
    expect(progressLabel(999, 1000)).toBe('99% da meta')
    expect(progressLabel(1000, 1000)).toBe('100% da meta')
  })
})

describe('avisos das taxas', () => {
  it('taxas em dia e completas: nenhum aviso', () => {
    expect(ratesNotice(rates())).toBeNull()
  })

  it('sem taxa nenhuma', () => {
    expect(ratesNotice(rates({ available: false, through: null }))?.kind).toBe('unavailable')
  })

  it('defasada cita a data da última taxa', () => {
    const notice = ratesNotice(rates({ stale: true, through: '2026-10-10' }))
    expect(notice?.kind).toBe('stale')
    expect(notice?.text).toContain('10/10/2026')
  })

  it('incompleta tem texto próprio e vale mais que defasada', () => {
    const notice = ratesNotice(rates({ stale: true, complete: false }))
    expect(notice?.kind).toBe('incomplete')
    expect(notice?.text).toContain('Faltam taxas')
  })

  it('a nota fixa cita até quando vai o cálculo', () => {
    expect(ratesFootnote(rates())).toBe(
      'Cálculo pelo CDI publicado pelo Banco Central até 15/10/2026. Estimativa; o extrato do banco manda.',
    )
    expect(ratesFootnote(rates({ through: null }))).not.toContain('até')
  })
})

describe('período do gráfico', () => {
  it('1M e 3M mostram um ponto por dia; 6M, por semana', () => {
    expect(periodRange('1M', '2026-10-15', null)).toEqual({ from: '2026-09-14', step: 'day' })
    expect(periodRange('3M', '2026-10-15', null).step).toBe('day')
    expect(periodRange('6M', '2026-10-15', null).step).toBe('week')
  })

  it('"Tudo" começa no primeiro aporte e espaça conforme o tamanho', () => {
    expect(periodRange('ALL', '2026-10-15', '2026-09-01')).toEqual({ from: '2026-09-01', step: 'day' })
    expect(periodRange('ALL', '2026-10-15', '2026-03-01').step).toBe('week')
    expect(periodRange('ALL', '2026-10-15', '2024-01-01').step).toBe('month')
  })

  it('sem aporte, começa hoje', () => {
    expect(periodRange('ALL', '2026-10-15', null).from).toBe('2026-10-15')
  })
})

describe('textos', () => {
  it('singular e plural', () => {
    expect(depositCountLabel(1)).toBe('1 aporte')
    expect(depositCountLabel(3)).toBe('3 aportes')
  })
})

const group: GroupBalance = {
  depositCount: 2,
  unavailableCount: 0,
  principalCents: 1_000_000,
  grossBalanceCents: 1_100_000,
  grossYieldCents: 100_000,
  ratesThrough: '2026-10-15',
  estimatedTaxCents: 22_500,
  netYieldCents: 77_500,
  netBalanceCents: 1_077_500,
}

describe('bruto e líquido estimado', () => {
  it('o modo escolhe qual saldo e qual rendimento aparecem', () => {
    expect(shownBalance(group, 'gross')).toBe(1_100_000)
    expect(shownBalance(group, 'net')).toBe(1_077_500)
    expect(shownYield(group, 'gross')).toBe(100_000)
    expect(shownYield(group, 'net')).toBe(77_500)
  })

  it('o líquido é o bruto menos o imposto estimado', () => {
    expect(shownBalance(group, 'gross') - group.estimatedTaxCents).toBe(shownBalance(group, 'net'))
    expect(shownYield(group, 'gross') - group.estimatedTaxCents).toBe(shownYield(group, 'net'))
  })

  it('os rótulos nunca falam em lucro nem em ganho', () => {
    const texts = [...Object.values(yieldLabels), ...Object.values(balanceLabels), taxFootnote].join(' ').toLowerCase()
    expect(texts).not.toContain('lucro')
    expect(texts).not.toContain('ganh')
    expect(balanceLabels.net).toBe('Saldo líquido estimado')
  })

  const deposit = (over: Partial<DepositBalance>): DepositBalance => ({
    depositId: 'd',
    goalId: null,
    depositDate: '2026-07-21',
    principalCents: 100_000,
    productKind: 'Cdb',
    indexKind: 'Cdi',
    rateE4: 1_000_000,
    grossBalanceCents: 101_000,
    grossYieldCents: 1_000,
    factorE8: 101_000_000,
    ratesUsed: 10,
    ratesThrough: '2026-10-15',
    unavailable: null,
    tax: {
      iofCents: 0,
      irCents: 225,
      totalCents: 225,
      irRateBasisPoints: 2_250,
      holdingDays: 87,
      ruleVersion: 'v',
      netYieldCents: 775,
      netBalanceCents: 100_775,
    },
    ...over,
  })

  it('o rendimento do aporte segue o modo; sem taxa não há o que mostrar', () => {
    expect(depositYield(deposit({}), 'gross')).toBe(1_000)
    expect(depositYield(deposit({}), 'net')).toBe(775)
    expect(depositYield(deposit({ grossYieldCents: null, tax: null }), 'gross')).toBeNull()
    expect(depositYield(deposit({ grossYieldCents: null, tax: null }), 'net')).toBeNull()
  })

  it('a nota do aporte diz a alíquota e o prazo, e o IOF quando há', () => {
    expect(depositTaxNote(deposit({}).tax)).toBe('IR 22,5% · 87 dias')
    expect(depositTaxNote({ ...deposit({}).tax!, iofCents: 49, holdingDays: 1 })).toBe('IOF + IR 22,5% · 1 dia')
    expect(depositTaxNote(null)).toBeNull()
  })
})

describe('avisos de limitação', () => {
  it('diz o que a renda fixa ainda não faz', () => {
    expect(limitationsNote).toContain('CDB')
    expect(limitationsNote).toContain('CDB, LCI e LCA')
    expect(limitationsNote).toContain('percentual do CDI')
    expect(limitationsNote).toContain('o CDI mais uma taxa')
    expect(limitationsNote).toContain('taxa fixa ao ano')
    expect(limitationsNote).toContain('IPCA')
    expect(limitationsNote).toContain('resgates')
    expect(limitationsNote).toContain('projeção')
  })

  it('o aviso do imposto cita a tabela usada e que o banco é quem retém', () => {
    expect(taxFootnote).toContain('regressiva da B3')
    expect(taxFootnote).toContain('banco retém')
    expect(taxFootnote).toContain('IOF')
  })
})
