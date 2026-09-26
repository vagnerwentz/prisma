import { CalendarDays } from 'lucide-react'
import { useEffect, useId, useRef, useState, type ReactNode, type Ref } from 'react'
import { CategoryTile } from '@/components/brand/Tiles'
import { FieldError } from '@/components/FieldError'
import { Input } from '@/components/ui/input'
import { resolveCategory, type CategoryNode } from '@/features/categories/queries'
import { addDays, formatShortDate, todayInSaoPaulo } from '@/lib/dates'
import { formatCents, parseCentsInput } from '@/lib/money'
import { cn } from '@/lib/utils'

// Peças do lançamento rápido, compartilhadas com a edição no painel do lançamento.

export type EntryType = 'Expense' | 'Income' | 'Refund'

export function Section({ title, aside, children }: { title: string; aside?: string; children: ReactNode }) {
  return (
    <section className="flex flex-col gap-2.5">
      <div className="flex items-baseline justify-between">
        <h2 className="text-sm font-medium text-foreground/75">{title}</h2>
        {aside && <span className="text-xs text-muted-foreground tabular-nums">{aside}</span>}
      </div>
      {children}
    </section>
  )
}

export function ChipRow({ children, className }: { children: ReactNode; className?: string }) {
  return (
    <div
      className={cn('-mx-4 flex gap-2 overflow-x-auto px-4 pb-1 [scrollbar-width:none] [&::-webkit-scrollbar]:hidden', className)}
    >
      {children}
    </div>
  )
}

export function Chip({
  selected,
  onClick,
  children,
  className,
}: {
  selected: boolean
  onClick: () => void
  children: ReactNode
  className?: string
}) {
  // O item escolhido fica visível mesmo numa fileira longa (ex.: 10x entre 24 parcelas). Rola só a
  // fileira: scrollIntoView rolaria também a tela (ou o painel) na vertical.
  const ref = useRef<HTMLButtonElement>(null)
  useEffect(() => {
    const chip = ref.current
    const row = chip?.parentElement
    if (!selected || !chip || !row) return
    const c = chip.getBoundingClientRect()
    const r = row.getBoundingClientRect()
    if (c.left < r.left || c.right > r.right)
      row.scrollBy({ left: c.left - r.left - (r.width - c.width) / 2, behavior: 'smooth' })
  }, [selected])

  return (
    <button
      ref={ref}
      type="button"
      aria-pressed={selected}
      onClick={onClick}
      className={cn(
        'flex h-10 shrink-0 items-center gap-2 rounded-full border px-3.5 text-sm whitespace-nowrap transition-colors active:scale-[0.97]',
        selected ? 'spectrum-ring font-medium' : 'bg-card text-muted-foreground hover:text-foreground',
        className,
      )}
    >
      {children}
    </button>
  )
}

export function TypeToggle({
  value,
  incomeDisabled,
  onChange,
  onTransfer,
  withRefund,
}: {
  value: EntryType | 'Transfer'
  incomeDisabled: boolean
  onChange: (value: EntryType) => void
  // No lançamento rápido, "Transferência" troca o formulário (docs/fase-1.md, 2.3).
  onTransfer?: () => void
  // "Estorno" só no lançamento novo; na edição o tipo do estorno não muda (docs/fase-2.md, 2.5).
  withRefund?: boolean
}) {
  const option = (selected: boolean, extra?: string) =>
    cn(
      'rounded-full px-4 py-1.5 transition-colors',
      selected ? (extra ?? 'bg-foreground text-background') : 'text-muted-foreground',
    )
  const count = 2 + (withRefund ? 1 : 0) + (onTransfer ? 1 : 0)
  return (
    // Quatro opções não cabem numa linha em 320px: no celular viram 2 × 2.
    <div
      className={cn(
        'grid border bg-card p-1 text-sm font-medium',
        count === 4 ? 'grid-cols-2 gap-y-1 rounded-3xl sm:grid-cols-[repeat(4,auto)] sm:rounded-full' : 'rounded-full',
        count === 3 && 'grid-cols-3',
        count === 2 && 'grid-cols-2',
      )}
    >
      <button
        type="button"
        aria-pressed={value === 'Expense'}
        onClick={() => onChange('Expense')}
        className={option(value === 'Expense')}
      >
        Despesa
      </button>
      <button
        type="button"
        aria-pressed={value === 'Income'}
        disabled={incomeDisabled}
        onClick={() => onChange('Income')}
        className={cn(option(value === 'Income', 'bg-[image:var(--spectrum)] text-white'), 'disabled:opacity-40')}
      >
        Receita
      </button>
      {withRefund && (
        <button
          type="button"
          aria-pressed={value === 'Refund'}
          onClick={() => onChange('Refund')}
          className={option(value === 'Refund')}
        >
          Estorno
        </button>
      )}
      {onTransfer && (
        <button type="button" aria-pressed={value === 'Transfer'} onClick={onTransfer} className={option(value === 'Transfer')}>
          Transferência
        </button>
      )}
    </div>
  )
}

