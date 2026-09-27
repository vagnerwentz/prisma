import { describe, expect, it, vi } from 'vitest'
import { ApiError } from './api'
import { buildReport, createReporter, isReportable, type ClientErrorReport } from './clientErrors'

// Erros do navegador relatados ao servidor (etapa H.3b): só mensagem, pilha, tela, tipo e versão;
// nada do que estava na tela; cada erro uma vez; o que não é bug fica de fora.
describe('buildReport', () => {
  it('leva só os campos permitidos, com a tela sem consulta nem fragmento', () => {
    const error = new TypeError("Cannot read properties of undefined (reading 'name')")

    const report = buildReport(error, 'crash', '/lancar?estorno=01a0#topo', '2026-09-26T23:59Z')

    expect(Object.keys(report).sort()).toEqual(['appVersion', 'kind', 'message', 'screen', 'stack'])
    expect(report).toMatchObject({
      message: "Cannot read properties of undefined (reading 'name')",
      screen: '/lancar',
      kind: 'crash',
      appVersion: '2026-09-26T23:59Z',
    })
    expect(report.stack).toContain('TypeError')
  })

  it('corta mensagem e pilha nos limites que a API aceita', () => {
    const error = new Error('x'.repeat(5000))
    error.stack = 'y'.repeat(20000)

    const report = buildReport(error, 'silent', '/', 'v')

    expect(report.message).toHaveLength(2000)
    expect(report.stack).toHaveLength(8000)
  })

  it('valor que não é um Error não tem o conteúdo enviado', () => {
    const report = buildReport({ amountCents: 12990, description: 'Mercado' }, 'silent', '/', 'v')

    expect(report.message).toBe('Non-error value (object)')
    expect(JSON.stringify(report)).not.toContain('Mercado')
    expect(report.stack).toBeNull()
  })
})

describe('isReportable', () => {
  it('não relata o que não é bug', () => {
    expect(isReportable(new ApiError(500, { detail: 'Algo deu errado' }))).toBe(false) // o servidor já registrou
    expect(isReportable(new TypeError('Failed to fetch dynamically imported module: /assets/x.js'))).toBe(false) // versão nova
    expect(isReportable(new TypeError('Failed to fetch'))).toBe(false) // sem conexão (Chrome)
    expect(isReportable(new TypeError('Load failed'))).toBe(false) // sem conexão (Safari)
  })

  it('relata o bug', () => {
    expect(isReportable(new TypeError("Cannot read properties of undefined (reading 'x')"))).toBe(true)
  })
})

describe('createReporter', () => {
  const setup = (send = vi.fn<(report: ClientErrorReport) => Promise<unknown>>().mockResolvedValue(undefined)) => ({
    send,
    report: createReporter(send, () => '/lancar', 'v1'),
  })

  it('envia cada erro uma vez por carregamento da página', () => {
    const { send, report } = setup()
    const error = new Error('boom')

    report(error, 'crash')
    report(error, 'silent')
    report(new Error('outro'), 'silent')

    expect(send).toHaveBeenCalledTimes(2)
    expect(send.mock.calls.map(([sent]) => sent.message)).toEqual(['boom', 'outro'])
  })

  it('não envia o que não é bug', () => {
    const { send, report } = setup()

    report(new ApiError(409, { detail: 'Conflito' }), 'silent')

    expect(send).not.toHaveBeenCalled()
  })

  it('falha no envio não vira outro erro', async () => {
    const send = vi.fn().mockRejectedValue(new TypeError('Failed to fetch'))
    const { report } = setup(send)

    await expect(report(new Error('boom'), 'crash')).resolves.toBeUndefined()
    expect(send).toHaveBeenCalledOnce()
  })
})
