import { zodResolver } from '@hookform/resolvers/zod'
import { CalendarDays, ChevronDown, X } from 'lucide-react'
import { useEffect, useMemo, useRef, useState, type ReactNode } from 'react'
import { useForm, useWatch, type Path, type PathValue } from 'react-hook-form'
import { Link, useNavigate } from 'react-router'
import { toast } from 'sonner'
import { z } from 'zod'
import { AccountTile, CategoryTile, EntryTile } from '@/components/brand/Tiles'
import { FieldError } from '@/components/FieldError'
import { Alert, AlertDescription } from '@/components/ui/alert'
import { Button } from '@/components/ui/button'
import { Input } from '@/components/ui/input'
import { Skeleton } from '@/components/ui/skeleton'
import { defaultPaymentMethod, paymentMethodLabels, type PaymentMethod } from '@/features/accounts/labels'
import { useAccounts, type Account } from '@/features/accounts/queries'
import { useCategories, type CategoryNode } from '@/features/categories/queries'
import { ApiError } from '@/lib/api'
import { findBrand } from '@/lib/brands/merchants'
import { addDays, todayInSaoPaulo } from '@/lib/dates'
import { describeInstallments, formatCents, parseCentsInput } from '@/lib/money'
import { readLastAccountId, saveLastAccountId } from '@/lib/preferences'
import { cn } from '@/lib/utils'
import { useCreateTransaction } from './queries'

const maxInstallments = 24

const schema = z.object({
  type: z.enum(['Expense', 'Income']),
  amountCents: z.number().int().min(1, 'Informe o valor.'),
  accountId: z.string().min(1, 'Escolha a conta.'),
  categoryId: z.string(),
  method: z.enum(['Pix', 'Debit', 'Credit', 'Boleto', 'Cash', 'Ted']),
  purchaseDate: z.string().min(1, 'Informe a data.'),
  installments: z.number().int().min(1).max(maxInstallments),
  description: z.string().max(200, 'A descrição deve ter no máximo 200 caracteres.'),
})

type Values = z.infer<typeof schema>

// Fora do cartão não existe "Crédito" (a API recusa).
const simpleMethods: PaymentMethod[] = ['Pix', 'Debit', 'Cash', 'Boleto', 'Ted']

export function NewTransactionPage() {
  const accounts = useAccounts()
  const categories = useCategories()
  const activeAccounts = useMemo(() => (accounts.data ?? []).filter((a) => a.isActive), [accounts.data])

  if (accounts.isPending || categories.isPending) {
    return (
      <Shell>
        <div className="flex flex-col items-center gap-6 px-4 pt-8">
          <Skeleton className="h-9 w-56 rounded-full" />
          <Skeleton className="h-16 w-64" />
          <Skeleton className="h-24 w-full rounded-2xl" />
        </div>
      </Shell>
    )
  }
  if (accounts.isError || categories.isError) {
    return (
      <Shell>
        <div className="p-4">
          <Alert variant="destructive">
            <AlertDescription>Não foi possível carregar contas e categorias. Tente novamente.</AlertDescription>
          </Alert>
        </div>
      </Shell>
    )
  }
  if (activeAccounts.length === 0) {
    return (
      <Shell>
        <div className="flex flex-col items-center gap-4 px-6 pt-16 text-center">
          <p className="font-display text-3xl">Primeiro, uma conta</p>
          <p className="text-muted-foreground">Lançamentos acontecem numa conta: corrente, cartão, carteira…</p>
          <Button asChild className="rounded-full">
            <Link to="/contas">Criar conta</Link>
          </Button>
        </div>
      </Shell>
    )
  }

  return <Composer accounts={activeAccounts} categories={categories.data} />
}

function Shell({ children, footer }: { children: ReactNode; footer?: ReactNode }) {
  return (
    <div className="flex min-h-dvh flex-col bg-background">
      <header className="sticky top-0 z-20 flex h-14 items-center justify-between border-b border-border/60 bg-background/80 px-2 pt-[env(safe-area-inset-top)] backdrop-blur-xl">
        <Button asChild variant="ghost" size="icon" className="rounded-full" aria-label="Fechar">
          <Link to="/">
            <X />
          </Link>
        </Button>
        <span className="text-sm font-medium">Novo lançamento</span>
        <span className="size-9" />
      </header>
      <div className="mx-auto w-full max-w-md flex-1">{children}</div>
      {footer}
    </div>
  )
}

