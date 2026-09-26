import { zodResolver } from '@hookform/resolvers/zod'
import { ArrowDown, ChevronDown, CopyPlus, Undo2, X } from 'lucide-react'
import { useEffect, useMemo, useRef, useState, type ReactNode } from 'react'
import { useForm, useWatch, type Path, type PathValue } from 'react-hook-form'
import { Link, useLocation, useNavigate, useSearchParams } from 'react-router'
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
import { categoryLabels, resolveCategory, useCategories, type CategoryNode } from '@/features/categories/queries'
import { ApiError } from '@/lib/api'
import { findBrand } from '@/lib/brands/merchants'
import { todayInSaoPaulo } from '@/lib/dates'
import { describeInstallments, formatCents } from '@/lib/money'
import { readLastAccountId, saveLastAccountId } from '@/lib/preferences'
import { AmountField, CategoryPicker, Chip, ChipRow, DateChooser, Section, TypeToggle, type EntryType } from './fields'
import { useCreateTransaction, useCreateTransfer, useDescriptionSuggestions, useTransaction, type Transaction } from './queries'
import { DescriptionCombobox } from './DescriptionCombobox'
import { isRepeatDraft, repeatValues, type RepeatDraft } from './repeat'
import { suggestionFill, type DescriptionSuggestion } from './suggestions'

const maxInstallments = 24

