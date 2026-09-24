import { zodResolver } from '@hookform/resolvers/zod'
import { useEffect, useMemo } from 'react'
import { Controller, useForm, useWatch } from 'react-hook-form'
import { Link, useNavigate } from 'react-router'
import { z } from 'zod'
import { FieldError } from '@/components/FieldError'
import { MoneyInput } from '@/components/MoneyInput'
import { NativeSelect } from '@/components/NativeSelect'
import { Alert, AlertDescription } from '@/components/ui/alert'
import { Button } from '@/components/ui/button'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'
import { defaultPaymentMethod, paymentMethodLabels, type PaymentMethod } from '@/features/accounts/labels'
import { useAccounts, type Account } from '@/features/accounts/queries'
import { useCategories, type CategoryNode } from '@/features/categories/queries'
import { ApiError } from '@/lib/api'
import { todayInSaoPaulo } from '@/lib/dates'
import { describeInstallments } from '@/lib/money'
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
    return <p className="p-4 text-center text-muted-foreground">Carregando…</p>
  }
  if (accounts.isError || categories.isError) {
    return (
      <main className="p-4">
        <Alert variant="destructive">
          <AlertDescription>Não foi possível carregar contas e categorias. Tente novamente.</AlertDescription>
        </Alert>
      </main>
    )
  }
  if (activeAccounts.length === 0) {
    return (
      <main className="mx-auto flex max-w-2xl flex-col gap-4 p-4">
        <p className="text-muted-foreground">Para lançar, crie antes uma conta.</p>
        <Button asChild>
          <Link to="/contas">Criar conta</Link>
        </Button>
      </main>
    )
  }

  return <NewTransactionForm accounts={activeAccounts} categories={categories.data} />
}

