import type { ComponentProps } from 'react'
import { cn } from '@/lib/utils'

// <select> nativo com o visual do Input: no celular abre o seletor do sistema, mais rápido.
export function NativeSelect({ className, ...props }: ComponentProps<'select'>) {
  return (
    <select
      className={cn(
        'h-9 w-full min-w-0 rounded-md border border-input bg-transparent px-3 py-1 text-base shadow-xs outline-none md:text-sm',
        'focus-visible:border-ring focus-visible:ring-[3px] focus-visible:ring-ring/50',
        'aria-invalid:border-destructive aria-invalid:ring-destructive/20 disabled:opacity-50',
        className,
      )}
      {...props}
    />
  )
}
