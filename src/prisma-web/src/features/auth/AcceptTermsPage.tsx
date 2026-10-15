import { useState } from 'react'
import { Link, useLocation, useNavigate } from 'react-router'
import { Alert, AlertDescription } from '@/components/ui/alert'
import { Button } from '@/components/ui/button'
import { ApiError } from '@/lib/api'
import { AuthCard, AuthShell } from './AuthShell'
import { useAcceptTerms, useLogout } from './queries'
import { TermsConsent } from './TermsConsent'

// O aceite de quem já tem conta (etapa A.4): quem se cadastrou antes do aceite existir, ou aceitou uma versão anterior,
// passa por aqui antes do app (RequireAuth). Quem não concorda pode sair ou apagar a conta (os termos, seção 10).
export function AcceptTermsPage() {
  const navigate = useNavigate()
  const from = (useLocation().state as { from?: string } | null)?.from ?? '/'
  const accept = useAcceptTerms()
  const logout = useLogout()
  const [checked, setChecked] = useState(false)

  const error = accept.error
    ? accept.error instanceof ApiError
      ? accept.error.message
      : 'Não foi possível conectar. Tente novamente.'
    : null

  return (
    <AuthShell>
      <AuthCard
        title="Termos de uso e privacidade"
        description="Para continuar no Prisma Money, leia e aceite os termos de uso e a política de privacidade."
      >
        {error && (
          <Alert variant="destructive">
            <AlertDescription>{error}</AlertDescription>
          </Alert>
        )}
        <TermsConsent checked={checked} onCheckedChange={setChecked} />
        <Button
          className="mt-2 h-11 w-full rounded-xl text-base"
          disabled={!checked || accept.isPending}
          onClick={() => accept.mutate(undefined, { onSuccess: () => navigate(from, { replace: true }) })}
        >
          {accept.isPending ? 'Aceitando…' : 'Aceitar e continuar'}
        </Button>
      </AuthCard>
      <div className="flex flex-col items-center gap-2 text-sm text-muted-foreground">
        <button
          type="button"
          className="font-medium text-foreground underline underline-offset-4"
          disabled={logout.isPending}
          onClick={async () => {
            await logout.mutateAsync()
            navigate('/entrar', { replace: true })
          }}
        >
          Sair
        </button>
        <Link to="/apagar-conta" className="underline-offset-4 hover:text-foreground hover:underline">
          Prefiro apagar minha conta
        </Link>
      </div>
    </AuthShell>
  )
}
