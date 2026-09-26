import { useEffect, useId, useMemo, useRef, useState, type ReactNode } from 'react'
import { formatCents } from '@/lib/money'
import { cn } from '@/lib/utils'
import { recentSuggestions, suggest, type DescriptionSuggestion, type SuggestionMatch } from './suggestions'

// Campo Descrição com sugestões (docs/fase-2.md, 2.8 e 4): combobox da WAI-ARIA. O texto digitado
// nunca é completado nem trocado sozinho; nenhuma sugestão vem destacada sem a pessoa pedir.
export function DescriptionCombobox({
  value,
  onChange,
  onChoose,
  vocabulary,
  type,
  today,
  leading,
  tileFor,
  detailsOf,
}: {
  value: string
  onChange: (text: string) => void
  // Aplica a sugestão e devolve o que dizer ao leitor de tela (o que foi preenchido e o que ficou).
  onChoose: (suggestion: DescriptionSuggestion) => string
  vocabulary: DescriptionSuggestion[]
  type: string
  today: string
  leading: ReactNode
  tileFor: (suggestion: DescriptionSuggestion, size: 'sm' | 'md') => ReactNode
  detailsOf: (suggestion: DescriptionSuggestion) => string
}) {
  const listId = useId()
  const wrapper = useRef<HTMLDivElement>(null)
  const [open, setOpen] = useState(false)
  const [active, setActive] = useState(-1)
  const [announcement, setAnnouncement] = useState('')

  const empty = value.trim() === ''
  const items: SuggestionMatch[] = useMemo(
    () =>
      empty
        ? recentSuggestions(vocabulary, type, today).map((suggestion) => ({ suggestion, start: -1, length: 0 }))
        : suggest(vocabulary, value, type, today),
    [empty, vocabulary, value, type, today],
  )
  const expanded = open && items.length > 0

  // Quantas sugestões há: só quando a pessoa para de digitar, para não falar a cada letra.
  useEffect(() => {
    if (!open) return
    const timer = setTimeout(() => {
      const n = items.length
      if (empty) setAnnouncement(n ? `${n} recentes. Setas para navegar, Enter para escolher.` : '')
      else setAnnouncement(n ? `${n} ${n === 1 ? 'sugestão' : 'sugestões'}. Setas para navegar, Enter para escolher.` : '')
    }, 450)
    return () => clearTimeout(timer)
  }, [open, items.length, empty, value])

  const close = () => {
    setOpen(false)
    setActive(-1)
  }

  const choose = (index: number) => {
    const match = items[index]
    if (!match) return
    close()
    setAnnouncement(onChoose(match.suggestion))
  }

  // Seta com a lista fechada abre já destacando a primeira (ou a última, para cima), como no padrão.
  const move = (delta: number) => {
    const n = items.length
    if (!n) return
    if (!expanded) {
      setOpen(true)
      setActive(delta > 0 ? 0 : n - 1)
      return
    }
    setActive((current) => (current < 0 ? (delta > 0 ? 0 : n - 1) : (current + delta + n) % n))
  }

  // No celular, o teclado cobre a metade de baixo: ao abrir, o campo sobe para a lista caber.
  const reveal = () => {
    if (!window.matchMedia?.('(pointer: coarse)').matches) return
    const calm = window.matchMedia('(prefers-reduced-motion: reduce)').matches
    requestAnimationFrame(() => wrapper.current?.scrollIntoView({ block: 'start', behavior: calm ? 'auto' : 'smooth' }))
  }

  const optionId = (i: number) => `${listId}-${i}`

  return (
    <div ref={wrapper} className="relative scroll-mt-20">
      <label className="flex items-center gap-3 rounded-2xl border bg-card py-2 pr-3 pl-2 focus-within:ring-2 focus-within:ring-ring/50">
        {leading}
        <input
          value={value}
          placeholder="Ex.: Uber, iFood (opcional)"
          autoComplete="off"
          spellCheck={false}
          maxLength={200}
          role="combobox"
          aria-label="Descrição"
          aria-autocomplete="list"
          aria-expanded={expanded}
          aria-controls={listId}
          aria-activedescendant={expanded && active >= 0 ? optionId(active) : undefined}
          className="h-10 min-w-0 flex-1 bg-transparent text-base outline-none placeholder:text-muted-foreground"
          onFocus={() => {
            setOpen(true)
            reveal()
          }}
          onBlur={close}
          onChange={(event) => {
            onChange(event.target.value)
            setActive(-1)
            setOpen(true)
          }}
          onKeyDown={(event) => {
            if (event.key === 'ArrowDown' || event.key === 'ArrowUp') {
              event.preventDefault()
              move(event.key === 'ArrowDown' ? 1 : -1)
            } else if (event.key === 'Enter' && expanded) {
              // Com a lista aberta, Enter nunca lança: escolhe a destacada ou só fecha a lista.
              event.preventDefault()
              if (active >= 0) choose(active)
              else close()
            } else if (event.key === 'Escape' && expanded) {
              event.preventDefault()
              close()
              setAnnouncement('Sugestões fechadas. O texto ficou como você digitou.')
            } else if (event.key === 'Tab') close()
          }}
        />
      </label>

      {expanded && (
        <div className="absolute inset-x-0 top-[calc(100%+0.375rem)] z-30 animate-in rounded-2xl bg-popover p-1.5 shadow-lg ring-1 ring-border/60 duration-150 ease-out fade-in-0 slide-in-from-top-1">
          {empty && (
            <p aria-hidden className="px-2 pt-1 pb-1.5 text-xs text-muted-foreground">
              Recentes
            </p>
          )}
          <ul
            id={listId}
            role="listbox"
            aria-label={empty ? 'Descrições recentes' : 'Sugestões de descrição'}
            className={cn(empty ? 'flex flex-wrap gap-1.5 px-1 pb-1' : 'flex flex-col')}
          >
            {items.map((match, i) => (
              <Option
                key={`${match.suggestion.type}-${match.suggestion.description}`}
                id={optionId(i)}
                match={match}
                chip={empty}
                active={i === active}
                tile={tileFor(match.suggestion, empty ? 'sm' : 'md')}
                details={detailsOf(match.suggestion)}
                // pointerdown: escolhe antes do blur fechar a lista.
                onPick={() => choose(i)}
              />
            ))}
          </ul>
        </div>
      )}

      <p role="status" className="sr-only">
        {announcement}
      </p>
    </div>
  )
}

