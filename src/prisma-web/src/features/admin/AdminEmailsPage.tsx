import { Lock, Send } from 'lucide-react'
import { useEffect, useState } from 'react'
import { toast } from 'sonner'
import { StaleFade } from '@/components/StaleFade'
import { Alert, AlertDescription } from '@/components/ui/alert'
import { Button } from '@/components/ui/button'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'
import { Skeleton } from '@/components/ui/skeleton'
import { useDebouncedValue } from '@/lib/useDebouncedValue'
import { cn } from '@/lib/utils'
import { AdminBack } from './AdminBack'
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
  type EmailTemplate,
} from './adminEmails'
import { formatInstant } from './adminMarket'
import { parseDays, peopleCount } from './adminPeople'
import {
  useEmailAudience,
  useEmailCampaigns,
  useEmailPreview,
  useEmailTemplates,
  useSendEmailCampaign,
  useSendEmailTest,
} from './queries'

// Disparos de e-mail (docs/admin-email.md, 3.3 e 3.4): escolher o e-mail, ver para quantas pessoas vai, ver como fica, testar para si e
// só então enviar, confirmando a contagem. A tela nunca mostra quem são as pessoas.
export function AdminEmailsPage() {
  const templates = useEmailTemplates()

  return (
    <main className="mx-auto flex max-w-2xl flex-col gap-6 px-4 pt-3 pb-32">
      <AdminBack />
      <div>
        <h1 className="font-display text-3xl">E-mails</h1>
        <div className="spectrum-line mt-2 opacity-80" />
        <p className="mt-3 text-sm text-muted-foreground">
          Mande um e-mail a um grupo de pessoas. Você vê antes como ele fica e quantas pessoas recebem, e só sai quando você
          confirma.
        </p>
      </div>

      {templates.isPending && <Skeleton className="h-96 w-full rounded-3xl" />}
      {templates.isError && (
        <Alert variant="destructive">
          <AlertDescription>Não foi possível carregar os e-mails. Tente novamente.</AlertDescription>
        </Alert>
      )}
      {templates.isSuccess && <Composer templates={templates.data} />}
    </main>
  )
}

function Composer({ templates }: { templates: EmailTemplate[] }) {
  const [code, setCode] = useState(templates[0].code)
  const template = templates.find((t) => t.code === code) ?? templates[0]
  const [text, setText] = useState(String(template.defaultDays))
  // O pedido de envio que espera confirmação: a chave do pedido e a contagem que o dono viu, travadas até ele confirmar ou desistir.
  const [confirming, setConfirming] = useState<{ requestId: string; count: number } | null>(null)

  const days = parseDays(text)
  const requested = 'days' in days ? days.days : null
  // Só consulta depois de um instante sem digitar, para não ir à API a cada tecla.
  const debounced = useDebouncedValue(requested, 300)
  const audience = useEmailAudience(template.code, debounced)
  const state = sendState({ template, days, audience: audience.data })

  const choose = (next: EmailTemplate) => {
    setCode(next.code)
    setText(String(next.defaultDays))
    setConfirming(null)
  }

  return (
    <>
      <section className="flex flex-col gap-3" aria-label="Escolha o e-mail">
        <h2 className="text-xs text-muted-foreground">Qual e-mail</h2>
        <ul className="flex flex-col gap-3">
          {templates.map((item) => (
            <li key={item.code}>
              <TemplateChoice template={item} selected={item.code === template.code} onChoose={() => choose(item)} />
            </li>
          ))}
        </ul>
      </section>

      <section className="surface flex flex-col gap-4 rounded-3xl p-5" aria-label="Para quem">
        <div className="flex flex-col gap-2">
          <Label htmlFor="email-days" className="text-xs font-normal text-muted-foreground">
            Dias
          </Label>
          <Input
            id="email-days"
            inputMode="numeric"
            autoComplete="off"
            value={text}
            aria-invalid={'problem' in days}
            aria-describedby={'problem' in days ? 'email-days-problem' : undefined}
            className="h-11 rounded-xl text-base"
            onChange={(event) => {
              setText(event.target.value)
              setConfirming(null)
            }}
          />
          {'problem' in days && (
            <p id="email-days-problem" className="text-xs text-destructive">
              {days.problem}
            </p>
          )}
        </div>

        <div className="flex flex-wrap gap-2" role="group" aria-label="Atalhos de dias">
          {quickDaysFor(template.code).map((shortcut) => (
            <button
              key={shortcut}
              type="button"
              aria-pressed={requested === shortcut}
              onClick={() => {
                setText(String(shortcut))
                setConfirming(null)
              }}
              className={cn(
                'flex h-10 items-center rounded-full border px-3.5 text-sm whitespace-nowrap transition-colors active:scale-[0.97]',
                requested === shortcut ? 'spectrum-ring font-medium' : 'bg-card text-foreground/80 hover:text-foreground',
              )}
            >
              {shortcut} dias
            </button>
          ))}
        </div>

        {/* Com o campo inválido não há contagem a mostrar: a anterior seria de outro número de dias. */}
        {!('problem' in days) && audience.isPending && debounced !== null && <Skeleton className="h-24 w-full rounded-2xl" />}
        {!('problem' in days) && audience.isError && (
          <Alert variant="destructive">
            <AlertDescription>Não foi possível carregar a contagem. Tente novamente.</AlertDescription>
          </Alert>
        )}
        {!('problem' in days) && audience.data && (
          <StaleFade stale={audience.isPlaceholderData}>
            <AudienceSummary template={template} audience={audience.data} />
          </StaleFade>
        )}
      </section>

      <PreviewPanel code={template.code} />

      <Actions
        template={template}
        count={audience.data?.count}
        days={requested}
        canSend={state.canSend}
        reason={state.reason}
        confirming={confirming}
        setConfirming={setConfirming}
      />

      <History templates={templates} />
    </>
  )
}

