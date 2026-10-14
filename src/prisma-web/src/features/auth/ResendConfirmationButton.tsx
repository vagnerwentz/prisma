import { useEffect, useState } from 'react'
import { Button } from '@/components/ui/button'
import { ApiError } from '@/lib/api'
import { useResendConfirmation } from './queries'

// A fila manda no máximo um e-mail de confirmação por minuto para cada pessoa (docs/email.md, seção 3): o botão
// espera o mesmo tempo, em vez de aceitar um toque que não manda nada.
const WAIT_SECONDS = 60

// "Reenviar e-mail", com a espera à vista depois de cada envio.
export function ResendConfirmationButton({
  email,
  label = 'Reenviar e-mail',
  sentNow = false,
}: {
  email: string
  label?: string
  // A tela que acabou de pedir um e-mail (o cadastro) já começa esperando.
  sentNow?: boolean
}) {
  const resend = useResendConfirmation()
  const [waitUntil, setWaitUntil] = useState(() => (sentNow ? Date.now() + WAIT_SECONDS * 1000 : 0))
  const [now, setNow] = useState(() => Date.now())
  // A API responde igual quando a fila barra o pedido (um por minuto): a mensagem não promete um e-mail novo, só
  // aponta para a caixa de entrada, onde o mais recente já está.
  const [sentAgain, setSentAgain] = useState(false)
  const left = Math.max(0, Math.ceil((waitUntil - now) / 1000))

  useEffect(() => {
    if (left === 0) return
    const timer = setInterval(() => setNow(Date.now()), 1000)
    return () => clearInterval(timer)
  }, [left])

  const send = () =>
    resend.mutate(email, {
      onSuccess: () => {
        setSentAgain(true)
        setWaitUntil(Date.now() + WAIT_SECONDS * 1000)
        setNow(Date.now())
      },
    })

  const error =
    resend.error instanceof ApiError ? resend.error.message : resend.error ? 'Não foi possível conectar. Tente novamente.' : null

  return (
    <div className="flex flex-col gap-2">
      <Button
        type="button"
        variant="outline"
        className="h-11 w-full rounded-xl text-base"
        disabled={left > 0 || resend.isPending || !email}
        onClick={send}
      >
        {resend.isPending ? 'Enviando…' : left > 0 ? `${label} em ${left} s` : label}
      </Button>
      <p aria-live="polite" className="text-center text-sm text-muted-foreground empty:hidden">
        {error ?? (sentAgain ? 'Pronto. Confira sua caixa de entrada.' : '')}
      </p>
    </div>
  )
}