function Option({
  id,
  match,
  chip,
  active,
  tile,
  details,
  onPick,
}: {
  id: string
  match: SuggestionMatch
  chip: boolean
  active: boolean
  tile: ReactNode
  details: string
  onPick: () => void
}) {
  const { suggestion, start, length } = match
  const last = `última ${formatCents(suggestion.lastAmountCents)}`
  const text = suggestion.description

  return (
    <li
      id={id}
      role="option"
      aria-selected={active}
      aria-label={`${text}, ${details}, ${last}`}
      onPointerDown={(event) => {
        event.preventDefault()
        onPick()
      }}
      className={cn(
        'flex cursor-pointer items-center select-none',
        chip
          ? 'gap-2 rounded-full py-1 pr-3 pl-1 text-sm font-medium ring-1 ring-border hover:bg-muted/60'
          : 'gap-3 rounded-xl px-2 py-2 hover:bg-muted/50',
        active && 'bg-primary/8 ring-1 ring-primary/40 hover:bg-primary/8',
      )}
    >
      {tile}
      {chip ? (
        <span className="max-w-40 truncate">{text}</span>
      ) : (
        <>
          <span className="flex min-w-0 flex-1 flex-col">
            <span className="truncate font-medium">
              {start >= 0 ? (
                <>
                  {text.slice(0, start)}
                  <mark className="bg-transparent text-inherit underline decoration-[#8b5cf6] decoration-2 underline-offset-[3px]">
                    {text.slice(start, start + length)}
                  </mark>
                  {text.slice(start + length)}
                </>
              ) : (
                text
              )}
            </span>
            <span className="truncate text-sm text-muted-foreground">{details}</span>
          </span>
          <span className="shrink-0 text-right text-xs leading-tight text-muted-foreground tabular-nums">
            última
            <br />
            {formatCents(suggestion.lastAmountCents)}
          </span>
        </>
      )}
    </li>
  )
}