function TemplateChoice({ template, selected, onChoose }: { template: EmailTemplate; selected: boolean; onChoose: () => void }) {
  const blocked = blockedMessage(template.blockedCode)
  return (
    <button
      type="button"
      aria-pressed={selected}
      onClick={onChoose}
      className={cn(
        'surface flex w-full flex-col gap-1.5 rounded-3xl border-2 border-transparent p-5 text-left transition-colors outline-none focus-visible:ring-3 focus-visible:ring-ring/50',
        selected ? 'spectrum-ring' : 'hover:bg-muted/40',
      )}
    >
      <span className="flex flex-wrap items-center gap-x-3 gap-y-1">
        <span className="font-display text-xl">{template.name}</span>
        <span className="rounded-full bg-muted px-2.5 py-0.5 text-xs text-muted-foreground">
          {categoryLabel(template.category)}
        </span>
      </span>
      <span className="text-sm text-muted-foreground">{template.description}</span>
      {blocked && (
        <span className="mt-1 flex items-start gap-1.5 text-xs text-muted-foreground">
          <Lock className="mt-0.5 size-3.5 shrink-0" aria-hidden />
          Só prévia e teste por enquanto.
        </span>
      )}
    </button>
  )
}

function AudienceSummary({ template, audience }: { template: EmailTemplate; audience: EmailAudience }) {
  return (
    <div className="flex flex-col gap-1">
      <p className="font-display text-5xl leading-none tabular-nums">{audience.count.toLocaleString('pt-BR')}</p>
      <p className="text-sm text-muted-foreground">{audienceLabel(template, audience.days)}</p>
      <p className="text-xs text-muted-foreground">Limite por disparo: {audience.cap.toLocaleString('pt-BR')} pessoas.</p>
    </div>
  )
}

// O e-mail com dados de exemplo, num quadro isolado: sem scripts e sem sair da página (`sandbox` vazio).
function PreviewPanel({ code }: { code: string }) {
  const preview = useEmailPreview(code)
  return (
    <section className="flex flex-col gap-3" aria-label="Como fica">
      <h2 className="text-xs text-muted-foreground">Como fica</h2>
      {preview.isPending && <Skeleton className="h-96 w-full rounded-3xl" />}
      {preview.isError && (
        <Alert variant="destructive">
          <AlertDescription>Não foi possível carregar a prévia. Tente novamente.</AlertDescription>
        </Alert>
      )}
      {preview.data && (
        <>
          <p className="text-sm">
            <span className="text-muted-foreground">Assunto: </span>
            {preview.data.subject}
          </p>
          <iframe
            title="Prévia do e-mail"
            sandbox=""
            referrerPolicy="no-referrer"
            srcDoc={preview.data.html}
            className="h-[600px] w-full rounded-3xl border border-border/60 bg-white"
          />
          <p className="text-xs text-muted-foreground">Com dados de exemplo: o endereço e o link não são de ninguém.</p>
        </>
      )}
    </section>
  )
}

