import { useSyncExternalStore } from 'react'
import { flushSync } from 'react-dom'

// Tema (CLAUDE.md, 7.1): segue o sistema até a pessoa escolher claro ou escuro. A escolha fica no
// navegador (preferência de quem vê, não dado financeiro). O CSS lê só o data-theme do <html>, que o
// script do index.html já define antes da primeira pintura, para não piscar o tema errado.

export type ThemeChoice = 'system' | 'light' | 'dark'
export type Theme = 'light' | 'dark'

export const themeStorageKey = 'prisma.theme'

// Cor da barra do navegador no celular, a mesma do fundo de cada tema.
const browserBarColor: Record<Theme, string> = { light: '#faf8f3', dark: '#15121f' }

export function parseChoice(raw: string | null): ThemeChoice {
  return raw === 'light' || raw === 'dark' ? raw : 'system'
}

export function resolveTheme(choice: ThemeChoice, systemDark: boolean): Theme {
  if (choice !== 'system') return choice
  return systemDark ? 'dark' : 'light'
}

// O botão inverte o que está na tela; voltar a seguir o sistema fica no menu da conta.
export function toggledChoice(current: Theme): ThemeChoice {
  return current === 'dark' ? 'light' : 'dark'
}

// Raio do círculo que revela o tema novo: do ponto de origem até o canto mais distante da tela.
export function revealRadius(x: number, y: number, width: number, height: number): number {
  return Math.hypot(Math.max(x, width - x), Math.max(y, height - y))
}

// --- Estado compartilhado (fora do React: o <html> e o armazenamento são a fonte) ---

const systemQuery = typeof window !== 'undefined' ? window.matchMedia?.('(prefers-color-scheme: dark)') : undefined
const listeners = new Set<() => void>()

function readChoice(): ThemeChoice {
  try {
    return parseChoice(localStorage.getItem(themeStorageKey))
  } catch {
    return 'system'
  }
}

let choice = readChoice()

function current(): Theme {
  return resolveTheme(choice, systemQuery?.matches ?? false)
}

function apply() {
  const theme = current()
  document.documentElement.dataset.theme = theme
  document.querySelector('meta[name="theme-color"]')?.setAttribute('content', browserBarColor[theme])
}

function notify() {
  apply()
  listeners.forEach((listener) => listener())
}

systemQuery?.addEventListener('change', () => {
  if (choice === 'system') notify()
})

function setChoice(next: ThemeChoice) {
  choice = next
  try {
    if (next === 'system') localStorage.removeItem(themeStorageKey)
    else localStorage.setItem(themeStorageKey, next)
  } catch {
    // Sem armazenamento (janela privada), a escolha vale até fechar a aba.
  }
  notify()
}

function subscribe(listener: () => void) {
  listeners.add(listener)
  return () => listeners.delete(listener)
}

const reducedMotion = () => window.matchMedia?.('(prefers-reduced-motion: reduce)').matches ?? false

type ViewTransitionDocument = Document & {
  startViewTransition?: (update: () => void) => { ready: Promise<void> }
}

// Troca o tema com o tema novo se abrindo num círculo a partir de `origin` (o botão tocado). Sem View
// Transitions, ou com menos movimento, a troca é imediata.
export function switchTheme(next: ThemeChoice, origin?: { x: number; y: number }) {
  const doc = document as ViewTransitionDocument
  const theme = resolveTheme(next, systemQuery?.matches ?? false)
  if (!doc.startViewTransition || reducedMotion() || theme === current()) {
    setChoice(next)
    return
  }

  const { x, y } = origin ?? { x: window.innerWidth, y: 0 }
  const radius = revealRadius(x, y, window.innerWidth, window.innerHeight)
  const transition = doc.startViewTransition(() => flushSync(() => setChoice(next)))
  transition.ready
    .then(() =>
      document.documentElement.animate(
        { clipPath: [`circle(0px at ${x}px ${y}px)`, `circle(${radius}px at ${x}px ${y}px)`] },
        { duration: 560, easing: 'cubic-bezier(0.23, 1, 0.32, 1)', pseudoElement: '::view-transition-new(root)' },
      ),
    )
    .catch(() => {})
}

// Centro de um elemento, para a revelação partir dele.
export function centerOf(element: Element): { x: number; y: number } {
  const rect = element.getBoundingClientRect()
  return { x: rect.left + rect.width / 2, y: rect.top + rect.height / 2 }
}

export function useTheme(): { choice: ThemeChoice; theme: Theme } {
  const snapshot = useSyncExternalStore(subscribe, () => `${choice}:${current()}`)
  const [savedChoice, theme] = snapshot.split(':') as [ThemeChoice, Theme]
  return { choice: savedChoice, theme }
}
