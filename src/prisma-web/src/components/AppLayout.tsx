import { Outlet, useNavigate } from 'react-router'
import { Button } from '@/components/ui/button'
import { useLogout } from '@/features/auth/queries'

export function AppLayout() {
  const logout = useLogout()
  const navigate = useNavigate()

  return (
    <div className="min-h-dvh bg-muted">
      <header className="sticky top-0 z-10 flex items-center justify-between border-b bg-background px-4 py-3">
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
      <Outlet />
    </div>
  )
}
