import { useState } from 'react'
import { Link, useLocation } from 'react-router'
import { Alert, AlertDescription } from '@/components/ui/alert'
import { Button } from '@/components/ui/button'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'
import { ApiError } from '@/lib/api'
import { AuthCard, AuthShell } from './AuthShell'
import { useForgotPassword } from './queries'

// "Esqueci a senha" (docs/email.md, 5.2). O e-mail digitado no login vem junto. A resposta é a mesma exista a conta ou
// não, e a tela diz isso com "Se houver uma conta".
export function ForgotPasswordPage() {
  const typed = (useLocation().state as { email?: string } | null)?.email ?? ''
  const [email, setEmail] = useState(typed)
  const [sentTo, setSentTo] = useState<string | null>(null)
  const forgot = useForgotPassword()
  const valid = /^\S+@\S+\.\S+$/.test(email.trim())

  const submit = (event: React.FormEvent) => {
    event.preventDefault()
    if (!valid) return
    forgot.mutate(email.trim(), { onSuccess: () => setSentTo(email.trim()) })
  }

  const error =
    forgot.error instanceof ApiError ? forgot.error.message : forgot.error ? 'Não foi possível conectar. Tente novamente.' : null

  return (
    <AuthShell>
      {sentTo ? (
        <AuthCard title="Confira seu e-mail">
          <p className="text-[0.95rem] leading-relaxed">
            Se houver uma conta com <strong className="font-semibold [overflow-wrap:anywhere]">{sentTo}</strong>, enviamos um link
            para criar uma senha nova. Ele vale por 1 hora.
          </p>
          <p className="text-sm text-muted-foreground">Não chegou em alguns minutos? Veja o spam ou peça de novo.</p>
          <Button variant="outline" className="h-11 w-full rounded-xl text-base" onClick={() => setSentTo(null)}>
            Pedir de novo
          </Button>
        </AuthCard>
      ) : (
        <form onSubmit={submit} noValidate>
          <AuthCard title="Esqueci a senha" description="Enviamos um link para você criar uma senha nova.">
            {error && (
              <Alert variant="destructive">
                <AlertDescription>{error}</AlertDescription>
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
                value={email}
                onChange={(e) => setEmail(e.target.value)}
              />
            </div>
            <Button type="submit" className="mt-2 h-11 w-full rounded-xl text-base" disabled={!valid || forgot.isPending}>
              {forgot.isPending ? 'Enviando…' : 'Enviar link'}
            </Button>
          </AuthCard>
        </form>
      )}
      <p className="text-center text-sm text-muted-foreground">
        Lembrou?{' '}
        <Link to="/entrar" className="font-medium text-foreground underline underline-offset-4">
          Entrar
        </Link>
      </p>
    </AuthShell>
  )
}
