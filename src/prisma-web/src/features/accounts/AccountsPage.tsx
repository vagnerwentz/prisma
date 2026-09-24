import { zodResolver } from '@hookform/resolvers/zod'
import { Controller, useForm, useWatch } from 'react-hook-form'
import { z } from 'zod'
import { FieldError } from '@/components/FieldError'
import { MoneyInput } from '@/components/MoneyInput'
import { NativeSelect } from '@/components/NativeSelect'
import { Alert, AlertDescription } from '@/components/ui/alert'
import { Button } from '@/components/ui/button'
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card'
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

  return (
    <main className="mx-auto flex max-w-2xl flex-col gap-4 p-4 pb-28">
      <h1 className="text-lg font-semibold">Contas</h1>

      {accounts.isPending && <p className="text-muted-foreground">Carregando…</p>}
      {accounts.isError && (
        <Alert variant="destructive">
          <AlertDescription>Não foi possível carregar as contas.</AlertDescription>
        </Alert>
      )}
      {accounts.isSuccess && accounts.data.length === 0 && (
        <p className="text-muted-foreground">Você ainda não tem contas. Crie a primeira abaixo para começar a lançar.</p>
      )}
      {accounts.isSuccess && accounts.data.length > 0 && (
        <ul className="divide-y overflow-hidden rounded-xl border bg-background">
          {accounts.data.map((account) => (
            <AccountRow key={account.id} account={account} />
          ))}
        </ul>
      )}

      <NewAccountForm />
    </main>
  )
}

function AccountRow({ account }: { account: Account }) {
  const details = [accountTypeLabels[account.type]]
  if (account.type === 'CreditCard') details.push(`fecha dia ${account.closingDay}, vence dia ${account.dueDay}`)
  if (!account.isActive) details.push('inativa')

  return (
    <li className="flex items-center justify-between gap-3 px-4 py-3">
      <div className="min-w-0">
        <p className="truncate font-medium">{account.name}</p>
        <p className="truncate text-sm text-muted-foreground">{details.join(' · ')}</p>
      </div>
      {account.type !== 'CreditCard' && (
        <span className="shrink-0 text-sm tabular-nums text-muted-foreground">{formatCents(account.initialBalanceCents)}</span>
      )}
    </li>
  )
}

function NewAccountForm() {
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
    } catch (error) {
      form.setError('root', {
        message: error instanceof ApiError ? error.message : 'Não foi possível conectar. Tente novamente.',
      })
    }
  })

  return (
    <Card>
      <CardHeader>
        <CardTitle>Nova conta</CardTitle>
      </CardHeader>
      <CardContent>
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
          <Button type="submit" disabled={isSubmitting}>
            {isSubmitting ? 'Criando…' : 'Criar conta'}
          </Button>
        </form>
      </CardContent>
    </Card>
  )
}

// Campo de dia vazio fica indefinido (a validação pede o dia); texto vira número.
function toDay(value: unknown): number | undefined {
  const text = String(value ?? '').trim()
  return text === '' ? undefined : Number(text)
}
