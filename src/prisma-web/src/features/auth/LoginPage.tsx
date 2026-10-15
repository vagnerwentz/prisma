import { useLocation, useNavigate } from 'react-router'
import { AuthForm } from './AuthForm'
import { useLogin } from './queries'

export function LoginPage() {
  const login = useLogin()
  const navigate = useNavigate()
  const from = (useLocation().state as { from?: string } | null)?.from ?? '/'

  return (
    <AuthForm
      mode="login"
      onSubmit={async ({ email, password }) => {
        await login.mutateAsync({ email, password })
        navigate(from, { replace: true })
      }}
    />
  )
}
