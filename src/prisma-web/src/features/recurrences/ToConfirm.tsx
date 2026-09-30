import { Bell } from 'lucide-react'
import { lazy, Suspense, useCallback, useEffect, useRef, useState, type ReactNode } from 'react'
import { bellLabel, bellRings } from './autoDebit'
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

// A última contagem que o sino mostrou, enquanto a página estiver aberta. Fica fora do componente porque o
// "Novo lançamento" não tem cabeçalho: o sino sai da tela e volta, e sem isso o débito criado ali voltaria
// como se fosse a primeira vez, sem balançar. Recarregar a página zera (e aí não balança, de propósito).
let lastSeenCount: number | undefined

// Com débito a conferir, o número ganha o anel do espectro, o mesmo do avatar ao lado: cor só quando há o que
// ver, em tinta para ler bem. Quando a contagem cresce, o sino balança uma vez.
export function ToConfirmBell() {
  const data = useToConfirm().data
  const count = data?.length ?? 0
  const open = useOpenToConfirm()
  // Balançar é mexer no elemento (a animação é do CSS, que respeita quem desliga movimento): tira e põe a
  // classe, com uma leitura de layout no meio para a animação recomeçar.
  const bell = useRef<SVGSVGElement>(null)
  useEffect(() => {
    if (data === undefined) return
    const element = bell.current
    if (bellRings(lastSeenCount, count) && element) {
      element.classList.remove('bell-ring')
      void element.getBoundingClientRect()
      element.classList.add('bell-ring')
    }
    lastSeenCount = count
  }, [data, count])
  return (
    <button
      type="button"
      aria-label={bellLabel(count)}
      onClick={open}
      className="relative flex size-9 items-center justify-center rounded-full text-foreground transition-colors outline-none hover:bg-muted/70 focus-visible:ring-2 focus-visible:ring-ring active:bg-muted"
    >
      <Bell ref={bell} className="size-5" aria-hidden />
      {count > 0 && (
        <span
          aria-hidden
          className="absolute -top-1 -right-1 rounded-full p-[1.5px] shadow-[0_0_0_2px_var(--background)]"
          style={{ background: 'var(--spectrum-conic)' }}
        >
          <span className="flex h-4 min-w-4 items-center justify-center rounded-full bg-foreground px-1 text-[10px] leading-none font-semibold text-background tabular-nums">
            {count > 9 ? '9+' : count}
          </span>
        </span>
      )}
    </button>
  )
}
