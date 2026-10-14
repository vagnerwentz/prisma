import { useState } from 'react'
import { AuthForm } from './AuthForm'
import { CheckEmail } from './CheckEmail'
import { useRegister } from './queries'

// O cadastro não entra: a pessoa confirma o e-mail pelo link e entra por ele (docs/email.md, 5.1).
export function RegisterPage() {
  const register = useRegister()
  const [sentTo, setSentTo] = useState<string | null>(null)

  if (sentTo) return <CheckEmail email={sentTo} />

  return (
    <AuthForm
      mode="register"
      onSubmit={async (credentials) => {
        const { email } = await register.mutateAsync(credentials)
        setSentTo(email)
      }}
    />
  )
}
