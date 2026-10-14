import type { ReactNode } from 'react'
import { PrismLogo } from '@/components/brand/PrismLogo'

// O palco das telas de entrada (entrar, criar conta, confirmar e-mail): o halo frio ao fundo, o prisma e o nome.
export function AuthShell({ children }: { children: ReactNode }) {
  return (
    <main className="relative flex min-h-dvh flex-col items-center justify-center overflow-hidden px-6 py-10">
      {/* A luz que entra no prisma: um halo frio e suave ao fundo. */}
      <div
        aria-hidden
        className="pointer-events-none absolute -top-40 left-1/2 size-[36rem] -translate-x-1/2 rounded-full opacity-30 blur-3xl"
        style={{ background: 'var(--halo)' }}
      />
      <div className="relative flex w-full max-w-sm flex-col gap-8">
        <div className="flex flex-col items-center gap-3 text-center">
          <PrismLogo animated className="size-16" />
          <h1 className="font-display text-5xl leading-none">Prisma</h1>
          <p className="text-muted-foreground">Seu dinheiro, decomposto em cores.</p>
        </div>
        {children}
      </div>
    </main>
  )
}

// O cartão das telas de entrada, com título e texto de apoio.
export function AuthCard({ title, description, children }: { title: string; description?: ReactNode; children?: ReactNode }) {
  return (
    <div className="flex flex-col gap-4 rounded-3xl border bg-card/80 p-5 shadow-sm backdrop-blur-xl">
      <div className="flex flex-col gap-1">
        <h2 className="text-lg font-semibold">{title}</h2>
        {description && <p className="text-sm text-muted-foreground">{description}</p>}
      </div>
      {children}
    </div>
  )
}
