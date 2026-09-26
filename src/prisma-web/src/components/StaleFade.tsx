import type { ReactNode } from 'react'
import { cn } from '@/lib/utils'

// Esmaece o que ainda é do mês anterior enquanto o novo carrega (placeholderData). O atraso evita
// piscar quando a resposta vem rápido; a volta é imediata.
export function StaleFade({ stale, className, children }: { stale: boolean; className?: string; children: ReactNode }) {
  return (
    <div
      aria-busy={stale || undefined}
      className={cn(
        'transition-opacity',
        stale ? 'opacity-55 delay-150 duration-200 ease-out' : 'duration-150 ease-out',
        className,
      )}
    >
      {children}
    </div>
  )
}