const schema = z.object({
  type: z.enum(['Expense', 'Income', 'Refund']),
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
  // "Estornar" no painel de uma despesa abre /lancar?estorno=<id> (docs/fase-2.md, 2.5 e 4).
  const [searchParams] = useSearchParams()
  const refundOfId = searchParams.get('estorno')
  const refundOf = useTransaction(refundOfId)
  // "Lançar de novo" chega pelo estado da navegação, com os dados que a tela anterior já tinha
  // (docs/fase-1.md, 5.1). Cada navegação tem a sua chave: o formulário recomeça a cada uma.
  const location = useLocation()
  const state: unknown = location.state
  const repeat =
    typeof state === 'object' && state !== null && 'repeat' in state && isRepeatDraft(state.repeat) ? state.repeat : undefined

  if (accounts.isPending || categories.isPending || (refundOfId && refundOf.isPending)) {
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
  if (accounts.isError || categories.isError || refundOf.isError) {
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

  if (refundOf.data) {
    // A conta da compra, mesmo que tenha sido desativada depois: o estorno fica nela.
    const account = (accounts.data ?? []).find((a) => a.id === refundOf.data.accountId)
    if (account)
      return <Composer key={refundOf.data.id} accounts={[account]} categories={categories.data} refundOf={refundOf.data} />
  }

  return <Composers key={location.key} accounts={activeAccounts} categories={categories.data} repeat={repeat} />
}

// Receita, despesa e estorno num formulário; transferência em outro (duas pontas, sem categoria).
// Sair da transferência pelo seletor abre o formulário já no tipo tocado (despesa, receita ou estorno).
function Composers({ accounts, categories, repeat }: { accounts: Account[]; categories: CategoryNode[]; repeat?: RepeatDraft }) {
  const [shown, setShown] = useState<EntryType | 'Transfer'>(repeat?.type ?? 'Expense')
  return shown === 'Transfer' ? (
    <TransferComposer accounts={accounts} onEntry={setShown} />
  ) : (
    <Composer
      key={shown}
      accounts={accounts}
      categories={categories}
      initialType={shown}
      // Voltar da transferência para outro tipo começa em branco.
      repeat={repeat?.type === shown ? repeat : undefined}
      onTransfer={() => setShown('Transfer')}
    />
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
  refundOf,
  repeat,
  initialType = 'Expense',
}: {
  accounts: Account[]
  categories: CategoryNode[]
  initialType?: EntryType
  onTransfer?: () => void
  // Estornar uma compra: tipo, conta e vínculo fixos; valor, categoria e descrição já preenchidos.
  refundOf?: Transaction
  // Lançar de novo: tudo preenchido e editável, com a data de hoje.
  repeat?: RepeatDraft
}) {
  const navigate = useNavigate()
  const createTransaction = useCreateTransaction()
  const today = todayInSaoPaulo()

  // A última conta usada; na primeira vez, a conta corrente é o palpite mais provável.
  const initialAccount =
    accounts.find((a) => a.id === readLastAccountId()) ?? accounts.find((a) => a.type === 'Checking') ?? accounts[0]
  const form = useForm<Values>({
    resolver: zodResolver(schema),
    defaultValues: refundOf
      ? {
          type: 'Refund',
          amountCents: refundOf.refundableCents ?? refundOf.amountCents,
          accountId: refundOf.accountId,
          categoryId: refundOf.categoryId ?? '',
          method: refundOf.method,
          purchaseDate: today,
          installments: 1,
          description: `Estorno: ${refundOf.description || 'compra'}`.slice(0, 200),
        }
      : repeat
        ? repeatValues(repeat, accounts, initialAccount, today)
        : {
            // Receita não vai para o cartão: se a conta lembrada for um cartão, começa como despesa.
            type: initialType === 'Income' && initialAccount.type === 'CreditCard' ? 'Expense' : initialType,
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
  const isRefund = type === 'Refund'
  // Estorno abate despesa: usa as categorias de despesa e não vai para investimento (2.5).
  const roots = categories.filter((c) => c.type === (isRefund ? 'Expense' : type))
  const choices = isRefund ? accounts.filter((a) => a.type !== 'Investment') : accounts
  const { root: selectedRoot, label: selectedCategory } = resolveCategory(roots, categoryId)
  const brand = findBrand(description)

  // Autocompletar (docs/fase-2.md, 2.8). A sugestão nunca desfaz uma escolha da pessoa: conta e
  // categoria tocadas neste lançamento ficam. No "Lançar de novo", as copiadas contam como escolhidas.
  const vocabulary = useDescriptionSuggestions().data
  const touched = useRef({ accountTouched: !!repeat, categoryTouched: !!repeat })
  const [suggested, setSuggested] = useState({ account: false, category: false })
  const labels = useMemo(() => categoryLabels(categories), [categories])
  const accountNames = useMemo(() => new Map(accounts.map((a) => [a.id, a.name])), [accounts])
  const chooseAccount = (id: string) => {
    touched.current.accountTouched = true
    setSuggested((s) => ({ ...s, account: false }))
    set('accountId', id)
  }
  const chooseCategory = (id: string) => {
    touched.current.categoryTouched = true
    setSuggested((s) => ({ ...s, category: false }))
    set('categoryId', id)
  }
  const applySuggestion = (suggestion: DescriptionSuggestion): string => {
    const fill = suggestionFill(suggestion, touched.current, choices, (id) => !!resolveCategory(roots, id).root, {
      accountId,
      categoryId,
    })
    set('description', fill.description)
    const filled: string[] = []
    if (fill.categoryId) {
      set('categoryId', fill.categoryId)
      filled.push(`categoria ${labels.get(fill.categoryId)?.name ?? ''}`)
    }
    const next = fill.accountId ? accounts.find((a) => a.id === fill.accountId) : undefined
    if (next && fill.method) {
      // Marca a conta como já ajustada, senão o efeito de troca de conta voltaria o meio ao padrão.
      adjustedAccount.current = next.id
      set('accountId', next.id)
      set('method', fill.method)
      if (next.type !== 'CreditCard') set('installments', 1)
      filled.push(`conta ${next.name}, ${paymentMethodLabels[fill.method]}`)
    }
    setSuggested({ account: !!next, category: !!fill.categoryId })
    const kept = [
      fill.keptAccount && `conta ${accountNames.get(accountId) ?? ''}`,
      fill.keptCategory && `categoria ${labels.get(categoryId)?.name ?? ''}`,
    ].filter(Boolean)
    return [
      `${fill.description} escolhido.`,
      filled.length > 0 && `Preenchido: ${filled.join(' e ')}.`,
      kept.length > 0 && `Mantido o que você escolheu: ${kept.join(' e ')}.`,
    ]
      .filter(Boolean)
      .join(' ')
  }

  // Trocar a conta ajusta o meio de pagamento; o cartão não recebe receita (docs/fase-1.md). Ao
  // lançar de novo, a conta de partida mantém o meio e as parcelas copiados.
  const adjustedAccount = useRef<string | undefined>(undefined)
  useEffect(() => {
    if (!account || refundOf || adjustedAccount.current === account.id) return
    const first = adjustedAccount.current === undefined
    adjustedAccount.current = account.id
    if (first && repeat) return
    form.setValue('method', defaultPaymentMethod(account.type))
    if (account.type === 'CreditCard' && form.getValues('type') === 'Income') form.setValue('type', 'Expense')
    if (account.type !== 'CreditCard') form.setValue('installments', 1)
  }, [account, form, refundOf, repeat])

  // Estorno não vai para conta de investimento: volta para uma conta aceita.
  useEffect(() => {
    if (isRefund && account?.type === 'Investment' && choices[0]) form.setValue('accountId', choices[0].id)
  }, [isRefund, account, choices, form])

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
        installments: isCard && !isRefund ? values.installments : 1,
        refundedTransactionId: refundOf?.id ?? null,
      })
      if (!refundOf) saveLastAccountId(values.accountId)
      // "Lançar de novo" no próprio aviso: para lançar a fatura em lote (docs/fase-1.md, 5.1).
      const again: RepeatDraft | undefined =
        values.type === 'Refund'
          ? undefined
          : {
              type: values.type,
              amountCents: values.amountCents,
              accountId: values.accountId,
              categoryId: values.categoryId,
              method: values.method,
              description: values.description.trim(),
              installments: isCard ? values.installments : 1,
            }
      toast.success(isRefund ? 'Estorno lançado' : 'Lançamento salvo', {
        description: [
          values.description.trim() || null,
          created.length > 1 ? describeInstallments(values.amountCents, created.length) : formatCents(values.amountCents),
        ]
          .filter(Boolean)
          .join(' · '),
        ...(again && {
          duration: 8000,
          action: { label: 'Lançar de novo', onClick: () => navigate('/lancar', { state: { repeat: again } }) },
        }),
      })
      // Abre o mês da compra: um lançamento antigo não some da vista.
      navigate(`/lancamentos?mes=${values.purchaseDate.slice(0, 7)}`, { replace: true })
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
            {isSubmitting
              ? 'Lançando…'
              : `${isRefund ? 'Lançar estorno' : 'Lançar'}${amountCents > 0 ? ` ${formatCents(amountCents)}` : ''}`}
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

        {refundOf && <RefundOfBanner purchase={refundOf} />}
        {repeat && <RepeatBanner description={repeat.description} />}

        <div className="flex flex-col items-center gap-5">
          {!refundOf && (
            <TypeToggle
              value={type}
              incomeDisabled={isCard}
              onChange={(value) => set('type', value)}
              onTransfer={onTransfer}
              withRefund
            />
          )}
          <AmountField
            value={amountCents}
            onChange={(cents) => set('amountCents', cents)}
            income={type === 'Income'}
            error={errors.amountCents?.message}
            autoFocus
          />
          {isRefund && (
            <p className="-mt-3 max-w-xs text-center text-xs text-muted-foreground">
              Abate uma despesa. No cartão, entra na fatura aberta na data do estorno.
            </p>
          )}
          {repeat && !isRefund && amountCents === repeat.amountCents && (
            <p className="-mt-3 max-w-xs text-center text-xs text-muted-foreground">
              Mesmo valor da última vez. Digite para trocar.
            </p>
          )}
        </div>

        <Section title="Descrição" aside={brand ? `${brand.name} reconhecido` : 'opcional'}>
          <DescriptionCombobox
            value={description}
            onChange={(text) => set('description', text)}
            onChoose={applySuggestion}
            vocabulary={vocabulary ?? []}
            type={type}
            today={today}
            leading={<EntryTile description={description} category={selectedCategory} />}
            tileFor={(s, size) => (
              <EntryTile description={s.description} category={s.categoryId ? labels.get(s.categoryId) : undefined} size={size} />
            )}
            detailsOf={(s) =>
              [s.categoryId ? labels.get(s.categoryId)?.name : 'Sem categoria', accountNames.get(s.accountId)]
                .filter(Boolean)
                .join(' · ')
            }
          />
          <FieldError message={errors.description?.message} />
        </Section>

        <Section title="Conta" aside={suggested.account ? 'sugerida' : undefined}>
          <ChipRow>
            {choices.map((a) => (
              <Chip key={a.id} selected={a.id === accountId} onClick={() => chooseAccount(a.id)}>
                <AccountTile name={a.name} type={a.type} size="sm" />
                {a.name}
              </Chip>
            ))}
          </ChipRow>
        </Section>

        {isCard && !isRefund && (
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

        <Section title="Categoria" aside={suggested.category ? 'sugerida' : undefined}>
          <CategoryPicker roots={roots} value={categoryId} onChange={chooseCategory} />
        </Section>

        <Section title="Data">
          <DateChooser value={purchaseDate} onChange={(date) => set('purchaseDate', date)} error={errors.purchaseDate?.message} />
        </Section>

        {/* Fechado, mostra a forma escolhida: dá para conferir sem abrir. */}
        <details className="group surface rounded-2xl open:pb-4">
          <summary className="flex cursor-pointer list-none items-center gap-3 px-4 py-3 text-sm">
            <span className="text-muted-foreground">Pagamento</span>
            <span className="ml-auto font-medium">{paymentMethodLabels[method]}</span>
            <ChevronDown className="size-4 text-muted-foreground transition-transform group-open:rotate-180" />
          </summary>
          <div className="px-4">
            <ChipRow>
              {(isCard ? (['Credit'] as PaymentMethod[]) : simpleMethods).map((m) => (
                <Chip key={m} selected={m === method} onClick={() => set('method', m)}>
                  {paymentMethodLabels[m]}
                </Chip>
              ))}
            </ChipRow>
          </div>
        </details>
      </form>
    </Shell>
  )
}

// O que está sendo estornado, com o quanto já voltou. Tocar no X desiste e abre o lançamento comum.
function RefundOfBanner({ purchase }: { purchase: Transaction }) {
  const refunded = purchase.refundedCents ?? 0
  const total = refunded + (purchase.refundableCents ?? 0)
  return (
    <div className="flex items-center gap-3 rounded-2xl bg-muted/60 py-2.5 pr-2 pl-4 text-sm">
      <Undo2 className="size-4 shrink-0 text-muted-foreground" />
      <div className="flex min-w-0 flex-1 flex-col">
        <span className="truncate font-medium">Estornando {purchase.description || 'uma compra'}</span>
        <span className="text-xs text-muted-foreground tabular-nums">
          {refunded > 0 ? `Já estornado ${formatCents(refunded)} de ${formatCents(total)}` : `Compra de ${formatCents(total)}`}
        </span>
      </div>
      <Button asChild variant="ghost" size="icon" className="rounded-full" aria-label="Lançar sem estornar a compra">
        <Link to="/lancar" replace>
          <X />
        </Link>
      </Button>
    </div>
  )
}

// De onde veio o preenchimento. Tocar no X desiste e abre um lançamento em branco.
function RepeatBanner({ description }: { description: string }) {
  return (
    <div data-tint="repeat" className="tinted-action flex items-center gap-3 rounded-2xl py-2.5 pr-2 pl-4 text-sm">
      <CopyPlus className="size-4 shrink-0" />
      <span className="min-w-0 flex-1 truncate font-medium">Lançando de novo{description ? `: ${description}` : ''}</span>
      <Button
        asChild
        variant="ghost"
        size="icon"
        className="rounded-full text-current hover:bg-transparent"
        aria-label="Começar em branco"
      >
        <Link to="/lancar" replace>
          <X />
        </Link>
      </Button>
    </div>
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
function TransferComposer({ accounts, onEntry }: { accounts: Account[]; onEntry: (type: EntryType) => void }) {
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
      navigate(`/lancamentos?mes=${values.date.slice(0, 7)}`, { replace: true })
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
          <TypeToggle value="Transfer" incomeDisabled={false} onChange={onEntry} onTransfer={() => {}} withRefund />
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
