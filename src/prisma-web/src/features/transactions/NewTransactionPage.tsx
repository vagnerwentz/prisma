import { zodResolver } from '@hookform/resolvers/zod'
import { ArrowDown, ChevronDown, X } from 'lucide-react'
import { useEffect, useMemo, useState, type ReactNode } from 'react'
import { useForm, useWatch, type Path, type PathValue } from 'react-hook-form'
import { Link, useNavigate } from 'react-router'
import { toast } from 'sonner'
import { z } from 'zod'
import { AccountTile, EntryTile } from '@/components/brand/Tiles'
import { FieldError } from '@/components/FieldError'
import { Alert, AlertDescription } from '@/components/ui/alert'
import { Button } from '@/components/ui/button'
import { Input } from '@/components/ui/input'
import { Skeleton } from '@/components/ui/skeleton'
import { defaultPaymentMethod, paymentMethodLabels, type PaymentMethod } from '@/features/accounts/labels'
import { useAccounts, type Account } from '@/features/accounts/queries'
import { resolveCategory, useCategories, type CategoryNode } from '@/features/categories/queries'
import { ApiError } from '@/lib/api'
import { findBrand } from '@/lib/brands/merchants'
import { todayInSaoPaulo } from '@/lib/dates'
import { describeInstallments, formatCents } from '@/lib/money'
import { readLastAccountId, saveLastAccountId } from '@/lib/preferences'
import { AmountField, CategoryPicker, Chip, ChipRow, DateChooser, Section, TypeToggle } from './fields'
import { useCreateTransaction, useCreateTransfer } from './queries'

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

  return <Composers accounts={activeAccounts} categories={categories.data} />
}

