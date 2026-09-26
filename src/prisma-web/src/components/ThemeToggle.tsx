import { centerOf, switchTheme, toggledChoice, useTheme } from '@/lib/theme'

// Uma faixa do espectro por raio: no claro, o sol é a luz já decomposta pelo prisma.
const rays = ['#8b5cf6', '#3b82f6', '#22d3ee', '#84cc16', '#f59e0b', '#f97316', '#f43f5e', '#ec4899']

// Troca claro ⇄ escuro. O sol recolhe os raios e vira lua; o tema novo se abre num círculo a partir
// do botão (switchTheme). Voltar a seguir o sistema fica no menu da conta.
export function ThemeToggle() {
  const { theme } = useTheme()
  const dark = theme === 'dark'

  return (
    <button
      type="button"
      aria-label={dark ? 'Usar tema claro' : 'Usar tema escuro'}
      onClick={(event) => switchTheme(toggledChoice(theme), centerOf(event.currentTarget))}
      className="flex size-9 items-center justify-center rounded-full text-foreground transition-colors outline-none hover:bg-muted/70 focus-visible:ring-2 focus-visible:ring-ring active:bg-muted"
    >
      <svg viewBox="0 0 24 24" className="theme-glyph size-5" data-mode={theme} aria-hidden>
        <mask id="theme-glyph-bite">
          <rect width="24" height="24" fill="white" />
          <circle className="theme-bite" cx="15" cy="9" r="4.4" fill="black" />
        </mask>
        <circle className="theme-core" cx="12" cy="12" r="4.6" fill="currentColor" mask="url(#theme-glyph-bite)" />
        {rays.map((color, i) => (
          <g key={color} transform={`rotate(${i * 45} 12 12)`}>
            <line
              className="theme-ray"
              x1="12"
              y1="1.8"
              x2="12"
              y2="4.2"
              stroke={color}
              strokeWidth="2.1"
              strokeLinecap="round"
              style={{ transitionDelay: `${i * 18}ms` }}
            />
          </g>
        ))}
      </svg>
    </button>
  )
}
