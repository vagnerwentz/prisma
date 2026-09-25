import { useEffect, useRef, useState, type CSSProperties } from 'react'
import { cn } from '@/lib/utils'

// Cena das telas de erro: a luz entra firme no prisma, mas o espectro sai desalinhado. Os raios
// oscilam, seguem de leve o ponteiro e, com `mending`, voltam ao leque do logo: a luz se recompõe.
// Animações em CSS (.broken-prism, index.css); os fótons, em SMIL, só sem "reduzir movimento".

const colors = ['#8b5cf6', '#3b82f6', '#22d3ee', '#84cc16', '#f59e0b', '#f43f5e']
// Ângulos de cada raio: espalhado (erro), à deriva (404, versão nova) e recomposto (o leque do logo).
const scattered = [-41, -19, -6, 12, 29, 53]
const drifting = [-25, -13, -4, 6, 17, 31]
const mended = [-7.5, -4.5, -1.5, 1.5, 4.5, 7.5]

// Ponto de saída do espectro, na face direita do prisma, e comprimento dos raios.
const exit = { x: 236, y: 118 }
const length = 175

export function BrokenPrism({
  cracked = false,
  mending = false,
  className,
}: {
  cracked?: boolean
  mending?: boolean
  className?: string
}) {
  const ref = useRef<HTMLDivElement>(null)
  const calm = useReducedMotion()
  const angles = cracked ? scattered : drifting

  // O ponteiro inclina o leque e desloca o prisma alguns pixels: parece vidro de verdade.
  const follow = (event: React.PointerEvent) => {
    const box = ref.current?.getBoundingClientRect()
    if (!box || calm) return
    ref.current!.style.setProperty('--px', (((event.clientX - box.left) / box.width) * 2 - 1).toFixed(3))
    ref.current!.style.setProperty('--py', (((event.clientY - box.top) / box.height) * 2 - 1).toFixed(3))
  }

  return (
    <div
      ref={ref}
      className={cn('broken-prism relative', className)}
      data-mending={mending}
      onPointerMove={follow}
      onPointerLeave={() => {
        ref.current?.style.setProperty('--px', '0')
        ref.current?.style.setProperty('--py', '0')
      }}
    >
      {/* Halo frio atrás do vidro. */}
      <span
        aria-hidden
        className="bp-halo pointer-events-none absolute top-1/2 left-1/2 size-[62%] -translate-x-1/2 -translate-y-1/2 rounded-full blur-3xl"
        style={{ background: 'var(--halo)' }}
      />
      <svg viewBox="0 0 400 250" fill="none" aria-hidden className="relative w-full overflow-visible">
        <defs>
          <filter id="bp-glow" x="-20%" y="-50%" width="140%" height="200%">
            <feGaussianBlur stdDeviation="4" />
          </filter>
          {/* O feixe surge do nada: no computador, a cena não começa num corte seco. */}
          <linearGradient id="bp-beam" x1="-20" y1="0" x2="80" y2="0" gradientUnits="userSpaceOnUse">
            <stop offset="0" stopColor="currentColor" stopOpacity="0" />
            <stop offset="1" stopColor="currentColor" />
          </linearGradient>
          <linearGradient id="bp-glass" x1="200" y1="40" x2="200" y2="190" gradientUnits="userSpaceOnUse">
            <stop offset="0" stopColor="currentColor" stopOpacity="0.16" />
            <stop offset="1" stopColor="currentColor" stopOpacity="0.03" />
          </linearGradient>
          {colors.map((color, i) => (
            <linearGradient
              key={color}
              id={`bp-ray-${i}`}
              x1={exit.x}
              y1="0"
              x2={exit.x + length}
              y2="0"
              gradientUnits="userSpaceOnUse"
            >
              <stop offset="0" stopColor={color} />
              <stop offset="0.7" stopColor={color} stopOpacity="0.8" />
              <stop offset="1" stopColor={color} stopOpacity="0" />
            </linearGradient>
          ))}
        </defs>

        {/* Feixe de luz branca: firme, é o que entra. */}
        <g className="bp-beam">
          <line x1="-20" y1="152" x2="160" y2="125" stroke="url(#bp-beam)" strokeWidth="9" opacity="0.18" filter="url(#bp-glow)" />
          <line x1="-20" y1="152" x2="160" y2="125" stroke="url(#bp-beam)" strokeWidth="3" strokeLinecap="round" />
          <line x1="160" y1="125" x2={exit.x} y2={exit.y} stroke="currentColor" strokeWidth="2" opacity="0.35" />
        </g>

        {/* Espectro: cada raio gira em torno do ponto de saída. */}
        <g className="bp-fan">
          {colors.map((color, i) => (
            <g key={color} className="bp-ray" style={{ '--a': `${angles[i]}deg`, '--m': `${mended[i]}deg` } as CSSProperties}>
              <g
                className={cn('bp-wobble', cracked && (i === 1 || i === 4) && 'bp-flicker')}
                style={{ animationDuration: `${2.8 + ((i * 0.7) % 1.9)}s`, animationDelay: `${-i * 0.45}s` }}
              >
                <line
                  x1={exit.x}
                  y1={exit.y}
                  x2={exit.x + length}
                  y2={exit.y}
                  stroke={`url(#bp-ray-${i})`}
                  strokeWidth="9"
                  opacity="0.35"
                  filter="url(#bp-glow)"
                />
                <line
                  x1={exit.x}
                  y1={exit.y}
                  x2={exit.x + length}
                  y2={exit.y}
                  stroke={`url(#bp-ray-${i})`}
                  strokeWidth="3"
                  strokeLinecap="round"
                />
                {!calm && (
                  <circle r="2.6" fill={color} opacity="0">
                    <animateMotion
                      path={`M${exit.x},${exit.y} L${exit.x + length - 20},${exit.y}`}
                      dur={`${2.2 + i * 0.35}s`}
                      begin={`${i * 0.4}s`}
                      repeatCount="indefinite"
                    />
                    <animate
                      attributeName="opacity"
                      values="0;1;1;0"
                      keyTimes="0;0.15;0.7;1"
                      dur={`${2.2 + i * 0.35}s`}
                      begin={`${i * 0.4}s`}
                      repeatCount="indefinite"
                    />
                  </circle>
                )}
              </g>
            </g>
          ))}
        </g>

        {/* O prisma de vidro, por cima dos raios; com fissura quando algo quebrou. */}
        <g className="bp-prism">
          <path
            d="M200 40 270 190H130L200 40Z"
            fill="url(#bp-glass)"
            stroke="currentColor"
            strokeWidth="2.5"
            strokeLinejoin="round"
          />
          <path d="M200 58 257 180" stroke="currentColor" strokeWidth="1" opacity="0.25" strokeLinecap="round" />
          {cracked && (
            <g className="bp-crack" stroke="currentColor" strokeLinecap="round" strokeLinejoin="round">
              <path d="M186 71 195 92 186 106 199 124 191 141 203 163" strokeWidth="1.6" opacity="0.7" />
              <path d="M195 92 207 97M199 124 188 130M191 141 181 146" strokeWidth="1.1" opacity="0.45" />
            </g>
          )}
        </g>
      </svg>
    </div>
  )
}

function useReducedMotion(): boolean {
  const [reduced, setReduced] = useState(() => window.matchMedia?.('(prefers-reduced-motion: reduce)').matches ?? false)
  useEffect(() => {
    const query = window.matchMedia?.('(prefers-reduced-motion: reduce)')
    if (!query) return
    const update = () => setReduced(query.matches)
    query.addEventListener('change', update)
    return () => query.removeEventListener('change', update)
  }, [])
  return reduced
}
