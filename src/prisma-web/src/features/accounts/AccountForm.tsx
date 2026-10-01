import { zodResolver } from '@hookform/resolvers/zod'
import { Controller, useForm, useWatch } from 'react-hook-form'
import { toast } from 'sonner'
import { z } from 'zod'
import { SheetFooterBar } from '@/components/BottomSheet'
import { AccountTile } from '@/components/brand/Tiles'
import { FieldError } from '@/components/FieldError'
import { MoneyInput } from '@/components/MoneyInput'
import { NativeSelect } from '@/components/NativeSelect'
import { Alert, AlertDescription } from '@/components/ui/alert'
import { Button } from '@/components/ui/button'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'
import { ApiError } from '@/lib/api'
import { findBrand } from '@/lib/brands/merchants'
import { accountTypeLabels, type AccountType } from './labels'
import { useCreateAccount, useUpdateAccount, type Account } from './queries'

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
      ctx.addIssue({
        code: 'custom',
        path: ['closingDay'],
        message: 'Cartão de crédito exige dia de fechamento.',
      })
    if (values.dueDay === undefined)
      ctx.addIssue({
        code: 'custom',
        path: ['dueDay'],
        message: 'Cartão de crédito exige dia de vencimento.',
      })
  })

type Values = z.infer<typeof schema>

const emptyForm: Values = {
  name: '',
  type: 'Checking',
  initialBalanceCents: 0,
  creditLimitCents: 0,
}

function valuesOf(account: Account): Values {
  return {
    name: account.name,
    type: account.type,
    initialBalanceCents: account.initialBalanceCents,
    closingDay: account.closingDay ?? undefined,
    dueDay: account.dueDay ?? undefined,
    creditLimitCents: account.creditLimitCents ?? 0,
  }
}

