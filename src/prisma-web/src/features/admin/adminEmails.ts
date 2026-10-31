import { ApiError, type Schemas } from '@/lib/api'
import { peopleCount, type parseDays } from './adminPeople'

// Tela de e-mails do admin (docs/admin-email.md, 3.3 e 3.4): os textos e quando ela deixa enviar. Só leitura do que a API devolveu; quem
// decide é o servidor (CampaignGuard), e a tela avisa antes para o dono não descobrir só depois de tocar.
export type EmailTemplate = Schemas['EmailTemplateResponse']
export type EmailAudience = Schemas['EmailAudienceResponse']
export type EmailPreview = Schemas['EmailPreviewResponse']
export type EmailCampaign = Schemas['EmailCampaignResponse']

// Atalhos do campo de dias de cada modelo: só preenchem o número, a API aceita qualquer N de 1 a 365.
const quickDaysByTemplate: Record<string, readonly number[]> = {
  'confirm-reminder': [3, 7, 14],
  'miss-you': [30, 60, 90],
}

export const quickDaysFor = (code: string): readonly number[] => quickDaysByTemplate[code] ?? [7, 30, 90]

export const categoryLabel = (category: string) => (category === 'Account' ? 'E-mail de conta' : 'De relacionamento')

const dayWord = (days: number) => (days === 1 ? 'dia' : 'dias')

// "Contas sem acesso há N dias ou mais" com o número que o dono escolheu.
export const audienceLabel = (template: EmailTemplate, days: number) =>
  template.audience.replace('N dias', `${days} ${dayWord(days)}`)

// Por que o modelo não pode ir ao público agora; nulo se pode. A prévia e o teste continuam valendo.
export function blockedMessage(code: string | null | undefined): string | null {
  if (!code) return null
  if (code === 'UnsubscribeRequired')
    return 'Ainda não pode ser enviado: falta o link para a pessoa deixar de receber. A prévia e o teste para você funcionam.'
  return 'Este e-mail não pode ser enviado agora. A prévia e o teste para você funcionam.'
}

type Days = ReturnType<typeof parseDays>

// Quando o botão de enviar vale. `reason` é o que a tela diz; nulo quando só falta esperar a contagem. A ordem é a do servidor: o
// modelo bloqueado vem antes de olhar o público.
export function sendState(input: { template: EmailTemplate; days: Days; audience: EmailAudience | undefined }): {
  canSend: boolean
  reason: string | null
} {
  const { template, days, audience } = input
  const blocked = blockedMessage(template.blockedCode)
  if (blocked) return { canSend: false, reason: blocked }
  if ('problem' in days) return { canSend: false, reason: days.problem }
  if (!audience) return { canSend: false, reason: null }
  if (audience.count === 0) return { canSend: false, reason: 'Ninguém no público escolhido.' }
  if (audience.count > audience.cap)
    return {
      canSend: false,
      reason: `O público tem ${peopleCount(audience.count)} e o limite por disparo é ${audience.cap}. Escolha mais dias para reduzir.`,
    }
  return { canSend: true, reason: null }
}

export const confirmQuestion = (template: EmailTemplate, count: number) =>
  `Enviar "${template.name}" para ${peopleCount(count)}? Depois de enviado, não dá para desfazer.`

// O servidor já escreve a mensagem em pt-BR (e o motivo); a tela só acrescenta o que ela mesma faz depois do erro.
export function sendErrorMessage(error: unknown): string {
  if (error instanceof ApiError) {
    if (error.status === 429) return 'Muitos envios em pouco tempo. Aguarde um minuto e tente novamente.'
    if (error.code === 'AudienceChanged') return `${error.message} A contagem foi atualizada.`
    return error.message
  }
  return 'Não foi possível concluir. Tente novamente.'
}

// Uma linha do histórico: o modelo, o público e quantas pessoas. A hora vem formatada pela tela (formatInstant).
export function campaignLine(campaign: EmailCampaign, templates: readonly EmailTemplate[]): { title: string; detail: string } {
  const name = templates.find((t) => t.code === campaign.template)?.name ?? campaign.template
  return {
    title: name,
    detail: `Público de ${campaign.days} ${dayWord(campaign.days)} · ${peopleCount(campaign.recipients)}`,
  }
}
