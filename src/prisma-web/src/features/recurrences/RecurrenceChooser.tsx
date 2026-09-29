import { ChevronRight, Repeat } from 'lucide-react'
import { FieldError } from '@/components/FieldError'
import { Input } from '@/components/ui/input'
import { Chip, ChipRow, Section } from '@/features/transactions/fields'
import { addDays } from '@/lib/dates'
import { cn } from '@/lib/utils'
import { noRepeat, type RepeatChoice } from './repeatChoice'
import { describeSeries, seriesLine, shortDate, type Frequency } from './schedule'

const frequencies: { value: Frequency; label: string }[] = [
  { value: 'Weekly', label: 'Toda semana' },
  { value: 'Monthly', label: 'Todo mês' },
]

// A linha compacta do Novo lançamento: "↻ Não se repete ›" ou a série escolhida.
export function RepeatRow({
  choice,
  start,
  today,
  onOpen,
}: {
  choice: RepeatChoice
  start: string
  today: string
  onOpen: () => void
}) {
  const repeats = choice.frequency !== null
  return (
    <button
      type="button"
      onClick={onOpen}
      className={cn(
        'flex min-h-12 w-full items-center gap-3 rounded-2xl border bg-card px-4 py-2.5 text-left text-sm transition-colors active:scale-[0.99]',
        repeats ? 'spectrum-ring font-medium' : 'text-foreground/80 hover:text-foreground',
      )}
    >
      <Repeat className="size-4 shrink-0 text-muted-foreground" aria-hidden />
      <span className="min-w-0 flex-1">
        {repeats ? seriesLine({ start, frequency: choice.frequency!, endDate: choice.endDate || null, today }) : 'Não se repete'}
      </span>
      <ChevronRight className="size-4 shrink-0 text-muted-foreground" aria-hidden />
    </button>
  )
}

// As escolhas do painel e o que a série vai fazer. withNone: o Novo lançamento pode voltar a "Não se
// repete"; no painel de um lançamento, desistir é fechar.
export function RecurrenceChooser({
  choice,
  onChange,
  start,
  today,
  withNone,
  error,
}: {
  choice: RepeatChoice
  onChange: (choice: RepeatChoice) => void
  start: string
  today: string
  withNone?: boolean
  error?: string
}) {
  const { frequency, endDate } = choice
  return (
    <div className="flex flex-col gap-6">
      <Section title="Frequência">
        <ChipRow>
          {withNone && (
            <Chip selected={frequency === null} onClick={() => onChange(noRepeat)}>
              Não se repete
            </Chip>
          )}
          {frequencies.map((f) => (
            <Chip key={f.value} selected={frequency === f.value} onClick={() => onChange({ frequency: f.value, endDate })}>
              {f.label}
            </Chip>
          ))}
        </ChipRow>
      </Section>

      {frequency !== null && (
        <Section title="Termina">
          <ChipRow>
            <Chip selected={endDate === null} onClick={() => onChange({ frequency, endDate: null })}>
              Nunca
            </Chip>
            <Chip selected={endDate !== null} onClick={() => endDate === null && onChange({ frequency, endDate: '' })}>
              Em uma data
            </Chip>
          </ChipRow>
          {endDate !== null && (
            <Input
              type="date"
              aria-label="Data do término"
              value={endDate}
              min={addDays(start, 1)}
              max="9999-12-31"
              onChange={(event) => onChange({ frequency, endDate: event.target.value })}
              className="h-11 rounded-xl"
            />
          )}
          <FieldError message={error} />
        </Section>
      )}

      {frequency !== null && <Preview start={start} frequency={frequency} endDate={endDate || null} today={today} />}
    </div>
  )
}

// O que acontece: a próxima data, as que já passaram (lançadas junto) e o dia que falta no mês.
function Preview({
  start,
  frequency,
  endDate,
  today,
}: {
  start: string
  frequency: Frequency
  endDate: string | null
  today: string
}) {
  const { dueNow, next } = describeSeries({ start, frequency, endDate, today })
  const day = Number(start.slice(8, 10))
  const dates = dueNow.map((d) => shortDate(d, today))
  return (
    <div className="surface flex flex-col gap-1.5 rounded-2xl px-4 py-3 text-sm">
      <p className="font-medium">{seriesLine({ start, frequency, endDate, today })}</p>
      {dueNow.length > 0 && (
        <p className="text-muted-foreground">
          {dueNow.length === 1
            ? `A de ${dates[0]}, que já passou, é lançada junto.`
            : dueNow.length <= 3
              ? `As de ${dates.slice(0, -1).join(', ')} e ${dates.at(-1)}, que já passaram, são lançadas junto.`
              : `As ${dueNow.length} que já passaram, de ${dates[0]} a ${dates.at(-1)}, são lançadas junto.`}
        </p>
      )}
      {next === null && <p className="text-muted-foreground">Depois disso, nada mais é lançado.</p>}
      {frequency === 'Monthly' && day > 28 && (
        <p className="text-muted-foreground">Nos meses sem dia {day}, no último dia do mês.</p>
      )}
      <p className="text-muted-foreground">Cada uma é lançada no dia dela.</p>
    </div>
  )
}
