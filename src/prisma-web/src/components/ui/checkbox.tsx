import { Check } from 'lucide-react'
import type { ComponentProps } from 'react'
import { Checkbox as CheckboxPrimitive } from 'radix-ui'
import { cn } from '@/lib/utils'

// A caixa de seleção do Prisma (CLAUDE.md, 7.1): vazia, só o contorno; marcada, o espectro frio com o visto branco
// (.check-box e .check-mark, no index.css). Radix por baixo: teclado, leitor de tela e o estado em data-state.
export function Checkbox({ className, ...props }: ComponentProps<typeof CheckboxPrimitive.Root>) {
  return (
    <CheckboxPrimitive.Root
      data-slot="checkbox"
      className={cn(
        'check-box outline-none focus-visible:ring-2 focus-visible:ring-ring/50 disabled:cursor-not-allowed disabled:opacity-50',
        className,
      )}
      {...props}
    >
      <CheckMark />
    </CheckboxPrimitive.Root>
  )
}

// O visto, sempre montado: entra e sai pela classe, sem desmontar (a transição precisa do elemento).
export function CheckMark() {
  return (
    <CheckboxPrimitive.Indicator forceMount className="check-mark flex">
      <Check className="size-3.5" strokeWidth={3} aria-hidden />
    </CheckboxPrimitive.Indicator>
  )
}
