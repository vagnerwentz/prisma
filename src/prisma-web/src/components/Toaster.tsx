import { Toaster as Sonner } from 'sonner'

// Avisos curtos ("Lançamento salvo"), no topo para não disputar espaço com a navegação.
export function Toaster() {
  return (
    <Sonner
      position="top-center"
      theme="system"
      toastOptions={{
        classNames: {
          toast: '!rounded-2xl !border-border !bg-popover !text-popover-foreground !shadow-lg',
          description: '!text-muted-foreground',
          actionButton: 'toast-action',
        },
      }}
    />
  )
}