// Valor em destaque. O campo nativo fica invisível por cima do número: o cursor do sistema seria
// gigante nesse tamanho, então um cursor fino do espectro pisca à direita dos dígitos. Rótulo, linha
// sempre visível e "Toque para digitar" deixam claro que é um campo (antes, parecia texto). Vazio e sem
// foco, um feixe do espectro corre pela linha a cada 4 s para chamar o olho.
export function AmountField({
  value,
  onChange,
  income,
  error,
  autoFocus,
  label = 'Valor',
  compact,
  ref,
}: {
  value: number
  onChange: (cents: number) => void
  income: boolean
  error?: string
  autoFocus?: boolean
  label?: string
  compact?: boolean
  ref?: Ref<HTMLInputElement>
}) {
  const id = useId()
  // Valor que já veio preenchido (lançar de novo, estorno, edição): o primeiro dígito o substitui,
  // em vez de entrar no fim como numa calculadora (R$ 62,00 + "4" viraria R$ 6.200,04).
  const pristine = useRef(true)
  const empty = value === 0
  // O dígito recém-digitado entra subindo; apagar ou trocar o valor por fora não anima.
  const [previous, setPrevious] = useState(value)
  const [grew, setGrew] = useState(false)
  if (value !== previous) {
    setPrevious(value)
    setGrew(value > previous)
  }
  const text = formatCents(value)
  return (
    <div className="group flex w-full flex-col items-center gap-1.5">
      <label htmlFor={id} className={cn('text-sm font-medium text-foreground/75', error && 'text-destructive')}>
        {label}
      </label>
      <div
        className={cn(
          'relative flex w-full items-center justify-center font-display leading-tight tabular-nums',
          amountSize(value, compact),
        )}
      >
        <input
          ref={ref}
          id={id}
          inputMode="numeric"
          autoComplete="off"
          autoFocus={autoFocus}
          aria-invalid={error ? true : undefined}
          value={empty ? '' : formatCents(value)}
          onFocus={(event) => {
            if (pristine.current && value > 0) event.target.select()
          }}
          onChange={(event) => {
            pristine.current = false
            onChange(parseCentsInput(event.target.value))
          }}
          className="absolute inset-0 w-full cursor-text bg-transparent text-center text-transparent caret-transparent outline-none selection:bg-transparent"
        />
        <span aria-hidden className={cn('pointer-events-none', empty ? 'text-muted-foreground/70' : income && 'text-spectrum')}>
          {grew ? text.slice(0, -1) : text}
        </span>
        {grew && (
          <span key={value} aria-hidden className={cn('amount-digit-in pointer-events-none', income && 'amount-digit-income')}>
            {text.slice(-1)}
          </span>
        )}
        <span aria-hidden className="amount-caret pointer-events-none hidden group-focus-within:inline-block" />
      </div>
      <span
        aria-hidden
        className={cn('relative h-0.5 w-40 rounded-full [clip-path:inset(-8px_0)]', error ? 'bg-destructive' : 'bg-border')}
      >
        {empty && !error && <span className="amount-sweep group-focus-within:hidden" />}
        {!error && (
          <span className="absolute inset-0 scale-x-[0.57] rounded-full bg-[image:var(--spectrum)] opacity-0 transition-[transform,opacity] duration-200 ease-out group-focus-within:scale-x-100 group-focus-within:opacity-100" />
        )}
      </span>
      {/* Espaço fixo para a dica ou o erro: o formulário abaixo não pula ao digitar o primeiro número. */}
      <div className="flex min-h-5 items-center">
        {error ? (
          <FieldError message={error} />
        ) : (
          empty && (
            <p className="text-xs text-muted-foreground transition-opacity duration-150 ease-out group-focus-within:opacity-0">
              Toque para digitar
            </p>
          )
        )}
      </div>
    </div>
  )
}

