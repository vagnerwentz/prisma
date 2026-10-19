import { Navigate, Outlet, useLocation } from 'react-router'
import { AppLayout } from '@/components/AppLayout'
import { NotFoundPage } from '@/components/NotFoundPage'
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

const acceptTermsPath = '/aceitar-termos'

// Rotas que exigem sessão: sem ela, vai para /entrar e volta depois do login.
export function RequireAuth() {
  const { data: user, isPending, isError } = useCurrentUser()
  const location = useLocation()

  if (isPending) return <FullScreenMessage>Carregando…</FullScreenMessage>
  if (isError) return <FullScreenMessage>Não foi possível conectar. Tente novamente em instantes.</FullScreenMessage>
  if (!user) return <Navigate to="/entrar" replace state={{ from: location.pathname }} />
  // Termos novos (ou nunca aceitos): o aceite vem antes do app. Apagar a conta continua aberto a quem não concorda.
  const accepting = location.pathname === acceptTermsPath
  if (user.mustAcceptTerms && !accepting && location.pathname !== '/apagar-conta')
    return <Navigate to={acceptTermsPath} replace state={{ from: location.pathname }} />
  // Aceito, volta para onde ia: a sessão atualizada chega aqui antes do navigate da própria tela.
  if (!user.mustAcceptTerms && accepting)
    return <Navigate to={(location.state as { from?: string } | null)?.from ?? '/'} replace />
  return <Outlet />
}

// Telas de entrada: quem já está logado vai direto para o início.
export function RedirectIfAuthenticated() {
  const { data: user, isPending } = useCurrentUser()

  if (isPending) return <FullScreenMessage>Carregando…</FullScreenMessage>
  if (user) return <Navigate to="/" replace />
  return <Outlet />
}

// A tela de admin só abre para quem é admin (docs/admin-mercado.md, 7); para os outros ela não existe: a mesma página de
// "endereço inexistente", sem revelar que há uma tela ali. Quem manda é o servidor, que confere a cada pedido.
export function RequireAdmin() {
  const { data: user } = useCurrentUser()
  return user?.isAdmin ? <AppLayout /> : <NotFoundPage />
}
