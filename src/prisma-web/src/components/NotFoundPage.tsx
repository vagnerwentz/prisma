import { ErrorScreen } from '@/components/ErrorScreen'
import { useCurrentUser } from '@/features/auth/queries'

// Endereço que não existe. Espera saber se há sessão (uma consulta ao /auth/me, que o resto do
// app reaproveita) para o botão não trocar de texto na frente do usuário.
export function NotFoundPage() {
  const { data: user, isPending } = useCurrentUser()
  if (isPending) return null
  return <ErrorScreen kind="not-found" signedOut={user === null} />
}
