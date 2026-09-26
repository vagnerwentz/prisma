import { Toaster as Sonner } from 'sonner'
import { useTheme } from '@/lib/theme'

// Avisos curtos ("Lançamento salvo"), no topo para não disputar espaço com a navegação.
export function Toaster() {
  const { theme } = useTheme()
  return (
    <Sonner
      position="top-center"
      theme={theme}
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
