import { useNavigate } from 'react-router'
import { AuthForm } from './AuthForm'
import { useRegister } from './queries'

// O cadastro já entra logado (a API devolve o cookie de sessão).
export function RegisterPage() {
  const register = useRegister()
  const navigate = useNavigate()

  return (
    <AuthForm
      mode="register"
      onSubmit={async (credentials) => {
        await register.mutateAsync(credentials)
        navigate('/', { replace: true })
      }}
    />
  )
}
