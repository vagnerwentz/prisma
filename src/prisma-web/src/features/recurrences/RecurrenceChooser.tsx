import { ChevronRight, Repeat } from 'lucide-react'
import { FieldError } from '@/components/FieldError'
import { Input } from '@/components/ui/input'
import { Chip, ChipRow, Section } from '@/features/transactions/fields'
import { addDays } from '@/lib/dates'
import { cn } from '@/lib/utils'
import { autoDebitLine, dueDateChoices } from './autoDebit'
import { noRepeat, type RepeatChoice } from './repeatChoice'
import { describeSeries, seriesLine, shortDate, type Frequency } from './schedule'

const frequencies: { value: Frequency; label: string }[] = [
  { value: 'Weekly', label: 'Toda semana' },
  { value: 'Monthly', label: 'Todo mês' },
]

// Débito automático de fato: escolhido, todo mês, e numa despesa em conta corrente (docs/fase-2.md, 2.15).
const isAutoDebit = (choice: RepeatChoice, available?: boolean) =>
  !!available && !!choice.autoDebit && choice.frequency === 'Monthly'

// O vencimento da primeira ocorrência: a data do lançamento ou alguns dias antes (regra 4).
const dueOf = (choice: RepeatChoice, start: string) => addDays(start, -(choice.dueDaysBefore ?? 0))

// A linha compacta do Novo lançamento: "↻ Não se repete ›" ou a série escolhida.
export function RepeatRow({
  choice,
  start,
  today,
  onOpen,
  autoDebitAvailable,
}: {
  choice: RepeatChoice
  start: string
  today: string
  onOpen: () => void
  autoDebitAvailable?: boolean
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
        {!repeats
          ? 'Não se repete'
          : isAutoDebit(choice, autoDebitAvailable)
            ? autoDebitLine({ due: dueOf(choice, start), endDate: choice.endDate || null, today })
            : seriesLine({ start, frequency: choice.frequency!, endDate: choice.endDate || null, today })}
      </span>
      <ChevronRight className="size-4 shrink-0 text-muted-foreground" aria-hidden />
    </button>
  )
}

// As escolhas do painel e o que a série vai fazer. withNone: o Novo lançamento pode voltar a "Não se
// repete"; no painel de um lançamento, desistir é fechar. autoDebitAvailable: despesa em conta corrente, onde
// "Todo mês" pode ser débito automático (docs/fase-2.md, 2.15).
export function RecurrenceChooser({
  choice,
  onChange,
  start,
  today,
  withNone,
  error,
  autoDebitAvailable,
}: {
  choice: RepeatChoice
  onChange: (choice: RepeatChoice) => void
  start: string
  today: string
  withNone?: boolean
  error?: string
  autoDebitAvailable?: boolean
}) {
  const { frequency, endDate } = choice
  const autoDebit = isAutoDebit(choice, autoDebitAvailable)
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
            <Chip
              key={f.value}
              selected={frequency === f.value}
              onClick={() => onChange({ ...choice, frequency: f.value, endDate })}
            >
              {f.label}
            </Chip>
          ))}
        </ChipRow>
      </Section>

      {autoDebitAvailable && frequency === 'Monthly' && (
        <Section title="Débito automático">
          <ChipRow>
            <Chip selected={!autoDebit} onClick={() => onChange({ ...choice, autoDebit: false })}>
              Não
            </Chip>
            {/* O caso que motivou o débito automático é a conta de consumo: o valor muda. */}
            <Chip
              selected={autoDebit}
              onClick={() => !autoDebit && onChange({ ...choice, autoDebit: true, amountVaries: choice.amountVaries ?? true })}
            >
              É débito automático
            </Chip>
          </ChipRow>
        </Section>
      )}

      {autoDebit && (
        <Section title="Valor">
          <ChipRow>
            <Chip selected={!!choice.amountVaries} onClick={() => onChange({ ...choice, amountVaries: true })}>
              Muda a cada mês
            </Chip>
            <Chip selected={!choice.amountVaries} onClick={() => onChange({ ...choice, amountVaries: false })}>
              Sempre o mesmo
            </Chip>
          </ChipRow>
        </Section>
      )}

      {autoDebit && (
        <Section title="Vencimento">
          <ChipRow>
            {dueDateChoices(start).map((due) => (
              <Chip
                key={due.daysBefore}
                selected={(choice.dueDaysBefore ?? 0) === due.daysBefore}
                onClick={() => onChange({ ...choice, dueDaysBefore: due.daysBefore })}
                className="tabular-nums"
              >
                {due.label}
              </Chip>
            ))}
          </ChipRow>
        </Section>
      )}

      {frequency !== null && (
        <Section title="Termina">
          <ChipRow>
            <Chip selected={endDate === null} onClick={() => onChange({ ...choice, endDate: null })}>
              Nunca
            </Chip>
            <Chip selected={endDate !== null} onClick={() => endDate === null && onChange({ ...choice, endDate: '' })}>
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
              onChange={(event) => onChange({ ...choice, endDate: event.target.value })}
              className="h-11 rounded-xl"
            />
          )}
          <FieldError message={error} />
        </Section>
      )}

      {frequency !== null &&
        (autoDebit ? (
          <AutoDebitPreview
            due={dueOf(choice, start)}
            endDate={endDate || null}
            amountVaries={!!choice.amountVaries}
            today={today}
          />
        ) : (
          <Preview start={start} frequency={frequency} endDate={endDate || null} today={today} />
        ))}
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

// O débito automático (2.15): o vencimento, o dia útil do débito (decidido pela API) e o sino, quando o valor muda.
function AutoDebitPreview({
  due,
  endDate,
  amountVaries,
  today,
}: {
  due: string
  endDate: string | null
  amountVaries: boolean
  today: string
}) {
  const { dueNow, next } = describeSeries({ start: due, frequency: 'Monthly', endDate, today })
  const day = Number(due.slice(8, 10))
  return (
    <div className="surface flex flex-col gap-1.5 rounded-2xl px-4 py-3 text-sm">
      <p className="font-medium">{autoDebitLine({ due, endDate, today })}</p>
      <p className="text-muted-foreground">Vencimento em fim de semana ou feriado é debitado no próximo dia útil.</p>
      {amountVaries && (
        <p className="text-muted-foreground">Cada débito sai com este valor, como média, e aparece no sino para você conferir.</p>
      )}
      {dueNow.length > 0 && <p className="text-muted-foreground">Os que já venceram são lançados junto.</p>}
      {next === null && <p className="text-muted-foreground">Depois disso, nada mais é lançado.</p>}
      {day > 28 && <p className="text-muted-foreground">Nos meses sem dia {day}, vence no último dia do mês.</p>}
    </div>
  )
}
