import { describe, expect, it } from 'vitest'
import { ApiError } from '@/lib/api'
import {
  formatInstant,
  formatWindow,
  isFailure,
  nextRunText,
  runSummary,
  runToast,
  scheduleText,
  seriesState,
  stateDescription,
  suggestedWindow,
  syncErrorMessage,
  windowProblem,
  type MarketRun,
  type MarketSeries,
} from './adminMarket'

// docs/admin-mercado.md, seção 7: o que a tela diz em cada situação. Os limites da janela são os mesmos de SyncWindow no domínio.
const series: MarketSeries = {
  code: 'cdi',
  name: 'CDI diário',
  source: 'bcb-sgs-12',
  count: 800,
  first: '2023-01-02',
  last: '2026-10-16',
  expectedLast: '2026-10-16',
  stale: false,
  complete: true,
  missingBusinessDayCount: 0,
  lastFetchedAt: '2026-10-19T15:00:00Z',
  lastRevisedAt: null,
  schedule: { runTimes: ['19:30', '23:30'], nextRunUtc: '2026-10-19T22:30:00Z', enabled: true },
  lastRun: null,
  lastSuccessfulRun: null,
}

const run: MarketRun = {
  id: 'r1',
  series: 'cdi',
  kind: 'Manual',
  startedAt: '2026-10-19T22:30:00Z',
  finishedAt: '2026-10-19T22:30:02Z',
  outcome: 'Succeeded',
  windowFrom: '2026-10-09',
  windowTo: '2026-10-19',
  fetched: 8,
  added: 2,
  revised: 0,
  unchanged: 6,
  sourceErrorCode: null,
  errorSummary: null,
}

describe('seriesState', () => {
  it('sem nenhuma taxa é "empty", mesmo defasado e incompleto', () => {
    expect(seriesState({ count: 0, stale: true, complete: true })).toBe('empty')
  })
  it('defasado vale mais que incompleto', () => {
    expect(seriesState({ count: 5, stale: true, complete: false })).toBe('stale')
  })
  it('incompleto', () => {
    expect(seriesState({ count: 5, stale: false, complete: false })).toBe('incomplete')
  })
  it('em dia', () => {
    expect(seriesState({ count: 5, stale: false, complete: true })).toBe('ok')
  })
})

describe('stateDescription', () => {
  it('em dia diz a última data', () => {
    expect(stateDescription(series)).toBe('A última taxa é de 16/10/2026.')
  })
  it('defasado diz a última e a esperada', () => {
    expect(stateDescription({ ...series, stale: true, last: '2026-10-14' })).toBe(
      'A última taxa é de 14/10/2026; já deveria haver a de 16/10/2026.',
    )
  })
  it('incompleto concorda no singular e no plural', () => {
    expect(stateDescription({ ...series, complete: false, missingBusinessDayCount: 1 })).toBe(
      'Falta a taxa de 1 dia útil no meio do histórico.',
    )
    expect(stateDescription({ ...series, complete: false, missingBusinessDayCount: 3 })).toBe(
      'Faltam as taxas de 3 dias úteis no meio do histórico.',
    )
  })
  it('sem taxas', () => {
    expect(stateDescription({ ...series, count: 0, first: null, last: null })).toBe('Ainda não há nenhuma taxa gravada.')
  })
})

describe('nextRunText', () => {
  const now = new Date('2026-10-19T15:00:00Z') // 12h em São Paulo
  it('hoje às 19h30', () => {
    expect(nextRunText(series.schedule, now)).toBe('Próxima execução automática: hoje às 19h30.')
  })
  it('amanhã', () => {
    expect(nextRunText({ ...series.schedule, nextRunUtc: '2026-10-20T22:30:00Z' }, now)).toBe(
      'Próxima execução automática: amanhã às 19h30.',
    )
  })
  it('mais adiante, com a data', () => {
    expect(nextRunText({ ...series.schedule, nextRunUtc: '2026-10-22T22:30:00Z' }, now)).toBe(
      'Próxima execução automática: 22/10 às 19h30.',
    )
  })
  it('o dia é o de São Paulo, não o de Greenwich: 23h30 de lá já é o dia seguinte aqui', () => {
    expect(nextRunText({ ...series.schedule, nextRunUtc: '2026-10-20T02:30:00Z' }, now)).toBe(
      'Próxima execução automática: hoje às 23h30.',
    )
  })
  it('tarefa desligada', () => {
    expect(nextRunText({ ...series.schedule, enabled: false }, now)).toBe('A execução automática está desligada.')
  })
})

describe('scheduleText', () => {
  it('lista os horários e diz que roda ao subir', () => {
    expect(scheduleText(series.schedule)).toBe(
      'Roda todo dia às 19h30 e 23h30 (São Paulo) e ao subir a API, se o histórico estiver vazio ou atrasado.',
    )
  })
})

describe('formatInstant e formatWindow', () => {
  it('o instante é em São Paulo', () => {
    expect(formatInstant('2026-10-19T22:30:00Z')).toBe('19/10/2026 19:30')
    expect(formatInstant('2026-10-20T02:05:00Z')).toBe('19/10/2026 23:05')
  })
  it('a janela de um dia só mostra a data uma vez', () => {
    expect(formatWindow('2026-10-19', '2026-10-19')).toBe('19/10/2026')
    expect(formatWindow('2026-10-01', '2026-10-16')).toBe('01/10/2026 a 16/10/2026')
  })
})

