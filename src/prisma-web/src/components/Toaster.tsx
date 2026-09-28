import { Toaster as Sonner } from 'sonner'
import { useTheme } from '@/lib/theme'

// Avisos curtos ("Lançamento salvo"), no topo para não disputar espaço com a navegação. Com um painel
// aberto, o Radix desliga o toque no resto da página: o aviso religa o dele, para o "Desfazer" funcionar
// no primeiro toque (o BottomSheet não se fecha com toque no aviso).
export function Toaster() {
  const { theme } = useTheme()
  return (
    <Sonner
      position="top-center"
      theme={theme}
      toastOptions={{
        classNames: {
          toast: '!pointer-events-auto !rounded-2xl !border-border !bg-popover !text-popover-foreground !shadow-lg',
          description: '!text-muted-foreground',
          actionButton: 'toast-action',
        },
      }}
    />
  )
}
