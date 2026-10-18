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
  type DepositBalance,
  type GroupBalance,
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
    indexPercentE4: 1_000_000,
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
    expect(limitationsNote).toContain('percentual do CDI')
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
