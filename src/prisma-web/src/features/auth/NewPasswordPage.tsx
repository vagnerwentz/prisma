import { useState } from 'react'
import { Link, useNavigate } from 'react-router'
import { toast } from 'sonner'
import { PasswordField } from '@/components/PasswordField'
import { Alert, AlertDescription } from '@/components/ui/alert'
import { Button } from '@/components/ui/button'
import { Label } from '@/components/ui/label'
import { ApiError } from '@/lib/api'
import { AuthCard, AuthShell } from './AuthShell'
import { useResetPassword } from './queries'
import { useEmailLink } from './useEmailLink'

// O link de senha nova (docs/email.md, 5.2): /nova-senha#u=<usuário>&t=<token> (useEmailLink). Salvar troca a senha,
// tira a conta dos outros aparelhos e entra.
export function NewPasswordPage() {
  const navigate = useNavigate()
  const { userId, token, complete } = useEmailLink()
  const reset = useResetPassword()
  const [password, setPassword] = useState('')
  const [repeated, setRepeated] = useState('')
  const [checked, setChecked] = useState(false)

  const error = reset.error instanceof ApiError ? reset.error : null
  if (!complete || error?.code === 'InvalidLink') return <InvalidLink />

  const tooShort = password.length < 8 ? 'A senha deve ter pelo menos 8 caracteres.' : null
  const different = password !== repeated ? 'As senhas não são iguais.' : null
  const fieldError = error?.fieldErrors.password ?? null
  const otherError = reset.error && !fieldError ? (error?.message ?? 'Não foi possível conectar. Tente novamente.') : null

  const submit = (event: React.FormEvent) => {
    event.preventDefault()
    setChecked(true)
    if (tooShort || different) return
    reset.mutate(
      { userId, token, password },
      {
        onSuccess: () => {
          toast.success('Senha nova salva', { description: 'Os outros aparelhos saem da conta em alguns minutos.' })
          navigate('/', { replace: true })
        },
      },
    )
  }

  return (
    <AuthShell>
      <form onSubmit={submit} noValidate>
        <AuthCard title="Crie uma senha nova" description="Ela passa a valer em todos os aparelhos.">
          {otherError && (
            <Alert variant="destructive">
              <AlertDescription>{otherError}</AlertDescription>
            </Alert>
          )}
          <div className="flex flex-col gap-2">
            <Label htmlFor="password">Senha nova</Label>
            <PasswordField
              id="password"
              autoComplete="new-password"
              autoFocus
              className="h-11"
              aria-invalid={checked && !!(tooShort ?? fieldError)}
              value={password}
              onChange={(e) => setPassword(e.target.value)}
            />
            {checked && (tooShort ?? fieldError) && <p className="text-sm text-destructive">{tooShort ?? fieldError}</p>}
          </div>
          <div className="flex flex-col gap-2">
            <Label htmlFor="confirmPassword">Repita a senha</Label>
            <PasswordField
              id="confirmPassword"
              autoComplete="new-password"
              className="h-11"
              aria-invalid={checked && !tooShort && !!different}
              value={repeated}
              onChange={(e) => setRepeated(e.target.value)}
            />
            {checked && !tooShort && different && <p className="text-sm text-destructive">{different}</p>}
          </div>
          <Button type="submit" className="mt-2 h-11 w-full rounded-xl text-base" disabled={reset.isPending}>
            {reset.isPending ? 'Salvando…' : 'Salvar e entrar'}
          </Button>
        </AuthCard>
      </form>
    </AuthShell>
  )
}

// Link vencido (vale 1 hora), já usado ou cortado. A API não diz qual.
function InvalidLink() {
  return (
    <AuthShell>
      <AuthCard title="Este link não vale mais" description="Ele vale por 1 hora e só uma vez. Peça outro, é rápido.">
        <Button asChild className="h-11 w-full rounded-xl text-base">
          <Link to="/esqueci-senha">Pedir outro link</Link>
        </Button>
      </AuthCard>
      <p className="text-center text-sm text-muted-foreground">
        Lembrou a senha?{' '}
        <Link to="/entrar" className="font-medium text-foreground underline underline-offset-4">
          Entrar
        </Link>
      </p>
    </AuthShell>
  )
}
