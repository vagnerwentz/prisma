import { useEffect, useState } from 'react'
import { useLocation, useNavigate } from 'react-router'

// Os links dos e-mails da conta (docs/email.md, 5.5): /<tela>#u=<usuário>&t=<token>. O token vem depois do "#", que o
// navegador não manda a servidor nenhum, e sai da barra de endereço assim que a tela o lê (não fica no histórico).
export function useEmailLink() {
  const location = useLocation()
  const navigate = useNavigate()
  const [link] = useState(() => {
    const params = new URLSearchParams(location.hash.replace(/^#/, ''))
    return { userId: params.get('u') ?? '', token: params.get('t') ?? '' }
  })

  useEffect(() => {
    if (location.hash) navigate(location.pathname, { replace: true })
  }, [location.hash, location.pathname, navigate])

  return { ...link, complete: !!link.userId && !!link.token }
}