// Receita e despesa num formulário; transferência em outro (duas pontas, sem categoria).
function Composers({ accounts, categories }: { accounts: Account[]; categories: CategoryNode[] }) {
  const [transfer, setTransfer] = useState(false)
  return transfer ? (
    <TransferComposer accounts={accounts} onEntry={() => setTransfer(false)} />
  ) : (
    <Composer accounts={accounts} categories={categories} onTransfer={() => setTransfer(true)} />
  )
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

function Composer({
  accounts,
  categories,
  onTransfer,
}: {
  accounts: Account[]
  categories: CategoryNode[]
  onTransfer: () => void
}) {
  const navigate = useNavigate()
  const createTransaction = useCreateTransaction()
  const today = todayInSaoPaulo()

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
  const { root: selectedRoot, label: selectedCategory } = resolveCategory(roots, categoryId)
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
          <TypeToggle value={type} incomeDisabled={isCard} onChange={(value) => set('type', value)} onTransfer={onTransfer} />
          <AmountField
            value={amountCents}
            onChange={(cents) => set('amountCents', cents)}
            income={type === 'Income'}
            error={errors.amountCents?.message}
            autoFocus
          />
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
          <CategoryPicker roots={roots} value={categoryId} onChange={(id) => set('categoryId', id)} />
        </Section>

        <Section title="Data">
          <DateChooser value={purchaseDate} onChange={(date) => set('purchaseDate', date)} error={errors.purchaseDate?.message} />
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

// Fora do cartão: o cartão recebe dinheiro só pelo "Pagar fatura" (decisão da etapa 1.10).
const transferMethods = ['Pix', 'Ted', 'Cash', 'Debit', 'Boleto'] as const satisfies readonly PaymentMethod[]

const transferSchema = z
  .object({
    amountCents: z.number().int().min(1, 'Informe o valor.'),
    fromAccountId: z.string().min(1, 'Escolha a conta de origem.'),
    toAccountId: z.string().min(1, 'Escolha a conta de destino.'),
    date: z.string().min(1, 'Informe a data.'),
    method: z.enum(['Pix', 'Debit', 'Boleto', 'Cash', 'Ted']),
    description: z.string().max(200, 'A descrição deve ter no máximo 200 caracteres.'),
  })
  .refine((v) => v.fromAccountId !== v.toAccountId, { path: ['toAccountId'], message: 'Escolha contas diferentes.' })

type TransferValues = z.infer<typeof transferSchema>

// Transferência entre contas próprias (docs/fase-1.md, 2.3): não é receita nem despesa.
function TransferComposer({ accounts, onEntry }: { accounts: Account[]; onEntry: () => void }) {
  const navigate = useNavigate()
  const createTransfer = useCreateTransfer()
  const eligible = accounts.filter((a) => a.type !== 'CreditCard')
  const initialFrom = eligible.find((a) => a.type === 'Checking') ?? eligible[0]
  const form = useForm<TransferValues>({
    resolver: zodResolver(transferSchema),
    defaultValues: {
      amountCents: 0,
      fromAccountId: initialFrom?.id ?? '',
      toAccountId: eligible.find((a) => a.id !== initialFrom?.id)?.id ?? '',
      date: todayInSaoPaulo(),
      method: 'Pix',
      description: '',
    },
  })
  const { errors, isSubmitting } = form.formState
  const [amountCents, fromAccountId, toAccountId, date, method] = useWatch({
    control: form.control,
    name: ['amountCents', 'fromAccountId', 'toAccountId', 'date', 'method'],
  })
  const set = <K extends Path<TransferValues>>(field: K, value: PathValue<TransferValues, K>) =>
    form.setValue(field, value, { shouldValidate: form.formState.isSubmitted })

  const submit = form.handleSubmit(async (values) => {
    try {
      await createTransfer.mutateAsync({
        fromAccountId: values.fromAccountId,
        toAccountId: values.toAccountId,
        amountCents: values.amountCents,
        date: values.date,
        method: values.method,
        description: values.description.trim() || null,
      })
      const from = eligible.find((a) => a.id === values.fromAccountId)?.name
      const to = eligible.find((a) => a.id === values.toAccountId)?.name
      toast.success('Transferência lançada', { description: `${from} → ${to} · ${formatCents(values.amountCents)}` })
      navigate(`/?mes=${values.date.slice(0, 7)}`, { replace: true })
    } catch (error) {
      form.setError('root', {
        message: error instanceof ApiError ? error.message : 'Não foi possível conectar. Tente novamente.',
      })
    }
  })

  const accountChips = (selected: string, field: 'fromAccountId' | 'toAccountId') => (
    <ChipRow>
      {eligible.map((a) => (
        <Chip key={a.id} selected={a.id === selected} onClick={() => set(field, a.id)}>
          <AccountTile name={a.name} type={a.type} size="sm" />
          {a.name}
        </Chip>
      ))}
    </ChipRow>
  )

  return (
    <Shell
      footer={
        <div className="sticky bottom-0 z-20 border-t border-border/60 bg-background/85 px-4 pt-3 pb-[max(0.75rem,env(safe-area-inset-bottom))] backdrop-blur-xl">
          <Button
            type="submit"
            form="new-transfer"
            size="lg"
            disabled={isSubmitting || eligible.length < 2}
            className="mx-auto flex h-12 w-full max-w-md rounded-2xl text-base"
          >
            {isSubmitting ? 'Transferindo…' : amountCents > 0 ? `Transferir ${formatCents(amountCents)}` : 'Transferir'}
          </Button>
        </div>
      }
    >
      <form id="new-transfer" onSubmit={submit} noValidate className="flex flex-col gap-7 px-4 pt-5 pb-8">
        {errors.root && (
          <Alert variant="destructive">
            <AlertDescription>{errors.root.message}</AlertDescription>
          </Alert>
        )}
        <div className="flex flex-col items-center gap-5">
          <TypeToggle value="Transfer" incomeDisabled={false} onChange={onEntry} onTransfer={() => {}} />
          <AmountField
            value={amountCents}
            onChange={(cents) => set('amountCents', cents)}
            income={false}
            error={errors.amountCents?.message}
            autoFocus
          />
          <p className="-mt-3 text-xs text-muted-foreground">Entre contas suas. Não conta como receita nem despesa.</p>
        </div>

        {eligible.length < 2 ? (
          <p className="rounded-2xl border border-dashed px-4 py-6 text-center text-sm text-muted-foreground">
            Transferir exige duas contas (corrente, carteira ou investimento). Cartão recebe pelo "Pagar fatura", na tela da
            fatura.
          </p>
        ) : (
          <>
            <Section title="De">{accountChips(fromAccountId, 'fromAccountId')}</Section>
            <ArrowDown className="-my-4 size-4 self-center text-muted-foreground" aria-hidden />
            <Section title="Para">
              {accountChips(toAccountId, 'toAccountId')}
              <FieldError message={errors.toAccountId?.message} />
            </Section>
            <Section title="Descrição" aside="opcional">
              <Input
                placeholder="Ex.: Aporte, saque, reserva"
                autoComplete="off"
                className="h-12 rounded-2xl"
                {...form.register('description')}
              />
              <FieldError message={errors.description?.message} />
            </Section>
            <Section title="Data">
              <DateChooser value={date} onChange={(value) => set('date', value)} error={errors.date?.message} />
            </Section>
            <Section title="Meio">
              <ChipRow>
                {transferMethods.map((m) => (
                  <Chip key={m} selected={m === method} onClick={() => set('method', m)}>
                    {paymentMethodLabels[m]}
                  </Chip>
                ))}
              </ChipRow>
            </Section>
          </>
        )}
      </form>
    </Shell>
  )
}
