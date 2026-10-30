import { useState } from 'react'
import { Alert, AlertDescription } from '@/components/ui/alert'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'
import { Skeleton } from '@/components/ui/skeleton'
import { StaleFade } from '@/components/StaleFade'
import { useDebouncedValue } from '@/lib/useDebouncedValue'
import { cn } from '@/lib/utils'
import { AdminBack } from './AdminBack'
import { activeLabel, defaultDays, inactiveLabel, parseDays, quickDays, reliabilityNote, type UserSummary } from './adminPeople'
import { useUserSummary } from './queries'

// Contagem de pessoas (docs/admin-email.md, 3.1): quantas contas há e quantas estão sem acessar o app há N dias, com N escolhido
// aqui. Só números, nunca quem é cada pessoa.
export function AdminPeoplePage() {
  const [text, setText] = useState(String(defaultDays))
  const parsed = parseDays(text)
  const problem = 'problem' in parsed ? parsed.problem : null
  const requested = 'days' in parsed ? parsed.days : null
  // Só consulta depois de um instante sem digitar, para não ir à API a cada tecla.
  const days = useDebouncedValue(requested, 300)
  const summary = useUserSummary(days)

  return (
    <main className="mx-auto flex max-w-2xl flex-col gap-6 px-4 pt-3 pb-32">
      <AdminBack />
      <div>
        <h1 className="font-display text-3xl">Pessoas</h1>
        <div className="spectrum-line mt-2 opacity-80" />
        <p className="mt-3 text-sm text-muted-foreground">
          As contas do Prisma, só em números. Escolha há quantos dias a pessoa não acessa para ver quantas ficam de fora.
        </p>
      </div>

      <section className="surface flex flex-col gap-4 rounded-3xl p-5">
        <div className="flex flex-col gap-2">
          <Label htmlFor="inactive-days" className="text-xs font-normal text-muted-foreground">
            Dias sem acesso
          </Label>
          <Input
            id="inactive-days"
            inputMode="numeric"
            autoComplete="off"
            value={text}
            aria-invalid={problem !== null}
            aria-describedby={problem ? 'inactive-days-problem' : undefined}
            className="h-11 rounded-xl text-base"
            onChange={(event) => setText(event.target.value)}
          />
          {problem && (
            <p id="inactive-days-problem" className="text-xs text-destructive">
              {problem}
            </p>
          )}
        </div>

        <div className="flex flex-wrap gap-2" role="group" aria-label="Atalhos de dias">
          {quickDays.map((shortcut) => {
            const selected = requested === shortcut
            return (
              <button
                key={shortcut}
                type="button"
                aria-pressed={selected}
                onClick={() => setText(String(shortcut))}
                className={cn(
                  'flex h-10 items-center rounded-full border px-3.5 text-sm whitespace-nowrap transition-colors active:scale-[0.97]',
                  selected ? 'spectrum-ring font-medium' : 'bg-card text-foreground/80 hover:text-foreground',
                )}
              >
                {shortcut} dias
              </button>
            )
          })}
        </div>
      </section>

      {/* Com o campo inválido não há contagem a mostrar: a anterior seria de outro número de dias. */}
      {!problem && summary.isPending && days !== null && <Skeleton className="h-64 w-full rounded-3xl" />}
      {!problem && summary.isError && (
        <Alert variant="destructive">
          <AlertDescription>Não foi possível carregar a contagem. Tente novamente.</AlertDescription>
        </Alert>
      )}
      {!problem && summary.data && (
        <StaleFade stale={summary.isPlaceholderData}>
          <SummaryCard summary={summary.data} />
        </StaleFade>
      )}
    </main>
  )
}

function SummaryCard({ summary }: { summary: UserSummary }) {
  const rows: [string, number][] = [
    [activeLabel(summary.days), summary.active],
    ['Confirmaram o e-mail e nunca usaram', summary.neverUsed],
    ['Ainda sem confirmar o e-mail', summary.unconfirmed],
    ['Contas no total', summary.accounts],
  ]

  return (
    <div className="surface flex flex-col gap-5 rounded-3xl p-5">
      <div className="flex flex-col gap-1">
        <p className="font-display text-5xl leading-none tabular-nums">{summary.inactive.toLocaleString('pt-BR')}</p>
        <p className="text-sm text-muted-foreground">{inactiveLabel(summary.days)}</p>
      </div>

      <dl className="flex flex-col border-t border-border/60">
        {rows.map(([label, value]) => (
          <div key={label} className="flex items-baseline justify-between gap-4 border-b border-border/60 py-3 last:border-b-0">
            <dt className="text-sm">{label}</dt>
            <dd className="text-sm font-medium tabular-nums">{value.toLocaleString('pt-BR')}</dd>
          </div>
        ))}
      </dl>

      <p className="text-xs text-muted-foreground">{reliabilityNote(summary.days)}</p>
    </div>
  )
}
