import { describe, expect, it } from 'vitest'
import {
  mainReason,
  monthNameOf,
  reasonDetail,
  reasonTitle,
  signedChange,
  variationHeadline,
  type VariationReason,
} from './variation'

// Etapa 2.10 (docs/fase-2.md, 2.7), com os números do exemplo: setembro contra agosto.
const reason = (overrides: Partial<VariationReason>): VariationReason => ({
  kind: 'Category',
  categoryId: null,
  name: null,
  icon: null,
  color: null,
  changeCents: 0,
  largest: null,
  started: [],
  ended: [],
  ...overrides,
})

const inherited = reason({
  kind: 'Inherited',
  changeCents: 60000,
  started: [{ purchaseId: 'p', description: 'Passagem', amountCents: 60000 }],
})
const leisure = reason({ categoryId: 'l', name: 'Lazer', changeCents: -45000 })
const health = reason({
  categoryId: 'h',
  name: 'Saúde',
  changeCents: 15000,
  largest: { transactionId: 't', description: 'Farmácia', amountCents: 15000 },
})

describe('monthNameOf', () => {
  it('é o nome do mês em minúsculas', () => {
    expect(monthNameOf('2026-08')).toBe('agosto')
  })
})

describe('signedChange', () => {
  it('leva sempre o sinal, com o "−" tipográfico', () => {
    expect(signedChange(60000)).toBe('+R$ 600,00')
    expect(signedChange(-45000)).toBe('−R$ 450,00')
  })
})

describe('variationHeadline', () => {
  it('traz a porcentagem do comparativo e a diferença em reais', () => {
    expect(variationHeadline(275000, 260000, 'agosto')).toBe('Gastou 6% a mais que em agosto (+R$ 150,00)')
    expect(variationHeadline(260000, 275000, 'setembro')).toBe('Gastou 5% a menos que em setembro (−R$ 150,00)')
  })

  it('sem gasto no mês anterior, só o valor', () => {
    expect(variationHeadline(275000, 0, 'agosto')).toBe('Gastou R$ 2.750,00 a mais que em agosto')
  })

  it('igual, ou quase igual quando a porcentagem arredonda para zero', () => {
    expect(variationHeadline(260000, 260000, 'agosto')).toBe('Gastou o mesmo que em agosto')
    expect(variationHeadline(260100, 260000, 'agosto')).toBe('Gastou quase o mesmo que em agosto (+R$ 1,00)')
  })

  it('não existe quando os dois meses não têm gasto', () => {
    expect(variationHeadline(0, 0, 'agosto')).toBeNull()
  })
})

describe('reasonTitle', () => {
  it('nomeia cada tipo de motivo', () => {
    expect(reasonTitle(inherited)).toBe('Parcelas de compras anteriores')
    expect(reasonTitle(leisure)).toBe('Lazer')
    expect(reasonTitle(reason({ changeCents: 100 }))).toBe('Sem categoria')
    expect(reasonTitle(reason({ kind: 'OtherCategories', changeCents: 100 }))).toBe('Outras categorias')
  })
})

describe('reasonDetail', () => {
  it('categoria que subiu mostra o maior lançamento; a que caiu, nada', () => {
    expect(reasonDetail(health)).toBe('O maior foi Farmácia, R$ 150,00')
    expect(reasonDetail(leisure)).toBeNull()
  })

  it('lançamento sem descrição usa o nome da categoria', () => {
    expect(reasonDetail({ ...health, largest: { transactionId: 't', description: '', amountCents: 15000 } })).toBe(
      'O maior foi Saúde, R$ 150,00',
    )
  })

  it('parcelas dizem as compras que começaram e as que terminaram', () => {
    expect(reasonDetail(inherited, 'agosto')).toBe('Começou: Passagem')
    const ended = [
      { purchaseId: 'a', description: 'TV', amountCents: 40000 },
      { purchaseId: 'b', description: 'Sofá', amountCents: 30000 },
      { purchaseId: 'c', description: 'Celular', amountCents: 20000 },
    ]
    expect(reasonDetail({ ...inherited, ended }, 'agosto')).toBe('Começou: Passagem · Terminaram em agosto: TV, Sofá e mais 1')
    expect(reasonDetail({ ...inherited, started: [], ended: ended.slice(0, 2) }, 'agosto')).toBe(
      'Terminaram em agosto: TV e Sofá',
    )
    expect(reasonDetail({ ...inherited, started: [] }, 'agosto')).toBeNull()
  })
})

describe('mainReason', () => {
  it('é o maior motivo na mesma direção da diferença', () => {
    expect(mainReason([inherited, leisure, health], 15000)).toBe('Principalmente parcelas de compras anteriores, +R$ 600,00')
    expect(mainReason([inherited, leisure, health], -1000)).toBe('Principalmente Lazer, −R$ 450,00')
  })

  it('não existe sem diferença ou sem motivo na direção dela', () => {
    expect(mainReason([inherited, leisure], 0)).toBeNull()
    expect(mainReason([leisure], 5000)).toBeNull()
  })

  it('outras categorias e sem categoria em minúsculas no meio da frase', () => {
    expect(mainReason([reason({ kind: 'OtherCategories', changeCents: 900 })], 900)).toBe(
      'Principalmente outras categorias, +R$ 9,00',
    )
    expect(mainReason([reason({ changeCents: 900 })], 900)).toBe('Principalmente lançamentos sem categoria, +R$ 9,00')
  })
})
