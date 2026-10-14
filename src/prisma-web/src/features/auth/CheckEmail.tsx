import { Link } from 'react-router'
import { AuthCard, AuthShell } from './AuthShell'
import { ResendConfirmationButton } from './ResendConfirmationButton'

// Depois do cadastro: a conta espera a confirmação do e-mail (docs/email.md, 5.1).
export function CheckEmail({ email }: { email: string }) {
  return (
    <AuthShell>
      <AuthCard title="Confira seu e-mail">
        <p className="text-[0.95rem] leading-relaxed">
          Enviamos um link para <strong className="font-semibold [overflow-wrap:anywhere]">{email}</strong>. Toque em “Confirmar
          e-mail” na mensagem para entrar.
        </p>
        <p className="text-sm text-muted-foreground">Não chegou em alguns minutos? Veja o spam ou peça outro.</p>
        <ResendConfirmationButton email={email} sentNow />
      </AuthCard>
      <p className="text-center text-sm text-muted-foreground">
        Já confirmou?{' '}
        <Link to="/entrar" className="font-medium text-foreground underline underline-offset-4">
          Entrar
        </Link>
      </p>
    </AuthShell>
  )
}
