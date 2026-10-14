import { useState } from 'react'
import { Link, useNavigate } from 'react-router'
import { toast } from 'sonner'
import { PasswordField } from '@/components/PasswordField'
import { Alert, AlertDescription } from '@/components/ui/alert'
import { Button } from '@/components/ui/button'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'
import { ApiError } from '@/lib/api'
import { AuthCard, AuthShell } from './AuthShell'
import { ResendConfirmationButton } from './ResendConfirmationButton'
import { useConfirmEmail } from './queries'
import { useEmailLink } from './useEmailLink'

// O link do e-mail de confirmação (docs/email.md, 5.1): /confirmar-email#u=<usuário>&t=<token> (useEmailLink).
// Confirmar pede a senha do cadastro: o link prova o e-mail, a senha prova quem criou a conta.
export function ConfirmEmailPage() {
  const navigate = useNavigate()
  const { userId, token, complete } = useEmailLink()
  const confirm = useConfirmEmail()
  const [password, setPassword] = useState('')

  const incomplete = !complete
  const error = confirm.error instanceof ApiError ? confirm.error : null
  if (incomplete || error?.code === 'InvalidLink') return <InvalidLink />

  const wrongPassword = error?.code === 'WrongPassword'
  const otherError = confirm.error && !wrongPassword ? (error?.message ?? 'Não foi possível conectar. Tente novamente.') : null

  const submit = (event: React.FormEvent) => {
    event.preventDefault()
    if (!password) return
    confirm.mutate(
      { userId, token, password },
      {
        onSuccess: () => {
          toast.success('E-mail confirmado', { description: 'Boas-vindas ao Prisma Money.' })
          navigate('/', { replace: true })
        },
      },
    )
  }

  return (
    <AuthShell>
      <form onSubmit={submit} noValidate>
        <AuthCard title="Confirme seu e-mail" description="Digite a senha que você criou no cadastro para ativar a conta.">
          {otherError && (
            <Alert variant="destructive">
              <AlertDescription>{otherError}</AlertDescription>
            </Alert>
          )}
          <div className="flex flex-col gap-2">
            <Label htmlFor="password">Senha</Label>
            <PasswordField
              id="password"
              autoComplete="current-password"
              autoFocus
              className="h-11"
              aria-invalid={wrongPassword}
              value={password}
              onChange={(e) => setPassword(e.target.value)}
            />
            {wrongPassword && (
              <p className="text-sm text-destructive">
                {error.message}{' '}
                <Link to="/esqueci-senha" className="font-medium text-foreground underline underline-offset-4">
                  Criar uma senha nova
                </Link>
              </p>
            )}
          </div>
          <Button type="submit" className="mt-2 h-11 w-full rounded-xl text-base" disabled={confirm.isPending || !password}>
            {confirm.isPending ? 'Confirmando…' : 'Confirmar e entrar'}
          </Button>
        </AuthCard>
      </form>
    </AuthShell>
  )
}

// Link vencido, já usado ou cortado. A API não diz qual, para não contar nada a quem tem só o link.
function InvalidLink() {
  const [email, setEmail] = useState('')
  const valid = /^\S+@\S+\.\S+$/.test(email.trim())

  return (
    <AuthShell>
      <AuthCard
        title="Este link não vale mais"
        description="Ele vale por 3 dias e só uma vez. Se você já confirmou, é só entrar."
      >
        <div className="flex flex-col gap-2">
          <Label htmlFor="email">Peça outro link</Label>
          <Input
            id="email"
            type="email"
            inputMode="email"
            autoComplete="email"
            className="h-11 rounded-xl"
            value={email}
            onChange={(e) => setEmail(e.target.value)}
          />
        </div>
        <ResendConfirmationButton email={valid ? email.trim() : ''} label="Enviar outro link" />
      </AuthCard>
      <p className="text-center text-sm text-muted-foreground">
        Já confirmou?{' '}
        <Link to="/entrar" className="font-medium text-foreground underline underline-offset-4">
          Entrar
        </Link>
      </p>
    </AuthShell>
  )
}
