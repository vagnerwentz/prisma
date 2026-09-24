import { cn } from '@/lib/utils'

// Um feixe de luz entra no prisma e sai decomposto no espectro. A cor do prisma e do feixe
// acompanha o texto (currentColor); as faixas usam as cores do espectro da identidade.
const rays = ['#8b5cf6', '#3b82f6', '#22d3ee', '#84cc16', '#f59e0b', '#f43f5e']

export function PrismLogo({ className, animated = false }: { className?: string; animated?: boolean }) {
  return (
    <svg viewBox="0 0 48 48" fill="none" aria-hidden className={cn('size-8', className)}>
      <path d="M1 29.5 17 24" stroke="currentColor" strokeWidth="2.2" strokeLinecap="round" />
      {rays.map((color, i) => (
        <path
          key={color}
          d={`M31 23.5 47 ${16 + i * 3.4}`}
          stroke={color}
          strokeWidth="2.2"
          strokeLinecap="round"
          className={animated ? 'animate-in fade-in slide-in-from-left-2 fill-mode-both' : undefined}
          style={animated ? { animationDelay: `${120 + i * 60}ms`, animationDuration: '500ms' } : undefined}
        />
      ))}
      <path
        d="M24 6.5 40.5 40H7.5L24 6.5Z"
        fill="currentColor"
        fillOpacity="0.08"
        stroke="currentColor"
        strokeWidth="2.2"
        strokeLinejoin="round"
      />
    </svg>
  )
}

export function Wordmark({ className }: { className?: string }) {
  return (
    <span className={cn('flex items-center gap-2', className)}>
      <PrismLogo className="size-7" />
      <span className="font-display text-2xl leading-none">Prisma</span>
    </span>
  )
}
