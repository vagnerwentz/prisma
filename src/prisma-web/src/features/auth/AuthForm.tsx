import { zodResolver } from '@hookform/resolvers/zod'
import { useForm } from 'react-hook-form'
import { Link } from 'react-router'
import { z } from 'zod'
import { Alert, AlertDescription } from '@/components/ui/alert'
import { Button } from '@/components/ui/button'
import { PrismLogo } from '@/components/brand/PrismLogo'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'
import { ApiError } from '@/lib/api'
import type { Credentials } from './queries'

type Mode = 'login' | 'register'

// Mesmas regras da API (Register.Validator e Login.Validator), para avisar antes do envio.
const schemas = {
  login: z.object({
    email: z.string().trim().min(1, 'Informe o e-mail.'),
    password: z.string().min(1, 'Informe a senha.'),
  }),
  register: z.object({
    email: z.string().trim().min(1, 'Informe o e-mail.').email('E-mail inválido.'),
    password: z.string().min(1, 'Informe a senha.').min(8, 'A senha deve ter pelo menos 8 caracteres.'),
  }),
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
  const form = useForm<Credentials>({
    resolver: zodResolver(schemas[mode]),
    defaultValues: { email: '', password: '' },
  })
  const { errors, isSubmitting } = form.formState

  const submit = form.handleSubmit(async (values) => {
    try {
      await onSubmit(values)
    } catch (error) {
      if (error instanceof ApiError) {
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
    <main className="relative flex min-h-dvh flex-col items-center justify-center overflow-hidden px-6 py-10">
      {/* A luz que entra no prisma: um halo frio e suave ao fundo. */}
      <div
        aria-hidden
        className="pointer-events-none absolute -top-40 left-1/2 size-[36rem] -translate-x-1/2 rounded-full opacity-30 blur-3xl"
        style={{ background: 'var(--halo)' }}
      />
      <div className="relative flex w-full max-w-sm flex-col gap-8">
        <div className="flex flex-col items-center gap-3 text-center">
          <PrismLogo animated className="size-16" />
          <h1 className="font-display text-5xl leading-none">Prisma</h1>
          <p className="text-muted-foreground">Seu dinheiro, decomposto em cores.</p>
        </div>

        <form
          onSubmit={submit}
          noValidate
          className="flex flex-col gap-4 rounded-3xl border bg-card/80 p-5 shadow-sm backdrop-blur-xl"
        >
          <div className="flex flex-col gap-1">
            <h2 className="text-lg font-semibold">{text.title}</h2>
            <p className="text-sm text-muted-foreground">{text.description}</p>
          </div>
          {errors.root && (
            <Alert variant="destructive">
              <AlertDescription>{errors.root.message}</AlertDescription>
            </Alert>
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
            <Label htmlFor="password">Senha</Label>
            <Input
              id="password"
              type="password"
              autoComplete={text.passwordAutocomplete}
              className="h-11 rounded-xl"
              aria-invalid={!!errors.password}
              {...form.register('password')}
            />
            {errors.password && <p className="text-sm text-destructive">{errors.password.message}</p>}
          </div>
          <Button type="submit" className="mt-2 h-11 w-full rounded-xl text-base" disabled={isSubmitting}>
            {isSubmitting ? text.submitting : text.submit}
          </Button>
        </form>

        <p className="text-center text-sm text-muted-foreground">
          {text.switchText}{' '}
          <Link to={text.switchTo} className="font-medium text-foreground underline underline-offset-4">
            {text.switchLink}
          </Link>
        </p>
      </div>
    </main>
  )
}
