import type { CSSProperties, ReactNode } from 'react'
import { cn } from '@/lib/utils'

// Peças da página de apresentação (docs/landing.md). Estilos em landing.css.

export const signupPath = '/cadastro'

export function Cta({ className }: { className?: string }) {
  return (
    <a href={signupPath} className={cn('cta', className)}>
      Criar minha conta
    </a>
  )
}

export function CtaNote({ className }: { className?: string }) {
  return <p className={cn('text-sm text-muted-foreground', className)}>Sem senha do banco. Leva menos de um minuto.</p>
}

export function SectionTitle({ children, className }: { children: ReactNode; className?: string }) {
  return <h2 className={cn('section-title', className)}>{children}</h2>
}

// Rótulo da seção com a faixa do espectro que a representa.
export function Eyebrow({ band, children }: { band: string; children: ReactNode }) {
  return (
    <p className="eyebrow" style={{ '--band': band } as CSSProperties}>
      {children}
    </p>
  )
}