function Composer({ accounts, categories }: { accounts: Account[]; categories: CategoryNode[] }) {
  const navigate = useNavigate()
  const createTransaction = useCreateTransaction()
  const today = todayInSaoPaulo()
  const yesterday = addDays(today, -1)

  // A última conta usada; na primeira vez, a conta corrente é o palpite mais provável.
  const initialAccount =
    accounts.find((a) => a.id === readLastAccountId()) ?? accounts.find((a) => a.type === 'Checking') ?? accounts[0]
  const form = useForm<Values>({
    resolver: zodResolver(schema),
    defaultValues: {
      type: 'Expense',
      amountCents: 0,
      accountId: initialAccount.id,
      categoryId: '',
      method: defaultPaymentMethod(initialAccount.type),
      purchaseDate: today,
      installments: 1,
      description: '',
    },
  })
  const { errors, isSubmitting } = form.formState
  const [type, amountCents, accountId, categoryId, purchaseDate, installments, method, description] = useWatch({
    control: form.control,
    name: ['type', 'amountCents', 'accountId', 'categoryId', 'purchaseDate', 'installments', 'method', 'description'],
  })
  const set = <K extends Path<Values>>(field: K, value: PathValue<Values, K>) =>
    form.setValue(field, value, { shouldValidate: form.formState.isSubmitted })

  const account = accounts.find((a) => a.id === accountId)
  const isCard = account?.type === 'CreditCard'
  const roots = categories.filter((c) => c.type === type)
  const selectedRoot = roots.find((r) => r.id === categoryId || r.subcategories.some((s) => s.id === categoryId))
  const selectedSub = selectedRoot?.subcategories.find((s) => s.id === categoryId)
  const selectedCategory = selectedRoot && {
    name: selectedSub?.name ?? selectedRoot.name,
    icon: selectedSub?.icon ?? selectedRoot.icon,
    color: selectedSub?.color ?? selectedRoot.color,
  }
  const brand = findBrand(description)

  // Trocar a conta ajusta o meio de pagamento; o cartão só aceita despesa (docs/fase-1.md).
  useEffect(() => {
    if (!account) return
    form.setValue('method', defaultPaymentMethod(account.type))
    if (account.type === 'CreditCard') form.setValue('type', 'Expense')
    else form.setValue('installments', 1)
  }, [account, form])

  // Categoria de receita não serve para despesa, e vice-versa.
  useEffect(() => {
    if (categoryId && !selectedRoot) form.setValue('categoryId', '')
  }, [categoryId, selectedRoot, form])

  const submit = form.handleSubmit(async (values) => {
    try {
      const created = await createTransaction.mutateAsync({
        accountId: values.accountId,
        type: values.type,
        amountCents: values.amountCents,
        purchaseDate: values.purchaseDate,
        categoryId: values.categoryId || null,
        method: values.method,
        description: values.description.trim() || null,
        installments: isCard ? values.installments : 1,
      })
      saveLastAccountId(values.accountId)
      toast.success('Lançamento salvo', {
        description: [
          values.description.trim() || null,
          created.length > 1 ? describeInstallments(values.amountCents, created.length) : formatCents(values.amountCents),
        ]
          .filter(Boolean)
          .join(' · '),
      })
      // Abre o mês da compra: um lançamento antigo não some da vista.
      navigate(`/?mes=${values.purchaseDate.slice(0, 7)}`, { replace: true })
    } catch (error) {
      form.setError('root', {
        message: error instanceof ApiError ? error.message : 'Não foi possível conectar. Tente novamente.',
      })
    }
  })

  const isOtherDate = purchaseDate !== today && purchaseDate !== yesterday
  const [pickingDate, setPickingDate] = useState(false)
  const dateInput = useRef<HTMLInputElement>(null)

  // "Outra data" mostra um campo visível (dá para digitar uma data antiga) e tenta abrir o
  // calendário do sistema, que nem todo navegador permite abrir por código.
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
    <Shell
      footer={
        <div className="sticky bottom-0 z-20 border-t border-border/60 bg-background/85 px-4 pt-3 pb-[max(0.75rem,env(safe-area-inset-bottom))] backdrop-blur-xl">
          <Button
            type="submit"
            form="new-transaction"
            size="lg"
            disabled={isSubmitting}
            className="mx-auto flex h-12 w-full max-w-md rounded-2xl text-base"
          >
            {isSubmitting ? 'Lançando…' : amountCents > 0 ? `Lançar ${formatCents(amountCents)}` : 'Lançar'}
          </Button>
        </div>
      }
    >
      <form id="new-transaction" onSubmit={submit} noValidate className="flex flex-col gap-7 px-4 pt-5 pb-8">
        {errors.root && (
          <Alert variant="destructive">
            <AlertDescription>{errors.root.message}</AlertDescription>
          </Alert>
        )}

        <div className="flex flex-col items-center gap-5">
          <TypeToggle value={type} incomeDisabled={isCard} onChange={(value) => set('type', value)} />
          <div className="flex w-full flex-col items-center gap-1">
            {/* Sem cursor piscando: os dígitos entram pela direita, e o foco aparece no filete. */}
            <input
              aria-label="Valor"
              inputMode="numeric"
              autoComplete="off"
              autoFocus
              placeholder={formatCents(0)}
              value={amountCents === 0 ? '' : formatCents(amountCents)}
              onChange={(event) => set('amountCents', parseCentsInput(event.target.value))}
              className={cn(
                'peer w-full bg-transparent text-center font-display leading-tight tabular-nums caret-transparent outline-none selection:bg-transparent placeholder:text-muted-foreground/40',
                amountSize(amountCents),
                type === 'Income' && amountCents > 0 && 'text-spectrum',
              )}
            />
            <span
              aria-hidden
              className="h-0.5 w-16 rounded-full bg-[image:var(--spectrum)] opacity-0 transition-all duration-300 peer-focus:w-28 peer-focus:opacity-100"
            />
            <FieldError message={errors.amountCents?.message} />
          </div>
        </div>

        <Section title="Descrição" aside={brand ? `${brand.name} reconhecido` : 'opcional'}>
          <label className="flex items-center gap-3 rounded-2xl border bg-card py-2 pr-3 pl-2 focus-within:ring-2 focus-within:ring-ring/50">
            <EntryTile description={description} category={selectedCategory} />
            <input
              placeholder="Ex.: Uber, iFood, Pão de Açúcar"
              autoComplete="off"
              className="h-10 min-w-0 flex-1 bg-transparent text-base outline-none placeholder:text-muted-foreground"
              {...form.register('description')}
            />
          </label>
          <FieldError message={errors.description?.message} />
        </Section>

        <Section title="Conta">
          <ChipRow>
            {accounts.map((a) => (
              <Chip key={a.id} selected={a.id === accountId} onClick={() => set('accountId', a.id)}>
                <AccountTile name={a.name} type={a.type} size="sm" />
                {a.name}
              </Chip>
            ))}
          </ChipRow>
        </Section>

        {isCard && (
          <Section
            title="Parcelas"
            aside={installments > 1 && amountCents >= installments ? describeInstallments(amountCents, installments) : undefined}
          >
            <ChipRow>
              {Array.from({ length: maxInstallments }, (_, i) => i + 1).map((n) => (
                <Chip key={n} selected={n === installments} onClick={() => set('installments', n)} className="tabular-nums">
                  {n === 1 ? 'À vista' : `${n}x`}
                </Chip>
              ))}
            </ChipRow>
          </Section>
        )}

        <Section title="Categoria">
          <div className="grid grid-cols-4 gap-x-2 gap-y-3">
            {roots.map((root) => {
              const selected = root.id === selectedRoot?.id
              return (
                <button
                  key={root.id}
                  type="button"
                  aria-pressed={selected}
                  onClick={() => set('categoryId', selected ? '' : root.id)}
                  className="group flex flex-col items-center gap-1.5 rounded-2xl p-1 text-center outline-none focus-visible:ring-2 focus-visible:ring-ring"
                >
                  <span className={cn('rounded-2xl p-[2px] transition-transform group-active:scale-95', selected && 'bg-[image:var(--spectrum-conic)]')}>
                    <CategoryTile name={root.name} icon={root.icon} color={root.color} size="lg" className={cn(selected && 'ring-2 ring-background')} />
                  </span>
                  <span className={cn('line-clamp-2 text-[0.7rem] leading-tight', selected ? 'font-semibold' : 'text-muted-foreground')}>
                    {root.name}
                  </span>
                </button>
              )
            })}
          </div>
          {selectedRoot && selectedRoot.subcategories.length > 0 && (
            <ChipRow className="mt-3">
              <Chip selected={categoryId === selectedRoot.id} onClick={() => set('categoryId', selectedRoot.id)}>
                Geral
              </Chip>
              {selectedRoot.subcategories.map((sub) => (
                <Chip key={sub.id} selected={sub.id === categoryId} onClick={() => set('categoryId', sub.id)}>
                  <CategoryTile name={sub.name} icon={sub.icon ?? selectedRoot.icon} color={sub.color ?? selectedRoot.color} size="sm" />
                  {sub.name}
                </Chip>
              ))}
            </ChipRow>
          )}
        </Section>

        <Section title="Data">
          <ChipRow>
            <Chip selected={purchaseDate === today} onClick={() => set('purchaseDate', today)}>
              Hoje
            </Chip>
            <Chip selected={purchaseDate === yesterday} onClick={() => set('purchaseDate', yesterday)}>
              Ontem
            </Chip>
            <Chip selected={isOtherDate || pickingDate} onClick={pickOtherDate}>
              <CalendarDays className="size-4" />
              {isOtherDate ? formatShortDate(purchaseDate) : 'Outra data'}
            </Chip>
          </ChipRow>
          {(pickingDate || isOtherDate) && (
            <Input
              ref={dateInput}
              type="date"
              aria-label="Data da compra"
              value={purchaseDate}
              max="9999-12-31"
              onChange={(event) => event.target.value && set('purchaseDate', event.target.value)}
              className="h-11 rounded-xl"
            />
          )}
          <FieldError message={errors.purchaseDate?.message} />
        </Section>

        <details className="group rounded-2xl border bg-card open:pb-4">
          <summary className="flex cursor-pointer list-none items-center justify-between px-4 py-3 text-sm font-medium">
            Mais opções
            <ChevronDown className="size-4 text-muted-foreground transition-transform group-open:rotate-180" />
          </summary>
          <div className="flex flex-col gap-4 px-4">
            <div className="flex flex-col gap-2">
              <span className="text-xs font-medium text-muted-foreground">Pagamento</span>
              <ChipRow>
                {(isCard ? (['Credit'] as PaymentMethod[]) : simpleMethods).map((m) => (
                  <Chip key={m} selected={m === method} onClick={() => set('method', m)}>
                    {paymentMethodLabels[m]}
                  </Chip>
                ))}
              </ChipRow>
            </div>
          </div>
        </details>
      </form>
    </Shell>
  )
}

