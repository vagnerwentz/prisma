import { describe, expect, it } from 'vitest'
import { ApiError } from '@/lib/api'
import {
  audienceLabel,
  blockedMessage,
  campaignLine,
  categoryLabel,
  confirmQuestion,
  quickDaysFor,
  sendErrorMessage,
  sendState,
  type EmailAudience,
  type EmailCampaign,
  type EmailTemplate,
} from './adminEmails'
import { parseDays } from './adminPeople'

// docs/admin-email.md, 3.3 e 3.4 (AE.5): o que a tela de e-mails diz e quando deixa enviar. Quem decide de verdade é o servidor
// (CampaignGuard); a tela só avisa antes, para o dono não descobrir só depois de tocar.
const reminder: EmailTemplate = {
  code: 'confirm-reminder',
  name: 'Falta confirmar o e-mail',
  description: 'Lembra quem criou a conta e não confirmou o e-mail.',
  category: 'Account',
  audience: 'Contas sem o e-mail confirmado há N dias ou mais',
  defaultDays: 3,
  minDaysBetween: null,
  sendable: true,
  blockedCode: null,
}

const missYou: EmailTemplate = {
  ...reminder,
  code: 'miss-you',
  name: 'Sentimos sua falta',
  category: 'Relationship',
  audience: 'Contas sem acesso há N dias ou mais',
  defaultDays: 30,
  minDaysBetween: 30,
  sendable: false,
  blockedCode: 'UnsubscribeRequired',
}

const audience = (count: number, cap = 50): EmailAudience => ({ template: 'confirm-reminder', days: 3, count, cap })

describe('quickDaysFor', () => {
  it('has shortcuts that fit each template', () => {
    expect(quickDaysFor('confirm-reminder')).toEqual([3, 7, 14])
    expect(quickDaysFor('miss-you')).toEqual([30, 60, 90])
  })

  it('falls back to 7, 30 and 90 for a template it does not know', () => {
    expect(quickDaysFor('novidade')).toEqual([7, 30, 90])
  })

  it('only offers days the server accepts', () => {
    for (const code of ['confirm-reminder', 'miss-you', 'novidade'])
      for (const days of quickDaysFor(code)) expect(parseDays(String(days))).toEqual({ days })
  })
})

describe('labels', () => {
  it('names the two categories the owner sees', () => {
    expect(categoryLabel('Account')).toBe('E-mail de conta')
    expect(categoryLabel('Relationship')).toBe('De relacionamento')
  })

  it('writes the audience with the chosen days and the right plural', () => {
    expect(audienceLabel(reminder, 30)).toBe('Contas sem o e-mail confirmado há 30 dias ou mais')
    expect(audienceLabel(reminder, 1)).toBe('Contas sem o e-mail confirmado há 1 dia ou mais')
    expect(audienceLabel(missYou, 90)).toBe('Contas sem acesso há 90 dias ou mais')
  })
})

describe('blockedMessage', () => {
  it('says nothing about a template that can go', () => {
    expect(blockedMessage(null)).toBeNull()
    expect(blockedMessage(undefined)).toBeNull()
  })

  it('explains the missing unsubscribe and that preview and test still work', () => {
    const message = blockedMessage('UnsubscribeRequired')!
    expect(message).toContain('deixar de receber')
    expect(message).toContain('prévia')
  })

  it('still blocks for a reason it does not know', () => {
    expect(blockedMessage('OutraTrava')).toBeTruthy()
  })
})