function Actions({
  template,
  count,
  days,
  canSend,
  reason,
  confirming,
  setConfirming,
}: {
  template: EmailTemplate
  count: number | undefined
  days: number | null
  canSend: boolean
  reason: string | null
  confirming: { requestId: string; count: number } | null
  setConfirming: (value: { requestId: string; count: number } | null) => void
}) {
  const test = useSendEmailTest()
  const send = useSendEmailCampaign()
  const busy = test.isPending || send.isPending

  // Mudou o público (outra contagem) com a confirmação aberta: a pergunta de antes já não vale.
  useEffect(() => {
    if (confirming && count !== undefined && count !== confirming.count) setConfirming(null)
  }, [count, confirming, setConfirming])

  const sendTest = () =>
    test.mutate(template.code, {
      onSuccess: () =>
        toast.success('Teste enviado', { description: 'Confira a caixa de entrada do seu e-mail de administrador.' }),
      onError: (error) => toast.error(sendErrorMessage(error)),
    })

  const confirmSend = () => {
    if (!confirming || days === null) return
    send.mutate(
      { template: template.code, days, expectedCount: confirming.count, clientRequestId: confirming.requestId },
      {
        onSuccess: (campaign) =>
          toast.success(`E-mail na fila para ${peopleCount(campaign.recipients)}`, {
            description: 'Sai aos poucos, nos próximos minutos.',
          }),
        onError: (error) => toast.error(sendErrorMessage(error)),
        onSettled: () => setConfirming(null),
      },
    )
  }

  return (
    <section className="surface flex flex-col gap-4 rounded-3xl p-5" aria-label="Enviar">
      <Button variant="outline" className="h-11 rounded-full" disabled={busy} onClick={sendTest}>
        {test.isPending ? 'Enviando teste…' : 'Enviar teste para mim'}
      </Button>

      {confirming ? (
        <div className="flex flex-col gap-3 rounded-2xl bg-muted/50 p-4">
          <p className="text-sm">{confirmQuestion(template, confirming.count)}</p>
          <div className="flex gap-2">
            <Button className="h-11 flex-1 rounded-full" disabled={busy} onClick={confirmSend}>
              {send.isPending ? 'Enviando…' : 'Confirmar envio'}
            </Button>
            <Button variant="ghost" className="h-11 rounded-full" disabled={busy} onClick={() => setConfirming(null)}>
              Voltar
            </Button>
          </div>
        </div>
      ) : (
        <Button
          className="h-11 rounded-full"
          disabled={busy || !canSend || count === undefined}
          onClick={() => count !== undefined && setConfirming({ requestId: crypto.randomUUID(), count })}
        >
          <Send />
          {count !== undefined && canSend ? `Enviar para ${peopleCount(count)}` : 'Enviar'}
        </Button>
      )}

      {reason && !confirming && <p className="text-xs text-muted-foreground">{reason}</p>}
    </section>
  )
}

function History({ templates }: { templates: EmailTemplate[] }) {
  const campaigns = useEmailCampaigns()
  return (
    <section className="flex flex-col gap-3" aria-label="Últimos disparos">
      <h2 className="text-xs text-muted-foreground">Últimos disparos</h2>
      {campaigns.isPending && <Skeleton className="h-20 w-full rounded-3xl" />}
      {campaigns.isError && <p className="text-sm text-destructive">Não foi possível carregar o histórico.</p>}
      {campaigns.data?.length === 0 && <p className="text-sm text-muted-foreground">Nenhum disparo ainda.</p>}
      {campaigns.data && campaigns.data.length > 0 && (
        <ul className="surface flex flex-col rounded-3xl px-5">
          {campaigns.data.map((campaign) => {
            const line = campaignLine(campaign, templates)
            return (
              <li key={campaign.id} className="flex flex-col gap-0.5 border-b border-border/60 py-3 last:border-b-0">
                <span className="text-sm font-medium">{line.title}</span>
                <span className="text-xs text-muted-foreground">
                  {line.detail} · {formatInstant(campaign.createdAt)}
                </span>
              </li>
            )
          })}
        </ul>
      )}
    </section>
  )
}
