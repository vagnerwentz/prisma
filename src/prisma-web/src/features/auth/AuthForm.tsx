import { zodResolver } from '@hookform/resolvers/zod'
import { useState } from 'react'
import { useForm, useWatch } from 'react-hook-form'
import { Link } from 'react-router'
import { z } from 'zod'
import { Alert, AlertDescription } from '@/components/ui/alert'
import { Button } from '@/components/ui/button'
import { PasswordField } from '@/components/PasswordField'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'
import { ApiError } from '@/lib/api'
import { AuthCard, AuthShell } from './AuthShell'
import { ResendConfirmationButton } from './ResendConfirmationButton'
import type { Credentials } from './queries'

type Mode = 'login' | 'register'

type Values = Credentials & { confirmPassword: string }

// Mesmas regras da API (Register.Validator e Login.Validator), para avisar antes do envio. Repetir a senha é só da
// tela: a API recebe uma senha.
const schemas = {
  login: z.object({
    email: z.string().trim().min(1, 'Informe o e-mail.'),
    password: z.string().min(1, 'Informe a senha.'),
    confirmPassword: z.string(),
  }),
  register: z
    .object({
      email: z.string().trim().min(1, 'Informe o e-mail.').email('E-mail inválido.'),
      password: z.string().min(1, 'Informe a senha.').min(8, 'A senha deve ter pelo menos 8 caracteres.'),
      confirmPassword: z.string().min(1, 'Repita a senha.'),
    })
    .refine((v) => v.password === v.confirmPassword, { path: ['confirmPassword'], message: 'As senhas não são iguais.' }),
}

const texts = {
  login: {
    title: 'Entrar',
    description: 'Acesse suas finanças.',
    submit: 'Entrar',
    submitting: 'Entrando…',
    switchText: 'Ainda não tem conta?',
    switchLink: 'Criar conta',
    switchTo: '/cadastro',
    passwordAutocomplete: 'current-password',
  },
  register: {
    title: 'Criar conta',
    description: 'Comece a organizar seu dinheiro.',
    submit: 'Criar conta',
    submitting: 'Criando conta…',
    switchText: 'Já tem conta?',
    switchLink: 'Entrar',
    switchTo: '/entrar',
    passwordAutocomplete: 'new-password',
  },
} as const

type Props = {
  mode: Mode
  onSubmit: (credentials: Credentials) => Promise<unknown>
}

export function AuthForm({ mode, onSubmit }: Props) {
  const text = texts[mode]
  const form = useForm<Values>({
    resolver: zodResolver(schemas[mode]),
    defaultValues: { email: '', password: '', confirmPassword: '' },
  })
  const { errors, isSubmitting } = form.formState
  const typedEmail = useWatch({ control: form.control, name: 'email' })
  // Login com a senha certa e o e-mail sem confirmar: o aviso traz o "Reenviar" para o e-mail digitado.
  const [unconfirmed, setUnconfirmed] = useState<string | null>(null)

  const submit = form.handleSubmit(async ({ email, password }) => {
    setUnconfirmed(null)
    try {
      await onSubmit({ email, password })
    } catch (error) {
      if (error instanceof ApiError && error.code === 'EmailNotConfirmed') {
        setUnconfirmed(email.trim())
      } else if (error instanceof ApiError) {
        for (const [field, message] of Object.entries(error.fieldErrors)) {
          if (field === 'email' || field === 'password') form.setError(field, { message })
        }
        if (Object.keys(error.fieldErrors).length === 0) form.setError('root', { message: error.message })
      } else {
        form.setError('root', { message: 'Não foi possível conectar. Tente novamente.' })
      }
    }
  })

  return (
    <AuthShell>
      <form onSubmit={submit} noValidate>
        <AuthCard title={text.title} description={text.description}>
          {errors.root && (
            <Alert variant="destructive">
              <AlertDescription>{errors.root.message}</AlertDescription>
            </Alert>
          )}
          {unconfirmed && (
            <div className="flex flex-col gap-3 rounded-2xl bg-muted p-4">
              <p className="text-sm leading-relaxed">
                Falta confirmar seu e-mail. Abra o link que enviamos para{' '}
                <strong className="font-semibold [overflow-wrap:anywhere]">{unconfirmed}</strong> ou peça outro.
              </p>
              <ResendConfirmationButton email={unconfirmed} />
            </div>
          )}
          <div className="flex flex-col gap-2">
            <Label htmlFor="email">E-mail</Label>
            <Input
              id="email"
              type="email"
              inputMode="email"
              autoComplete="email"
              autoFocus
              className="h-11 rounded-xl"
              aria-invalid={!!errors.email}
              {...form.register('email')}
            />
            {errors.email && <p className="text-sm text-destructive">{errors.email.message}</p>}
          </div>
          <div className="flex flex-col gap-2">
            <div className="flex items-baseline justify-between gap-3">
              <Label htmlFor="password">Senha</Label>
              {mode === 'login' && (
                // Leva o e-mail já digitado, para não pedir de novo.
                <Link
                  to="/esqueci-senha"
                  state={{ email: typedEmail.trim() }}
                  className="text-sm text-muted-foreground underline-offset-4 hover:text-foreground hover:underline"
                >
                  Esqueci a senha
                </Link>
              )}
            </div>
            <PasswordField
              id="password"
              autoComplete={text.passwordAutocomplete}
              className="h-11"
              aria-invalid={!!errors.password}
              {...form.register('password')}
            />
            {errors.password && <p className="text-sm text-destructive">{errors.password.message}</p>}
          </div>
          {mode === 'register' && (
            <div className="flex flex-col gap-2">
              <Label htmlFor="confirmPassword">Repita a senha</Label>
              <PasswordField
                id="confirmPassword"
                autoComplete="new-password"
                className="h-11"
                aria-invalid={!!errors.confirmPassword}
                {...form.register('confirmPassword')}
              />
              {errors.confirmPassword && <p className="text-sm text-destructive">{errors.confirmPassword.message}</p>}
            </div>
          )}
          <Button type="submit" className="mt-2 h-11 w-full rounded-xl text-base" disabled={isSubmitting}>
            {isSubmitting ? text.submitting : text.submit}
          </Button>
        </AuthCard>
      </form>

      <p className="text-center text-sm text-muted-foreground">
        {text.switchText}{' '}
        <Link to={text.switchTo} className="font-medium text-foreground underline underline-offset-4">
          {text.switchLink}
        </Link>
      </p>
    </AuthShell>
  )
}
