import { zodResolver } from '@hookform/resolvers/zod'
import { Plus, X } from 'lucide-react'
import { useState } from 'react'
import { Controller, useForm, useWatch } from 'react-hook-form'
import { toast } from 'sonner'
import { z } from 'zod'
import { AccountTile } from '@/components/brand/Tiles'
import { FieldError } from '@/components/FieldError'
import { MoneyInput } from '@/components/MoneyInput'
import { NativeSelect } from '@/components/NativeSelect'
import { Alert, AlertDescription } from '@/components/ui/alert'
import { Button } from '@/components/ui/button'
import { Skeleton } from '@/components/ui/skeleton'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'
import { ApiError } from '@/lib/api'
import { formatCents } from '@/lib/money'
import { accountTypeLabels, type AccountType } from './labels'
import { useAccounts, useCreateAccount, type Account } from './queries'

const day = z
  .number({ error: 'Informe o dia.' })
  .int('Informe o dia.')
  .min(1, 'O dia deve estar entre 1 e 31.')
  .max(31, 'O dia deve estar entre 1 e 31.')

// Mesmas regras de Account no domínio, para avisar antes do envio.
const schema = z
  .object({
    name: z.string().trim().min(1, 'Informe o nome da conta.').max(100, 'O nome deve ter no máximo 100 caracteres.'),
    type: z.enum(['Checking', 'CreditCard', 'Cash', 'Investment']),
    initialBalanceCents: z.number().int(),
    closingDay: day.optional(),
    dueDay: day.optional(),
    creditLimitCents: z.number().int(),
  })
  .superRefine((values, ctx) => {
    if (values.type !== 'CreditCard') return
    if (values.closingDay === undefined)
      ctx.addIssue({ code: 'custom', path: ['closingDay'], message: 'Cartão de crédito exige dia de fechamento.' })
    if (values.dueDay === undefined)
      ctx.addIssue({ code: 'custom', path: ['dueDay'], message: 'Cartão de crédito exige dia de vencimento.' })
  })

type Values = z.infer<typeof schema>

const emptyForm: Values = { name: '', type: 'Checking', initialBalanceCents: 0, creditLimitCents: 0 }

export function AccountsPage() {
  const accounts = useAccounts()
  const [creating, setCreating] = useState(false)
  const showForm = creating || (accounts.isSuccess && accounts.data.length === 0)

  return (
    <main className="mx-auto flex max-w-2xl flex-col gap-6 px-4 pt-5 pb-32">
      <div className="flex flex-col gap-3">
        <div className="flex items-end justify-between">
          <h1 className="font-display text-4xl leading-none">Contas</h1>
          {!showForm && (
            <Button size="sm" variant="secondary" className="rounded-full" onClick={() => setCreating(true)}>
              <Plus />
              Nova conta
            </Button>
          )}
        </div>
        <div className="spectrum-line opacity-80" />
      </div>

      {accounts.isPending && <Skeleton className="h-40 w-full rounded-2xl" />}
      {accounts.isError && (
        <Alert variant="destructive">
          <AlertDescription>Não foi possível carregar as contas.</AlertDescription>
        </Alert>
      )}
      {accounts.isSuccess && accounts.data.length === 0 && (
        <p className="text-muted-foreground">Crie a primeira conta para começar a lançar: corrente, cartão, carteira…</p>
      )}
      {accounts.isSuccess && accounts.data.length > 0 && (
        <ul className="overflow-hidden rounded-2xl border bg-card">
          {accounts.data.map((account) => (
            <AccountRow key={account.id} account={account} />
          ))}
        </ul>
      )}

      {showForm && <NewAccountForm onDone={() => setCreating(false)} canCancel={accounts.data?.length !== 0} />}
    </main>
  )
}

function AccountRow({ account }: { account: Account }) {
  const details = [account.type === 'CreditCard' ? 'Cartão' : accountTypeLabels[account.type]]
  if (account.type === 'CreditCard') details.push(`fecha dia ${account.closingDay}`, `vence dia ${account.dueDay}`)
  if (!account.isActive) details.push('inativa')

  return (
    <li className="flex items-center gap-3 px-4 py-3 [&+&]:border-t">
      <AccountTile name={account.name} type={account.type} />
      <div className="min-w-0 flex-1">
        <p className="truncate font-medium">{account.name}</p>
        <p className="truncate text-sm text-muted-foreground">{details.join(' · ')}</p>
      </div>
      {account.type !== 'CreditCard' && (
        <div className="shrink-0 text-right">
          <p className="text-[0.65rem] tracking-wide text-muted-foreground uppercase">Saldo inicial</p>
          <p className="text-sm tabular-nums">{formatCents(account.initialBalanceCents)}</p>
        </div>
      )}
    </li>
  )
}

