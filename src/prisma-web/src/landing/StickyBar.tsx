import { useEffect, useState } from 'react'
import { PrismLogo } from '@/components/brand/PrismLogo'
import { signupPath } from './parts'

// A barra que acompanha a rolagem (docs/landing.md, D16): surge quando a abertura sai da tela, com o logo, "Entrar" e
// "Criar conta". Sem menu de tópicos: o botão fica a um toque, sem dar saídas a quem chegou para decidir.
export function StickyBar({ after }: { after: string }) {
  const [shown, setShown] = useState(false)

  useEffect(() => {
    const target = document.getElementById(after)
    if (!target) return
    // Aparece só quando a abertura ficou para cima; voltando ao topo, some.
    const observer = new IntersectionObserver(([entry]) => setShown(!entry.isIntersecting && entry.boundingClientRect.top < 0))
    observer.observe(target)
    return () => observer.disconnect()
  }, [after])

  return (
    <div className="sticky-bar" data-shown={shown} aria-hidden={!shown}>
      <div className="mx-auto flex h-14 max-w-6xl items-center justify-between gap-3 px-5 sm:px-8">
        <a href="/" className="flex items-center gap-2" tabIndex={shown ? 0 : -1} aria-label="Prisma Money, início">
          <PrismLogo className="size-6" />
          <span className="font-display text-xl leading-none">Prisma Money</span>
        </a>
        <div className="flex items-center gap-1">
          <a
            href="/entrar"
            tabIndex={shown ? 0 : -1}
            className="rounded-full px-3 py-2 text-sm font-medium text-muted-foreground transition-colors duration-150 ease-out hover:text-foreground"
          >
            Entrar
          </a>
          <a href={signupPath} tabIndex={shown ? 0 : -1} className="cta cta-small">
            Criar conta
          </a>
        </div>
      </div>
    </div>
  )
}
