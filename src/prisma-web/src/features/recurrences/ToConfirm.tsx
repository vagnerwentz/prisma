import { Bell } from 'lucide-react'
import { lazy, Suspense, useCallback, useState, type ReactNode } from 'react'
import { bellLabel } from './autoDebit'
import { useToConfirm } from './queries'
import { OpenToConfirmContext, useOpenToConfirm } from './toConfirmContext'

// O sino (docs/fase-2.md, 2.15, D4): os débitos automáticos que saíram com o valor médio. Não é uma caixa de
// avisos: cada item sai quando é conferido ou excluído, e não há "marcar todas como lidas", que deixaria a
// estimativa passar por valor real.

const ToConfirmSheet = lazy(() => import('./ToConfirmPanel'))

// O painel só carrega no primeiro toque; depois fica montado, para fechar com a animação.
export function ToConfirmProvider({ children }: { children: ReactNode }) {
  const [open, setOpen] = useState(false)
  const [loaded, setLoaded] = useState(false)
  const show = useCallback(() => {
    setLoaded(true)
    setOpen(true)
  }, [])
  return (
    <OpenToConfirmContext.Provider value={show}>
      {children}
      {loaded && (
        <Suspense>
          <ToConfirmSheet open={open} onClose={() => setOpen(false)} />
        </Suspense>
      )}
    </OpenToConfirmContext.Provider>
  )
}

export function ToConfirmBell() {
  const count = useToConfirm().data?.length ?? 0
  const open = useOpenToConfirm()
  return (
    <button
      type="button"
      aria-label={bellLabel(count)}
      onClick={open}
      className="relative flex size-9 items-center justify-center rounded-full text-foreground transition-colors outline-none hover:bg-muted/70 focus-visible:ring-2 focus-visible:ring-ring active:bg-muted"
    >
      <Bell className="size-5" aria-hidden />
      {count > 0 && (
        <span
          aria-hidden
          className="absolute -top-0.5 -right-0.5 flex h-4 min-w-4 items-center justify-center rounded-full bg-foreground px-1 text-[10px] leading-none font-semibold text-background tabular-nums ring-2 ring-background"
        >
          {count > 9 ? '9+' : count}
        </span>
      )}
    </button>
  )
}
