import { useState } from 'react'
import { ArrowLeft } from 'lucide-react'
import { Link, useNavigate } from 'react-router'
import { toast } from 'sonner'
import { PasswordField } from '@/components/PasswordField'
import { Alert, AlertDescription } from '@/components/ui/alert'
import { Button } from '@/components/ui/button'
import { Label } from '@/components/ui/label'
import { ApiError } from '@/lib/api'
import { useCurrentUser, useDeleteAccount } from './queries'

// "Apagar conta" (etapa A.3), pelo menu da conta. Sem "Desfazer": a tela diz o que sai e pede a senha.
export function DeleteAccountPage() {
  const navigate = useNavigate()
  const { data: user } = useCurrentUser()
  const remove = useDeleteAccount()
  const [password, setPassword] = useState('')

  const error = remove.error instanceof ApiError ? remove.error : null
  const wrongPassword = error?.code === 'WrongPassword'
  const otherError = remove.error && !wrongPassword ? (error?.message ?? 'Não foi possível conectar. Tente novamente.') : null

  const submit = (event: React.FormEvent) => {
    event.preventDefault()
    if (!password) return
    remove.mutate(password, {
      onSuccess: () => {
        toast.success('Conta apagada', { description: 'Tudo o que era seu saiu do Prisma Money.' })
        navigate('/entrar', { replace: true })
      },
    })
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
        <h1 className="font-display text-4xl leading-none">Apagar conta</h1>
        <div className="spectrum-line opacity-80" />
      </div>

      <form onSubmit={submit} noValidate className="surface flex flex-col gap-5 rounded-2xl p-5">
        <div className="flex flex-col gap-3 text-[15px] leading-relaxed">
          <p>
            Tudo o que você registrou sai do Prisma Money de vez: contas, cartões, faturas, lançamentos, recorrências,
            categorias, carteira e notas de corretagem.
          </p>
          <p>
            <strong className="font-semibold">Não dá para desfazer.</strong> Se quiser voltar, é só criar uma conta nova
            com o mesmo e-mail, que começa vazia.
          </p>
          <p className="text-sm text-muted-foreground">
            As cópias de segurança, criptografadas e sem uso, são descartadas em até 90 dias.
          </p>
        </div>

        {otherError && (
          <Alert variant="destructive">
            <AlertDescription>{otherError}</AlertDescription>
          </Alert>
        )}

        {/* O gerenciador de senhas sabe de qual conta é a senha pedida. */}
        <input type="email" autoComplete="username" value={user?.email ?? ''} readOnly hidden />
        <div className="flex flex-col gap-2">
          <Label htmlFor="password">Para confirmar, digite sua senha</Label>
          <PasswordField
            id="password"
            autoComplete="current-password"
            className="h-11"
            aria-invalid={wrongPassword}
            value={password}
            onChange={(e) => setPassword(e.target.value)}
          />
          {wrongPassword && <p className="text-sm text-destructive">{error.message}</p>}
        </div>

        <Button
          type="submit"
          variant="destructive"
          className="h-11 w-full rounded-xl text-base sm:w-auto sm:self-start sm:px-6"
          disabled={remove.isPending || !password}
        >
          {remove.isPending ? 'Apagando…' : 'Apagar minha conta'}
        </Button>
      </form>
    </main>
  )
}