describe('sendState', () => {
  const days = parseDays('3')

  it('lets a template that can go be sent to exactly the people counted', () => {
    expect(sendState({ template: reminder, days, audience: audience(12) })).toEqual({ canSend: true, reason: null })
  })

  it('blocks a relationship template, even with an audience', () => {
    const state = sendState({ template: missYou, days: parseDays('30'), audience: audience(12) })

    expect(state.canSend).toBe(false)
    expect(state.reason).toBe(blockedMessage('UnsubscribeRequired'))
  })

  it('blocks invalid days with the range', () => {
    const state = sendState({ template: reminder, days: parseDays('0'), audience: audience(12) })

    expect(state.canSend).toBe(false)
    expect(state.reason).toContain('365')
  })

  it('waits for the count without scolding', () => {
    expect(sendState({ template: reminder, days, audience: undefined })).toEqual({ canSend: false, reason: null })
  })

  it('blocks an empty audience', () => {
    const state = sendState({ template: reminder, days, audience: audience(0) })

    expect(state.canSend).toBe(false)
    expect(state.reason).toBe('Ninguém no público escolhido.')
  })

  it('blocks an audience over the cap and tells how to shrink it', () => {
    const state = sendState({ template: reminder, days, audience: audience(51, 50) })

    expect(state.canSend).toBe(false)
    expect(state.reason).toContain('51 pessoas')
    expect(state.reason).toContain('50')
    expect(state.reason).toContain('mais dias')
  })

  it('lets an audience exactly at the cap go', () => {
    expect(sendState({ template: reminder, days, audience: audience(50, 50) }).canSend).toBe(true)
  })

  it('puts the block of the template before everything else', () => {
    const state = sendState({ template: missYou, days: parseDays('0'), audience: audience(0) })

    expect(state.reason).toBe(blockedMessage('UnsubscribeRequired'))
  })
})

describe('confirmQuestion', () => {
  it('names the email and the exact head count, and says it cannot be undone', () => {
    const question = confirmQuestion(reminder, 12)

    expect(question).toContain('Falta confirmar o e-mail')
    expect(question).toContain('12 pessoas')
    expect(question).toContain('não dá para desfazer')
  })

  it('uses the singular for one person', () => {
    expect(confirmQuestion(reminder, 1)).toContain('1 pessoa')
    expect(confirmQuestion(reminder, 1)).not.toContain('1 pessoas')
  })
})

describe('sendErrorMessage', () => {
  it('keeps what the server says for a count that changed, and says the count was refreshed', () => {
    const error = new ApiError(409, {
      detail: 'O público mudou: agora são 4 pessoas. Confira e envie de novo.',
      code: 'AudienceChanged',
    })

    const message = sendErrorMessage(error)

    expect(message).toContain('O público mudou: agora são 4 pessoas')
    expect(message).toContain('atualizada')
  })

  it('explains the rate limit', () => {
    const message = sendErrorMessage(new ApiError(429, undefined))

    expect(message).toContain('Muitos envios')
    expect(message).toContain('Aguarde')
  })

  it('keeps the message of the other errors from the server', () => {
    const error = new ApiError(400, {
      detail: 'O público tem 60 pessoas e o limite por disparo é 50.',
      code: 'TooManyRecipients',
    })

    expect(sendErrorMessage(error)).toBe('O público tem 60 pessoas e o limite por disparo é 50.')
  })

  it('has a message for an error that is not from the server', () => {
    expect(sendErrorMessage(new Error('Failed to fetch'))).toBe('Não foi possível concluir. Tente novamente.')
  })
})

describe('campaignLine', () => {
  const campaign: EmailCampaign = {
    id: 'c1',
    template: 'confirm-reminder',
    days: 3,
    recipients: 12,
    createdAt: '2026-10-31T15:00:00Z',
  }

  it('describes a send with the template name, the days and how many people', () => {
    const line = campaignLine(campaign, [reminder, missYou])

    expect(line.title).toBe('Falta confirmar o e-mail')
    expect(line.detail).toContain('3 dias')
    expect(line.detail).toContain('12 pessoas')
  })

  it('uses the code when the template is no longer in the catalog', () => {
    expect(campaignLine({ ...campaign, template: 'antigo' }, [reminder]).title).toBe('antigo')
  })

  it('uses the singular for a single day', () => {
    expect(campaignLine({ ...campaign, days: 1, recipients: 1 }, [reminder]).detail).toContain('1 dia')
  })
})
