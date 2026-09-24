import type { ReactNode } from 'react'
import { Sheet, SheetContent } from '@/components/ui/sheet'

// Painel que sobe de baixo (lançamento, fatura, conta). Abre com o foco no próprio painel, não
// no primeiro botão, para não desenhar o anel de foco em "Editar" ao abrir.
export function BottomSheet({ open, onClose, children }: { open: boolean; onClose: () => void; children: ReactNode }) {
  return (
    <Sheet open={open} onOpenChange={(next) => !next && onClose()}>
      <SheetContent
        side="bottom"
        showCloseButton={false}
        aria-describedby={undefined}
        onOpenAutoFocus={(event) => {
          event.preventDefault()
          ;(event.currentTarget as HTMLElement).focus()
        }}
        className="mx-auto max-h-[92dvh] w-full max-w-lg gap-0 rounded-t-[1.75rem] border-x bg-background p-0 outline-none sm:bottom-4 sm:rounded-[1.75rem] sm:border-b"
      >
        <span aria-hidden className="mx-auto mt-2.5 mb-1 h-1 w-10 shrink-0 rounded-full bg-border" />
        {children}
      </SheetContent>
    </Sheet>
  )
}

// Rodapé fixo do painel, com a ação principal.
export function SheetFooterBar({ children, className = '' }: { children: ReactNode; className?: string }) {
  return (
    <footer
      className={`shrink-0 border-t border-border/60 bg-background/90 px-4 pt-3 pb-[max(1rem,env(safe-area-inset-bottom))] backdrop-blur-xl ${className}`}
    >
      {children}
    </footer>
  )
}