// O valor encolhe conforme cresce, para caber na largura do celular.
function amountSize(cents: number, compact?: boolean): string {
  const length = formatCents(cents).length
  if (length > 15) return compact ? 'text-3xl' : 'text-4xl'
  if (length > 12) return compact ? 'text-4xl' : 'text-5xl'
  return compact ? 'text-5xl' : 'text-6xl'
}

export function CategoryPicker({
  roots,
  value,
  onChange,
}: {
  roots: CategoryNode[]
  value: string
  onChange: (categoryId: string) => void
}) {
  const { root: selectedRoot } = resolveCategory(roots, value)
  return (
    <>
      <div className="grid grid-cols-4 gap-x-2 gap-y-3">
        {roots.map((root) => {
          const selected = root.id === selectedRoot?.id
          return (
            <button
              key={root.id}
              type="button"
              aria-pressed={selected}
              onClick={() => onChange(selected ? '' : root.id)}
              className="group flex flex-col items-center gap-1.5 rounded-2xl p-1 text-center outline-none focus-visible:ring-2 focus-visible:ring-ring"
            >
              <span
                className={cn(
                  'rounded-2xl p-[2px] transition-transform group-active:scale-95',
                  selected && 'bg-[image:var(--spectrum-conic)]',
                )}
              >
                <CategoryTile
                  name={root.name}
                  icon={root.icon}
                  color={root.color}
                  size="lg"
                  className={cn(selected && 'ring-2 ring-background')}
                />
              </span>
              <span
                className={cn('line-clamp-2 text-[0.7rem] leading-tight', selected ? 'font-semibold' : 'text-muted-foreground')}
              >
                {root.name}
              </span>
            </button>
          )
        })}
      </div>
      {selectedRoot && selectedRoot.subcategories.length > 0 && (
        <ChipRow className="mt-3">
          <Chip selected={value === selectedRoot.id} onClick={() => onChange(selectedRoot.id)}>
            Geral
          </Chip>
          {selectedRoot.subcategories.map((sub) => (
            <Chip key={sub.id} selected={sub.id === value} onClick={() => onChange(sub.id)}>
              <CategoryTile
                name={sub.name}
                icon={sub.icon ?? selectedRoot.icon}
                color={sub.color ?? selectedRoot.color}
                size="sm"
              />
              {sub.name}
            </Chip>
          ))}
        </ChipRow>
      )}
    </>
  )
}

// Hoje, ontem ou outra data. "Outra data" mostra um campo visível (dá para digitar uma data
// antiga) e tenta abrir o calendário do sistema, que nem todo navegador permite abrir por código.
export function DateChooser({ value, onChange, error }: { value: string; onChange: (date: string) => void; error?: string }) {
  const today = todayInSaoPaulo()
  const yesterday = addDays(today, -1)
  const isOtherDate = value !== today && value !== yesterday
  const [pickingDate, setPickingDate] = useState(false)
  const dateInput = useRef<HTMLInputElement>(null)

  const pickOtherDate = () => {
    setPickingDate(true)
    requestAnimationFrame(() => {
      dateInput.current?.focus()
      try {
        dateInput.current?.showPicker()
      } catch {
        // sem calendário por código: o campo visível basta
      }
    })
  }

  return (
    <>
      <ChipRow>
        <Chip selected={value === today} onClick={() => onChange(today)}>
          Hoje
        </Chip>
        <Chip selected={value === yesterday} onClick={() => onChange(yesterday)}>
          Ontem
        </Chip>
        <Chip selected={isOtherDate || pickingDate} onClick={pickOtherDate}>
          <CalendarDays className="size-4" />
          {isOtherDate ? formatShortDate(value) : 'Outra data'}
        </Chip>
      </ChipRow>
      {(pickingDate || isOtherDate) && (
        <Input
          ref={dateInput}
          type="date"
          aria-label="Data da compra"
          value={value}
          max="9999-12-31"
          onChange={(event) => event.target.value && onChange(event.target.value)}
          className="h-11 rounded-xl"
        />
      )}
      <FieldError message={error} />
    </>
  )
}