function NewAccountForm({ onDone, canCancel }: { onDone: () => void; canCancel: boolean }) {
  const createAccount = useCreateAccount()
  const form = useForm<Values>({ resolver: zodResolver(schema), defaultValues: emptyForm })
  const { errors, isSubmitting } = form.formState
  const isCard = useWatch({ control: form.control, name: 'type' }) === 'CreditCard'

  const submit = form.handleSubmit(async (values) => {
    try {
      await createAccount.mutateAsync({
        name: values.name,
        type: values.type,
        initialBalanceCents: values.initialBalanceCents,
        closingDay: isCard ? (values.closingDay ?? null) : null,
        dueDay: isCard ? (values.dueDay ?? null) : null,
        creditLimitCents: isCard && values.creditLimitCents > 0 ? values.creditLimitCents : null,
      })
      form.reset(emptyForm)
      toast.success('Conta criada', { description: values.name })
      onDone()
    } catch (error) {
      form.setError('root', {
        message: error instanceof ApiError ? error.message : 'Não foi possível conectar. Tente novamente.',
      })
    }
  })

  return (
    <section className="rounded-2xl border bg-card p-4">
      <div className="mb-4 flex items-center justify-between">
        <h2 className="font-display text-2xl">Nova conta</h2>
        {canCancel && (
          <Button variant="ghost" size="icon" className="rounded-full" aria-label="Cancelar" onClick={onDone}>
            <X />
          </Button>
        )}
      </div>
      <div>
        <form onSubmit={submit} noValidate className="flex flex-col gap-4">
          {errors.root && (
            <Alert variant="destructive">
              <AlertDescription>{errors.root.message}</AlertDescription>
            </Alert>
          )}
          <div className="flex flex-col gap-2">
            <Label htmlFor="name">Nome</Label>
            <Input id="name" placeholder="Ex.: Itaú, Nubank, Carteira" aria-invalid={!!errors.name} {...form.register('name')} />
            <FieldError message={errors.name?.message} />
          </div>
          <div className="flex flex-col gap-2">
            <Label htmlFor="type">Tipo</Label>
            <NativeSelect id="type" {...form.register('type')}>
              {(Object.entries(accountTypeLabels) as [AccountType, string][]).map(([value, label]) => (
                <option key={value} value={value}>
                  {label}
                </option>
              ))}
            </NativeSelect>
          </div>
          {isCard ? (
            <>
              <div className="grid grid-cols-2 gap-3">
                <div className="flex flex-col gap-2">
                  <Label htmlFor="closingDay">Dia de fechamento</Label>
                  <Input
                    id="closingDay"
                    inputMode="numeric"
                    aria-invalid={!!errors.closingDay}
                    {...form.register('closingDay', { setValueAs: toDay })}
                  />
                  <FieldError message={errors.closingDay?.message} />
                </div>
                <div className="flex flex-col gap-2">
                  <Label htmlFor="dueDay">Dia de vencimento</Label>
                  <Input
                    id="dueDay"
                    inputMode="numeric"
                    aria-invalid={!!errors.dueDay}
                    {...form.register('dueDay', { setValueAs: toDay })}
                  />
                  <FieldError message={errors.dueDay?.message} />
                </div>
              </div>
              <div className="flex flex-col gap-2">
                <Label htmlFor="creditLimit">Limite (opcional)</Label>
                <Controller
                  control={form.control}
                  name="creditLimitCents"
                  render={({ field }) => <MoneyInput id="creditLimit" value={field.value} onChange={field.onChange} />}
                />
              </div>
            </>
          ) : (
            <div className="flex flex-col gap-2">
              <Label htmlFor="initialBalance">Saldo inicial</Label>
              <Controller
                control={form.control}
                name="initialBalanceCents"
                render={({ field }) => <MoneyInput id="initialBalance" value={field.value} onChange={field.onChange} />}
              />
            </div>
          )}
          <Button type="submit" disabled={isSubmitting} className="h-11 rounded-2xl">
            {isSubmitting ? 'Criando…' : 'Criar conta'}
          </Button>
        </form>
      </div>
    </section>
  )
}

// Campo de dia vazio fica indefinido (a validação pede o dia); texto vira número.
function toDay(value: unknown): number | undefined {
  const text = String(value ?? '').trim()
  return text === '' ? undefined : Number(text)
}
