import { cn } from '@/lib/utils'

// Um feixe de luz entra no prisma e sai decomposto no espectro. O feixe termina na face do prisma, a luz atravessa o
// vidro e sai num leque de faixas coladas, do violeta ao vermelho. A cor do prisma e do feixe acompanha o texto
// (currentColor). Desenho para qualquer tamanho, de 16 px para cima: sem brilho nem degradê, que viram borrão no
// pequeno (a versão grande, com brilho, fica na abertura da página de apresentação, Hero.tsx).
const prism = 'M22.4 9.88Q21 7 19.6 9.88L6.4 37.12Q5 40 8.2 40L33.8 40Q37 40 35.6 37.12Z'

// Faixas do trecho de saída na face direita até a borda; cada uma avança um pouco sobre a seguinte, sem fresta.
const bands = [
  { color: '#8b5cf6', points: '27.72 20.86 28.34 22.14 47 16.56 47 12.5' },
  { color: '#3b82f6', points: '28.31 22.07 28.93 23.35 47 20.4 47 16.33' },
  { color: '#22d3ee', points: '28.89 23.28 29.52 24.56 47 24.23 47 20.17' },
  { color: '#84cc16', points: '29.48 24.49 30.1 25.77 47 28.06 47 24' },
  { color: '#f59e0b', points: '30.07 25.7 30.69 26.98 47 31.9 47 27.83' },
  { color: '#f43f5e', points: '30.65 26.91 31.24 28.12 47 35.5 47 31.67' },
]

export function PrismLogo({ className, animated = false }: { className?: string; animated?: boolean }) {
  return (
    <svg viewBox="0 0 48 48" fill="none" aria-hidden className={cn('size-8', className)}>
      <line x1="1" y1="31.68" x2="12.04" y2="25.48" stroke="currentColor" strokeWidth="2.5" />
      <polygon points="12.04 25.48 27.72 20.86 31.24 28.12" fill="#a78bfa" fillOpacity="0.45" />
      {bands.map((band, i) => (
        <polygon
          key={band.color}
          points={band.points}
          fill={band.color}
          className={animated ? 'animate-in fade-in slide-in-from-left-2 fill-mode-both' : undefined}
          style={animated ? { animationDelay: `${120 + i * 60}ms`, animationDuration: '500ms' } : undefined}
        />
      ))}
      <path d={prism} fill="currentColor" fillOpacity="0.1" stroke="currentColor" strokeWidth="2.5" />
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
