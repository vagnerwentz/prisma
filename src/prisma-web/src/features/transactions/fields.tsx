import { CalendarDays } from 'lucide-react'
import { useEffect, useRef, useState, type ReactNode } from 'react'
import { CategoryTile } from '@/components/brand/Tiles'
import { FieldError } from '@/components/FieldError'
import { Input } from '@/components/ui/input'
import { resolveCategory, type CategoryNode } from '@/features/categories/queries'
import { addDays, formatShortDate, todayInSaoPaulo } from '@/lib/dates'
import { formatCents, parseCentsInput } from '@/lib/money'
import { cn } from '@/lib/utils'

// Peças do lançamento rápido, compartilhadas com a edição no painel do lançamento.

export type EntryType = 'Expense' | 'Income'

export function Section({ title, aside, children }: { title: string; aside?: string; children: ReactNode }) {
  return (
    <section className="flex flex-col gap-2.5">
      <div className="flex items-baseline justify-between">
        <h2 className="text-xs font-medium tracking-wide text-muted-foreground uppercase">{title}</h2>
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
}: {
  value: EntryType | 'Transfer'
  incomeDisabled: boolean
  onChange: (value: EntryType) => void
  // No lançamento rápido, "Transferência" troca o formulário (docs/fase-1.md, 2.3).
  onTransfer?: () => void
}) {
  const option = (selected: boolean, extra?: string) =>
    cn(
      'rounded-full px-4 py-1.5 transition-colors',
      selected ? (extra ?? 'bg-foreground text-background') : 'text-muted-foreground',
    )
  return (
    <div className={cn('grid rounded-full border bg-card p-1 text-sm font-medium', onTransfer ? 'grid-cols-3' : 'grid-cols-2')}>
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
      {onTransfer && (
        <button type="button" aria-pressed={value === 'Transfer'} onClick={onTransfer} className={option(value === 'Transfer')}>
          Transferência
        </button>
      )}
    </div>
  )
}

// Valor em destaque. Sem cursor piscando: os dígitos entram pela direita, e o foco aparece no
// filete espectral.
export function AmountField({
  value,
  onChange,
  income,
  error,
  autoFocus,
  label = 'Valor',
  compact,
}: {
  value: number
  onChange: (cents: number) => void
  income: boolean
  error?: string
  autoFocus?: boolean
  label?: string
  compact?: boolean
}) {
  return (
    <div className="flex w-full flex-col items-center gap-1">
      <input
        aria-label={label}
        inputMode="numeric"
        autoComplete="off"
        autoFocus={autoFocus}
        placeholder={formatCents(0)}
        value={value === 0 ? '' : formatCents(value)}
        onChange={(event) => onChange(parseCentsInput(event.target.value))}
        className={cn(
          'peer w-full bg-transparent text-center font-display leading-tight tabular-nums caret-transparent outline-none selection:bg-transparent placeholder:text-muted-foreground/40',
          amountSize(value, compact),
          income && value > 0 && 'text-spectrum',
        )}
      />
      <span
        aria-hidden
        className="h-0.5 w-16 rounded-full bg-[image:var(--spectrum)] opacity-0 transition-all duration-300 peer-focus:w-28 peer-focus:opacity-100"
      />
      <FieldError message={error} />
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
