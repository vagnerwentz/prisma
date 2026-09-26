import { useEffect, useRef, useState, type ComponentProps, type CSSProperties } from 'react'
import { cn } from '@/lib/utils'

// Campo de senha com "mostrar senha" (login e cadastro). Mostrar é o momento da marca: um feixe de
// luz branca que se decompõe no espectro atravessa o texto, as letras surgem acesas por onde ele passa
// e o campo ganha um halo (index.css, .password-*). O olho fechado abre com a pupila no espectro.
// Esconder é imediato, sem cerimônia.
export function PasswordField({ className, ref, id, ...props }: Omit<ComponentProps<'input'>, 'type'>) {
  const input = useRef<HTMLInputElement | null>(null)
  const [shown, setShown] = useState(false)
  const [reveals, setReveals] = useState(0)
  // Fração do campo que o texto ocupa: a luz percorre só o texto, não o espaço vazio à direita.
  const [span, setSpan] = useState(1)

  // Ao enviar, a senha volta a ficar oculta antes de qualquer outro tratamento: não fica exposta na
  // troca de tela e o gerenciador de senhas continua vendo um campo de senha.
  useEffect(() => {
    const form = input.current?.form
    if (!form) return
    const hide = () => {
      if (input.current) input.current.type = 'password'
      setShown(false)
    }
    form.addEventListener('submit', hide, { capture: true })
    return () => form.removeEventListener('submit', hide, { capture: true })
  }, [])

  const toggle = () => {
    const el = input.current
    const [start, end] = [el?.selectionStart ?? null, el?.selectionEnd ?? null]
    if (!shown && el) {
      setSpan(textShare(el))
      setReveals((n) => n + 1)
    }
    setShown(!shown)
    // Trocar o tipo do campo leva o cursor para o fim em alguns navegadores: devolve ao lugar.
    requestAnimationFrame(() => {
      if (el && document.activeElement === el && start !== null && end !== null) el.setSelectionRange(start, end)
    })
  }

  return (
    <div
      style={{ '--reveal-span': span } as CSSProperties}
      className={cn(
        shown && reveals > 0 && 'password-lit',
        'relative flex items-center overflow-hidden rounded-xl border border-input transition-colors focus-within:border-ring focus-within:ring-3 focus-within:ring-ring/50 has-aria-invalid:border-destructive has-aria-invalid:ring-3 has-aria-invalid:ring-destructive/20 dark:bg-input/30',
        className,
      )}
    >
      {shown && reveals > 0 && <span key={reveals} aria-hidden className="password-sweep" />}
      <input
        {...props}
        id={id}
        ref={(el) => {
          input.current = el
          if (typeof ref === 'function') ref(el)
          else if (ref) ref.current = el
        }}
        type={shown ? 'text' : 'password'}
        autoCapitalize="none"
        autoCorrect="off"
        spellCheck={false}
        className={cn(
          'h-full min-w-0 flex-1 bg-transparent py-1 pr-12 pl-3 text-base outline-none placeholder:text-muted-foreground',
          shown && reveals > 0 && 'password-reveal',
        )}
      />
      <button
        type="button"
        aria-controls={id}
        aria-pressed={shown}
        aria-label={shown ? 'Ocultar senha' : 'Mostrar senha'}
        // Não tira o foco do campo: no celular o teclado continua aberto.
        onPointerDown={(event) => event.preventDefault()}
        onClick={toggle}
        className="absolute right-1 flex size-9 items-center justify-center rounded-lg text-muted-foreground transition-colors outline-none hover:text-foreground focus-visible:ring-2 focus-visible:ring-ring"
      >
        <svg viewBox="0 0 24 24" className="password-eye size-5" data-open={shown} aria-hidden>
          <defs>
            <linearGradient id="password-eye-spectrum" x1="0" y1="0" x2="1" y2="1">
              <stop offset="0" stopColor="#8b5cf6" />
              <stop offset="0.5" stopColor="#3b82f6" />
              <stop offset="1" stopColor="#22d3ee" />
            </linearGradient>
          </defs>
          <path
            className="password-eye-lid"
            d="M2.5 12s3.5-6.5 9.5-6.5S21.5 12 21.5 12s-3.5 6.5-9.5 6.5S2.5 12 2.5 12z"
            fill="none"
            stroke="currentColor"
            strokeWidth="1.8"
            strokeLinejoin="round"
          />
          <g className="password-eye-pupil">
            <circle cx="12" cy="12" r="3.2" fill="none" stroke="url(#password-eye-spectrum)" strokeWidth="2.2" />
            <circle cx="12" cy="12" r="1.1" fill="currentColor" />
          </g>
          <g className="password-eye-lashes" stroke="currentColor" strokeWidth="1.8" strokeLinecap="round">
            <path d="M6.5 13.6l-1.2 1.9M12 14.6v2.2M17.5 13.6l1.2 1.9" />
          </g>
        </svg>
      </button>
    </div>
  )
}

const measure = typeof document !== 'undefined' ? document.createElement('canvas').getContext('2d') : null

// Quanto do campo o texto ocupa (com o recuo à esquerda e uma folga), de 0,15 a 1.
function textShare(input: HTMLInputElement): number {
  const style = getComputedStyle(input)
  if (!measure || input.clientWidth === 0) return 1
  measure.font = `${style.fontWeight} ${style.fontSize} ${style.fontFamily}`
  const text = measure.measureText(input.value).width + parseFloat(style.paddingLeft) + 12
  return Math.min(1, Math.max(0.15, text / input.clientWidth))
}
