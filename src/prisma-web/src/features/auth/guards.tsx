import { Navigate, Outlet, useLocation } from 'react-router'
import { PrismLogo } from '@/components/brand/PrismLogo'
import { useCurrentUser } from './queries'

function FullScreenMessage({ children }: { children: string }) {
  return (
    <div className="flex min-h-dvh flex-col items-center justify-center gap-4 p-4 text-muted-foreground">
      <PrismLogo animated className="size-14 text-foreground" />
      <span className="sr-only">{children}</span>
    </div>
  )
}

// Rotas que exigem sessão: sem ela, vai para /entrar e volta depois do login.
export function RequireAuth() {
  const { data: user, isPending, isError } = useCurrentUser()
  const location = useLocation()

  if (isPending) return <FullScreenMessage>Carregando…</FullScreenMessage>
  if (isError) return <FullScreenMessage>Não foi possível conectar. Tente novamente em instantes.</FullScreenMessage>
  if (!user) return <Navigate to="/entrar" replace state={{ from: location.pathname }} />
  return <Outlet />
}

// Telas de entrada: quem já está logado vai direto para o início.
export function RedirectIfAuthenticated() {
  const { data: user, isPending } = useCurrentUser()

  if (isPending) return <FullScreenMessage>Carregando…</FullScreenMessage>
  if (user) return <Navigate to="/" replace />
  return <Outlet />
}