// Criar (sem `account`) ou editar uma conta. O tipo não muda depois de criado: trocar cartão por
// conta corrente invalidaria faturas e parcelas (regra do domínio).
export function AccountForm({
  account,
  formId,
  submitLabel,
  onSaved,
}: {
  account?: Account
  formId: string
  submitLabel: string
  onSaved: (account: Account) => void
}) {
  const createAccount = useCreateAccount()
  const updateAccount = useUpdateAccount()
  const form = useForm<Values>({
    resolver: zodResolver(schema),
    defaultValues: account ? valuesOf(account) : emptyForm,
  })
  const { errors, isSubmitting } = form.formState
  const type = useWatch({ control: form.control, name: 'type' })
  const name = useWatch({ control: form.control, name: 'name' })
  const isCard = type === 'CreditCard'
  // Como no Lançar: o ícone acompanha o nome digitado, e a marca reconhecida aparece ao lado do rótulo.
  const brand = findBrand(name)

  const submit = form.handleSubmit(async (values) => {
    const fields = {
      name: values.name,
      initialBalanceCents: isCard ? 0 : values.initialBalanceCents,
      closingDay: isCard ? (values.closingDay ?? null) : null,
      dueDay: isCard ? (values.dueDay ?? null) : null,
      creditLimitCents: isCard && values.creditLimitCents > 0 ? values.creditLimitCents : null,
    }
    try {
      const saved = account
        ? await updateAccount.mutateAsync({
            id: account.id,
            body: { ...fields, isActive: account.isActive },
          })
        : await createAccount.mutateAsync({ ...fields, type: values.type })
      toast.success(account ? 'Conta atualizada' : 'Conta criada', {
        description: values.name,
      })
      if (!account) form.reset(emptyForm)
      onSaved(saved)
    } catch (error) {
      form.setError('root', {
        message: error instanceof ApiError ? error.message : 'Não foi possível conectar. Tente novamente.',
      })
    }
  })

  return (
    <form id={formId} onSubmit={submit} noValidate className="flex min-h-0 flex-col">
      <div className="flex min-h-0 flex-col gap-4 overflow-y-auto overscroll-contain px-4 pt-2 pb-6">
        {errors.root && (
          <Alert variant="destructive">
            <AlertDescription>{errors.root.message}</AlertDescription>
          </Alert>
        )}
        <div className="flex flex-col gap-2">
          <div className="flex items-baseline justify-between">
            <Label htmlFor={`${formId}-name`}>Nome</Label>
            {brand && <span className="text-xs text-muted-foreground">{brand.name} reconhecido</span>}
          </div>
          <div className="relative">
            {/* Enfeite: a marca reconhecida já é dita ao lado do rótulo. */}
            <span aria-hidden className="pointer-events-none absolute top-1/2 left-2 -translate-y-1/2">
              <AccountTile name={name} type={type} />
            </span>
            <Input
              id={`${formId}-name`}
              placeholder="Ex.: Itaú, Nubank, Carteira"
              aria-invalid={!!errors.name}
              className="h-14 rounded-xl pl-14"
              {...form.register('name')}
            />
          </div>
          <FieldError message={errors.name?.message} />
        </div>
        <div className="flex flex-col gap-2">
          <Label htmlFor={`${formId}-type`}>Tipo</Label>
          {account ? (
            <p
              id={`${formId}-type`}
              className="flex h-11 items-center rounded-xl border bg-muted/40 px-3 text-sm text-muted-foreground"
            >
              {accountTypeLabels[account.type]} · o tipo não muda depois de criado
            </p>
          ) : (
            <NativeSelect id={`${formId}-type`} {...form.register('type')}>
              {(Object.entries(accountTypeLabels) as [AccountType, string][]).map(([value, label]) => (
                <option key={value} value={value}>
                  {label}
                </option>
              ))}
            </NativeSelect>
          )}
        </div>
        {isCard ? (
          <>
            <div className="grid grid-cols-2 gap-3">
              <div className="flex flex-col gap-2">
                <Label htmlFor={`${formId}-closingDay`}>Dia de fechamento</Label>
                <Input
                  id={`${formId}-closingDay`}
                  inputMode="numeric"
                  aria-invalid={!!errors.closingDay}
                  className="h-11 rounded-xl"
                  {...form.register('closingDay', { setValueAs: toDay })}
                />
                <FieldError message={errors.closingDay?.message} />
              </div>
              <div className="flex flex-col gap-2">
                <Label htmlFor={`${formId}-dueDay`}>Dia de vencimento</Label>
                <Input
                  id={`${formId}-dueDay`}
                  inputMode="numeric"
                  aria-invalid={!!errors.dueDay}
                  className="h-11 rounded-xl"
                  {...form.register('dueDay', { setValueAs: toDay })}
                />
                <FieldError message={errors.dueDay?.message} />
              </div>
            </div>
            {account && (
              <p className="text-xs leading-relaxed text-muted-foreground">
                Novos dias valem para as próximas faturas. As faturas já abertas mantêm as datas; ajuste-as na fatura, se preciso.
              </p>
            )}
            <div className="flex flex-col gap-2">
              <Label htmlFor={`${formId}-creditLimit`}>Limite (opcional)</Label>
              <Controller
                control={form.control}
                name="creditLimitCents"
                render={({ field }) => <MoneyInput id={`${formId}-creditLimit`} value={field.value} onChange={field.onChange} />}
              />
            </div>
          </>
        ) : (
          <div className="flex flex-col gap-2">
            <Label htmlFor={`${formId}-initialBalance`}>Saldo inicial</Label>
            <Controller
              control={form.control}
              name="initialBalanceCents"
              render={({ field }) => <MoneyInput id={`${formId}-initialBalance`} value={field.value} onChange={field.onChange} />}
            />
          </div>
        )}
      </div>
      <SheetFooterBar>
        <Button type="submit" size="lg" disabled={isSubmitting} className="h-12 w-full rounded-2xl text-base">
          {isSubmitting ? 'Salvando…' : submitLabel}
        </Button>
      </SheetFooterBar>
    </form>
  )
}

// Campo de dia vazio fica indefinido (a validação pede o dia); texto vira número.
function toDay(value: unknown): number | undefined {
  const text = String(value ?? '').trim()
  return text === '' ? undefined : Number(text)
}