function NewTransactionForm({ accounts, categories }: { accounts: Account[]; categories: CategoryNode[] }) {
  const navigate = useNavigate()
  const createTransaction = useCreateTransaction()

  const initialAccount = accounts.find((a) => a.id === readLastAccountId()) ?? accounts[0]
  const form = useForm<Values>({
    resolver: zodResolver(schema),
    defaultValues: {
      type: 'Expense',
      amountCents: 0,
      accountId: initialAccount.id,
      categoryId: '',
      method: defaultPaymentMethod(initialAccount.type),
      purchaseDate: todayInSaoPaulo(),
      installments: 1,
      description: '',
    },
  })
  const { errors, isSubmitting } = form.formState

  const [type, accountId, categoryId, amountCents, installments] = useWatch({
    control: form.control,
    name: ['type', 'accountId', 'categoryId', 'amountCents', 'installments'],
  })
  const account = accounts.find((a) => a.id === accountId)
  const isCard = account?.type === 'CreditCard'
  const categoriesOfType = categories.filter((c) => c.type === type)

  // Trocar a conta ajusta o meio de pagamento; o cartão só aceita despesa (docs/fase-1.md).
  useEffect(() => {
    if (!account) return
    form.setValue('method', defaultPaymentMethod(account.type))
    if (account.type === 'CreditCard') form.setValue('type', 'Expense')
    else form.setValue('installments', 1)
  }, [account, form])

  // Categoria de receita não serve para despesa, e vice-versa.
  useEffect(() => {
    const stillValid = categoriesOfType.some((c) => c.id === categoryId || c.subcategories.some((s) => s.id === categoryId))
    if (categoryId && !stillValid) form.setValue('categoryId', '')
  }, [categoriesOfType, categoryId, form])

  const submit = form.handleSubmit(async (values) => {
    try {
      await createTransaction.mutateAsync({
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
      navigate('/', { replace: true })
    } catch (error) {
      form.setError('root', {
        message: error instanceof ApiError ? error.message : 'Não foi possível conectar. Tente novamente.',
      })
    }
  })

  return (
    <main className="mx-auto max-w-2xl p-4 pb-28">
      <form onSubmit={submit} noValidate className="flex flex-col gap-4">
        <h1 className="text-lg font-semibold">Novo lançamento</h1>

        {errors.root && (
          <Alert variant="destructive">
            <AlertDescription>{errors.root.message}</AlertDescription>
          </Alert>
        )}

        <div className="grid grid-cols-2 gap-2 rounded-lg bg-background p-1">
          {(['Expense', 'Income'] as const).map((option) => (
            <button
              key={option}
              type="button"
              disabled={option === 'Income' && isCard}
              onClick={() => form.setValue('type', option)}
              className={cn(
                'rounded-md py-2 text-sm font-medium disabled:opacity-40',
                type === option ? 'bg-primary text-primary-foreground' : 'text-muted-foreground',
              )}
            >
              {option === 'Expense' ? 'Despesa' : 'Receita'}
            </button>
          ))}
        </div>

        <div className="flex flex-col gap-2">
          <Label htmlFor="amount">Valor</Label>
          <Controller
            control={form.control}
            name="amountCents"
            render={({ field }) => (
              <MoneyInput
                id="amount"
                autoFocus
                className="h-14 text-3xl font-semibold tabular-nums md:text-3xl"
                aria-invalid={!!errors.amountCents}
                value={field.value}
                onChange={field.onChange}
              />
            )}
          />
          <FieldError message={errors.amountCents?.message} />
        </div>

        <div className="flex flex-col gap-2">
          <Label htmlFor="account">Conta</Label>
          <NativeSelect id="account" aria-invalid={!!errors.accountId} {...form.register('accountId')}>
            {accounts.map((a) => (
              <option key={a.id} value={a.id}>
                {a.name}
              </option>
            ))}
          </NativeSelect>
        </div>

        {isCard && (
          <div className="flex flex-col gap-2">
            <Label htmlFor="installments">Parcelas</Label>
            <NativeSelect id="installments" {...form.register('installments', { valueAsNumber: true })}>
              {Array.from({ length: maxInstallments }, (_, i) => i + 1).map((n) => (
                <option key={n} value={n}>
                  {n === 1 ? 'À vista' : `${n}x`}
                </option>
              ))}
            </NativeSelect>
            {installments > 1 && amountCents >= installments && (
              <p className="text-sm text-muted-foreground">{describeInstallments(amountCents, installments)}</p>
            )}
          </div>
        )}

        <div className="flex flex-col gap-2">
          <Label htmlFor="category">Categoria</Label>
          <NativeSelect id="category" {...form.register('categoryId')}>
            <option value="">Sem categoria</option>
            {categoriesOfType.map((root) =>
              root.subcategories.length === 0 ? (
                <option key={root.id} value={root.id}>
                  {root.name}
                </option>
              ) : (
                <optgroup key={root.id} label={root.name}>
                  <option value={root.id}>{root.name} (geral)</option>
                  {root.subcategories.map((sub) => (
                    <option key={sub.id} value={sub.id}>
                      {sub.name}
                    </option>
                  ))}
                </optgroup>
              ),
            )}
          </NativeSelect>
        </div>

        <div className="grid grid-cols-2 gap-3">
          <div className="flex flex-col gap-2">
            <Label htmlFor="date">Data</Label>
            <Input id="date" type="date" aria-invalid={!!errors.purchaseDate} {...form.register('purchaseDate')} />
            <FieldError message={errors.purchaseDate?.message} />
          </div>
          <div className="flex flex-col gap-2">
            <Label htmlFor="method">Pagamento</Label>
            <NativeSelect id="method" disabled={isCard} {...form.register('method')}>
              {(isCard ? (['Credit'] as PaymentMethod[]) : simpleMethods).map((m) => (
                <option key={m} value={m}>
                  {paymentMethodLabels[m]}
                </option>
              ))}
            </NativeSelect>
          </div>
        </div>

        <div className="flex flex-col gap-2">
          <Label htmlFor="description">Descrição (opcional)</Label>
          <Input id="description" autoComplete="off" {...form.register('description')} />
          <FieldError message={errors.description?.message} />
        </div>

        <Button type="submit" size="lg" disabled={isSubmitting}>
          {isSubmitting ? 'Lançando…' : 'Lançar'}
        </Button>
      </form>
    </main>
  )
}