// "2025-11-10" → "10/11/2025", sem passar por Date (DateOnly não tem fuso).
function formatShortDate(date: string): string {
  return date.split('-').reverse().join('/')
}

// O valor encolhe conforme cresce, para caber na largura do celular.
function amountSize(cents: number): string {
  const length = formatCents(cents).length
  if (length > 15) return 'text-4xl'
  if (length > 12) return 'text-5xl'
  return 'text-6xl'
}

function TypeToggle({
  value,
  incomeDisabled,
  onChange,
}: {
  value: Values['type']
  incomeDisabled: boolean
  onChange: (value: Values['type']) => void
}) {
  return (
    <div className="grid grid-cols-2 rounded-full border bg-card p-1 text-sm font-medium">
      <button
        type="button"
        aria-pressed={value === 'Expense'}
        onClick={() => onChange('Expense')}
        className={cn('rounded-full px-5 py-1.5 transition-colors', value === 'Expense' ? 'bg-foreground text-background' : 'text-muted-foreground')}
      >
        Despesa
      </button>
      <button
        type="button"
        aria-pressed={value === 'Income'}
        disabled={incomeDisabled}
        onClick={() => onChange('Income')}
        className={cn(
          'rounded-full px-5 py-1.5 transition-colors disabled:opacity-40',
          value === 'Income' ? 'bg-[image:var(--spectrum)] text-white' : 'text-muted-foreground',
        )}
      >
        Receita
      </button>
    </div>
  )
}

function Section({ title, aside, children }: { title: string; aside?: string; children: ReactNode }) {
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

function ChipRow({ children, className }: { children: ReactNode; className?: string }) {
  return (
    <div className={cn('-mx-4 flex gap-2 overflow-x-auto px-4 pb-1 [scrollbar-width:none] [&::-webkit-scrollbar]:hidden', className)}>
      {children}
    </div>
  )
}

function Chip({
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
  // O item escolhido fica visível mesmo numa fileira longa (ex.: 10x entre 24 parcelas).
  const ref = useRef<HTMLButtonElement>(null)
  useEffect(() => {
    if (selected) ref.current?.scrollIntoView({ block: 'nearest', inline: 'nearest', behavior: 'smooth' })
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
