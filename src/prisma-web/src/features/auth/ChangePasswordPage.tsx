import { useState } from 'react'
import { ArrowLeft } from 'lucide-react'
import { Link, useNavigate } from 'react-router'
import { toast } from 'sonner'
import { PasswordField } from '@/components/PasswordField'
import { Alert, AlertDescription } from '@/components/ui/alert'
import { Button } from '@/components/ui/button'
import { Label } from '@/components/ui/label'
import { ApiError } from '@/lib/api'
import { useChangePassword, useCurrentUser } from './queries'

// "Trocar senha" (docs/email.md, 5.3; etapa H.4d), pelo menu da conta. Este aparelho continua na conta, os outros saem.
export function ChangePasswordPage() {
  const navigate = useNavigate()
  const { data: user } = useCurrentUser()
  const change = useChangePassword()
  const [current, setCurrent] = useState('')
  const [password, setPassword] = useState('')
  const [repeated, setRepeated] = useState('')
  const [checked, setChecked] = useState(false)

  const error = change.error instanceof ApiError ? change.error : null
  const wrongPassword = error?.code === 'WrongPassword'
  const tooShort = password.length < 8 ? 'A senha deve ter pelo menos 8 caracteres.' : null
  const different = password !== repeated ? 'As senhas não são iguais.' : null
  const missingCurrent = !current ? 'Informe a senha atual.' : null
  const currentError = checked ? (missingCurrent ?? (wrongPassword ? error.message : null)) : null
  const newError = checked ? (tooShort ?? error?.fieldErrors.newPassword ?? null) : null
  const repeatError = checked && !tooShort ? different : null
  const otherError =
    change.error && !wrongPassword && !error?.fieldErrors.newPassword
      ? (error?.message ?? 'Não foi possível conectar. Tente novamente.')
      : null

  const submit = (event: React.FormEvent) => {
    event.preventDefault()
    setChecked(true)
    if (missingCurrent || tooShort || different) return
    change.mutate(
      { currentPassword: current, newPassword: password },
      {
        onSuccess: () => {
          toast.success('Senha trocada', { description: 'Os outros aparelhos saem da conta em alguns minutos.' })
          navigate('/', { replace: true })
        },
      },
    )
  }

  return (
    <main className="mx-auto flex max-w-2xl flex-col gap-6 px-4 pt-3 pb-32">
      <nav>
        <Button asChild variant="ghost" size="sm" className="-ml-2 rounded-full text-muted-foreground">
          <Link to="/">
            <ArrowLeft />
            Resumo
          </Link>
        </Button>
      </nav>
      <div className="flex flex-col gap-3">
        <h1 className="font-display text-4xl leading-none">Trocar senha</h1>
        <div className="spectrum-line opacity-80" />
      </div>

      <form onSubmit={submit} noValidate className="surface flex flex-col gap-5 rounded-2xl p-5">
        <p className="text-[15px] leading-relaxed">
          A senha nova passa a valer em todos os aparelhos. Este continua na conta, os outros saem em alguns minutos.
        </p>

        {otherError && (
          <Alert variant="destructive">
            <AlertDescription>{otherError}</AlertDescription>
          </Alert>
        )}

        {/* O gerenciador de senhas sabe de qual conta é a senha trocada. */}
        <input type="email" autoComplete="username" value={user?.email ?? ''} readOnly hidden />
        <div className="flex flex-col gap-2">
          <Label htmlFor="currentPassword">Senha atual</Label>
          <PasswordField
            id="currentPassword"
            autoComplete="current-password"
            className="h-11"
            aria-invalid={!!currentError}
            value={current}
            onChange={(e) => setCurrent(e.target.value)}
          />
          {currentError && <p className="text-sm text-destructive">{currentError}</p>}
        </div>
        <div className="flex flex-col gap-2">
          <Label htmlFor="password">Senha nova</Label>
          <PasswordField
            id="password"
            autoComplete="new-password"
            className="h-11"
            aria-invalid={!!newError}
            value={password}
            onChange={(e) => setPassword(e.target.value)}
          />
          {newError && <p className="text-sm text-destructive">{newError}</p>}
        </div>
        <div className="flex flex-col gap-2">
          <Label htmlFor="confirmPassword">Repita a senha nova</Label>
          <PasswordField
            id="confirmPassword"
            autoComplete="new-password"
            className="h-11"
            aria-invalid={!!repeatError}
            value={repeated}
            onChange={(e) => setRepeated(e.target.value)}
          />
          {repeatError && <p className="text-sm text-destructive">{repeatError}</p>}
        </div>

        <Button
          type="submit"
          className="h-11 w-full rounded-xl text-base sm:w-auto sm:self-start sm:px-6"
          disabled={change.isPending}
        >
          {change.isPending ? 'Salvando…' : 'Salvar senha nova'}
        </Button>
      </form>
    </main>
  )
}