describe('runSummary', () => {
  it('deu certo: as contagens, e as revisadas só quando houve', () => {
    expect(runSummary(run)).toBe('8 lidas · 2 novas')
    expect(runSummary({ ...run, revised: 1 })).toBe('8 lidas · 2 novas · 1 revisada')
    expect(runSummary({ ...run, revised: 3 })).toBe('8 lidas · 2 novas · 3 revisadas')
  })
  it('falha da fonte com o código do Banco Central', () => {
    expect(
      runSummary({
        ...run,
        outcome: 'SourceUnavailable',
        errorSummary: 'O Banco Central não respondeu como esperado.',
        sourceErrorCode: '7240913300019',
      }),
    ).toBe('O Banco Central não respondeu como esperado. Código de erro do Banco Central: 7240913300019.')
  })
  it('falha sem código', () => {
    expect(runSummary({ ...run, outcome: 'Failed', errorSummary: 'A sincronização falhou por um erro interno.' })).toBe(
      'A sincronização falhou por um erro interno.',
    )
  })
  it('sem texto do servidor, cai no rótulo do desfecho', () => {
    expect(runSummary({ ...run, outcome: 'TimedOut', errorSummary: null })).toBe('Passou do tempo')
  })
  it('só o que deu certo não é falha', () => {
    expect(isFailure(run)).toBe(false)
    expect(isFailure({ outcome: 'TimedOut' })).toBe(true)
    expect(isFailure({ outcome: 'SourceUnavailable' })).toBe(true)
    expect(isFailure({ outcome: 'Failed' })).toBe(true)
  })
})

describe('runToast', () => {
  it('sem novidade', () => {
    expect(runToast({ ...run, added: 0, revised: 0 })).toBe('Sincronizado: nenhuma taxa nova.')
  })
  it('com novidade', () => {
    expect(runToast(run)).toBe('Sincronizado: 2 novas, 0 revisadas.')
    expect(runToast({ ...run, added: 0, revised: 1 })).toBe('Sincronizado: 0 novas, 1 revisadas.')
  })
  it('falha mostra o motivo', () => {
    expect(runToast({ ...run, outcome: 'Failed', errorSummary: 'A sincronização falhou por um erro interno.' })).toBe(
      'A sincronização falhou por um erro interno.',
    )
  })
})

describe('syncErrorMessage', () => {
  it('o texto do servidor e o código do Banco Central', () => {
    const error = new ApiError(502, {
      detail: 'O Banco Central não respondeu como esperado.',
      code: 'SourceUnavailable',
      sourceErrorCode: '7240913300019',
    })
    expect(syncErrorMessage(error)).toBe(
      'O Banco Central não respondeu como esperado. Código de erro do Banco Central: 7240913300019.',
    )
  })
  it('sem código, só o texto', () => {
    expect(syncErrorMessage(new ApiError(504, { detail: 'A sincronização passou do tempo limite.', code: 'SyncTimedOut' }))).toBe(
      'A sincronização passou do tempo limite.',
    )
  })
  it('erro que não é da API cai no texto padrão', () => {
    expect(syncErrorMessage(null)).toBe('Não foi possível concluir. Tente novamente.')
  })
})

describe('windowProblem', () => {
  const today = '2026-10-19'
  it('janela boa', () => {
    expect(windowProblem('2026-10-01', '2026-10-16', today)).toBeNull()
    expect(windowProblem(today, today, today)).toBeNull()
  })
  it('falta data', () => {
    expect(windowProblem('', '2026-10-16', today)).toBe('Escolha as duas datas.')
    expect(windowProblem('2026-10-01', '', today)).toBe('Escolha as duas datas.')
  })
  it('início depois do fim', () => {
    expect(windowProblem('2026-10-10', '2026-10-09', today)).toBe('A data inicial não pode ser depois da final.')
  })
  it('fim no futuro', () => {
    expect(windowProblem('2026-10-01', '2026-10-20', today)).toBe('A data final não pode estar no futuro.')
  })
  it('10 anos vale, 10 anos e um dia não', () => {
    expect(windowProblem('2016-10-19', '2026-10-19', today)).toBeNull()
    expect(windowProblem('2016-10-18', '2026-10-19', today)).toBe('O período pode ter no máximo 10 anos.')
  })
  it('29/02 + 10 anos acompanha o calendário, como no servidor', () => {
    expect(windowProblem('2016-02-29', '2026-02-28', today)).toBeNull()
    expect(windowProblem('2016-02-29', '2026-03-01', today)).toBe('O período pode ter no máximo 10 anos.')
    expect(windowProblem('2012-02-29', '2022-02-28', today)).toBeNull()
  })
})

describe('suggestedWindow', () => {
  it('os últimos 30 dias, terminando hoje', () => {
    expect(suggestedWindow('2026-10-19')).toEqual({ from: '2026-09-19', to: '2026-10-19' })
  })
})
