import { zodResolver } from '@hookform/resolvers/zod'
import { useForm } from 'react-hook-form'
import { Link } from 'react-router'
import { z } from 'zod'
import { Alert, AlertDescription } from '@/components/ui/alert'
import { Button } from '@/components/ui/button'
import { Card, CardContent, CardDescription, CardFooter, CardHeader, CardTitle } from '@/components/ui/card'
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
    <main className="flex min-h-dvh items-center justify-center bg-muted p-4">
      <Card className="w-full max-w-sm">
        <CardHeader>
          <CardTitle className="text-2xl">{text.title}</CardTitle>
          <CardDescription>{text.description}</CardDescription>
        </CardHeader>
        <form onSubmit={submit} noValidate>
          <CardContent className="flex flex-col gap-4">
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
                aria-invalid={!!errors.password}
                {...form.register('password')}
              />
              {errors.password && <p className="text-sm text-destructive">{errors.password.message}</p>}
            </div>
          </CardContent>
          <CardFooter className="mt-6 flex flex-col gap-4">
            <Button type="submit" className="w-full" disabled={isSubmitting}>
              {isSubmitting ? text.submitting : text.submit}
            </Button>
            <p className="text-sm text-muted-foreground">
              {text.switchText}{' '}
              <Link to={text.switchTo} className="font-medium text-foreground underline underline-offset-4">
                {text.switchLink}
              </Link>
            </p>
          </CardFooter>
        </form>
      </Card>
    </main>
  )
}
