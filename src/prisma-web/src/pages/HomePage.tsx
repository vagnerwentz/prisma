import { useNavigate } from 'react-router'
import { Button } from '@/components/ui/button'
import { useCurrentUser, useLogout } from '@/features/auth/queries'

// Provisória: a lista de transações ocupa esta tela na etapa 1.12b.
export function HomePage() {
  const { data: user } = useCurrentUser()
  const logout = useLogout()
  const navigate = useNavigate()

  return (
    <div className="min-h-dvh bg-muted">
      <header className="flex items-center justify-between border-b bg-background px-4 py-3">
        <span className="text-lg font-semibold">Prisma</span>
        <Button
          variant="ghost"
          size="sm"
          disabled={logout.isPending}
          onClick={async () => {
            await logout.mutateAsync()
            navigate('/entrar', { replace: true })
          }}
        >
          Sair
        </Button>
      </header>
      <main className="p-4">
        <p className="text-muted-foreground">
          Você entrou como <span className="font-medium text-foreground">{user?.email}</span>.
        </p>
      </main>
    </div>
  )
}
