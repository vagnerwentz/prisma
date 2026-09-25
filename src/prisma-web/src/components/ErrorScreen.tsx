import { ArrowLeft, LogIn, RotateCw, Sparkles } from 'lucide-react'
import { useRef, useState, type ReactNode } from 'react'
import { useNavigate } from 'react-router'
import { BrokenPrism } from '@/components/brand/BrokenPrism'
import { Wordmark } from '@/components/brand/PrismLogo'
import { Button } from '@/components/ui/button'
import type { CrashKind } from '@/lib/crash'

export type ErrorKind = CrashKind | 'not-found'

type Copy = { eyebrow: string; title: string; body: string; action: string; icon: ReactNode }

const copy: Record<ErrorKind, Copy> = {
  crash: {
    eyebrow: 'Algo quebrou',
    title: 'A luz saiu do caminho.',
    body: 'Esta tela encontrou um erro e não conseguiu se recompor sozinha. Seus lançamentos estão a salvo: nada foi perdido.',
    action: 'Recarregar',
    icon: <RotateCw />,
  },
  update: {
    eyebrow: 'Versão nova',
    title: 'O Prisma ganhou um brilho novo.',
    body: 'Publicamos uma atualização enquanto o app estava aberto. Recarregue para continuar; nada do que você lançou se perdeu.',
    action: 'Atualizar agora',
    icon: <Sparkles />,
  },
  'not-found': {
    eyebrow: 'Erro 404',
    title: 'Esta página se dispersou.',
    body: 'O endereço não leva a nenhuma tela do Prisma. Talvez o link esteja incompleto, ou a página tenha mudado de lugar.',
    action: 'Voltar ao Resumo',
    icon: <ArrowLeft />,
  },
}

// Sem sessão, o Resumo só levaria ao login: a página inexistente oferece entrar direto.
const signedOutAction = { action: 'Entrar no Prisma', icon: <LogIn /> }

// Quanto a luz leva para se recompor antes de recarregar ou sair da tela (a transição dos raios).
const mendingMs = 750

// Tela de erro: a cena do prisma desalinhado e uma saída clara. Passar o ponteiro (ou o foco) na
// ação principal recompõe a luz; tocar nela recompõe e então recarrega ou volta ao Resumo.
export function ErrorScreen({ kind, error, signedOut = false }: { kind: ErrorKind; error?: unknown; signedOut?: boolean }) {
  const navigate = useNavigate()
  const [hovering, setHovering] = useState(false)
  const [leaving, setLeaving] = useState(false)
  const timer = useRef<number>(undefined)
  const text = kind === 'not-found' && signedOut ? { ...copy[kind], ...signedOutAction } : copy[kind]

  const run = () => {
    if (kind === 'not-found') navigate(signedOut ? '/entrar' : '/', { replace: true })
    else window.location.reload()
  }

  const act = () => {
    if (leaving) return
    setLeaving(true)
    const calm = window.matchMedia?.('(prefers-reduced-motion: reduce)').matches
    timer.current = window.setTimeout(run, calm ? 0 : mendingMs)
  }

  return (
    <main className="relative flex min-h-dvh flex-col items-center overflow-hidden px-4 pt-6 pb-10">
      <Wordmark className="text-foreground/80" />

      <div className="flex w-full max-w-lg flex-1 flex-col items-center justify-center gap-8 py-8 text-center">
        <BrokenPrism
          cracked={kind === 'crash'}
          mending={hovering || leaving}
          className="w-full max-w-[26rem] animate-in duration-700 fade-in fill-mode-both"
        />

        <div className="flex flex-col items-center gap-4">
          <span
            className="animate-in text-xs font-semibold tracking-[0.2em] text-foreground/60 uppercase duration-700 fade-in slide-in-from-bottom-2 fill-mode-both"
            style={{ animationDelay: '120ms' }}
          >
            {text.eyebrow}
          </span>
          <h1
            className="animate-in font-display text-[clamp(2.4rem,10vw,3.5rem)] leading-[1.05] text-balance duration-700 fade-in slide-in-from-bottom-3 fill-mode-both"
            style={{ animationDelay: '200ms' }}
          >
            {text.title}
          </h1>
          <p
            className="max-w-sm animate-in text-[0.95rem] leading-relaxed text-pretty text-foreground/75 duration-700 fade-in slide-in-from-bottom-3 fill-mode-both"
            style={{ animationDelay: '300ms' }}
          >
            {text.body}
          </p>
        </div>

        <div
          className="flex w-full animate-in flex-col items-center gap-2 duration-700 fade-in slide-in-from-bottom-3 fill-mode-both sm:w-auto sm:flex-row sm:gap-3"
          style={{ animationDelay: '420ms' }}
        >
          <Button
            onClick={act}
            onPointerEnter={() => setHovering(true)}
            onPointerLeave={() => setHovering(false)}
            onFocus={() => setHovering(true)}
            onBlur={() => setHovering(false)}
            disabled={leaving}
            className="h-12 w-full rounded-full px-7 text-[0.95rem] sm:w-auto [&_svg:not([class*='size-'])]:size-[1.05rem]"
          >
            <span className={leaving && kind !== 'not-found' ? 'animate-spin' : undefined}>{text.icon}</span>
            {text.action}
          </Button>
          {kind === 'crash' && (
            <Button variant="ghost" asChild className="h-12 w-full rounded-full px-6 text-[0.95rem] sm:w-auto">
              <a href="/">Voltar ao Resumo</a>
            </Button>
          )}
        </div>

        {import.meta.env.DEV && error !== undefined && (
          <details className="w-full max-w-md rounded-2xl bg-muted/60 px-4 py-3 text-left text-xs text-muted-foreground">
            <summary className="cursor-pointer font-medium">Detalhes técnicos (só em desenvolvimento)</summary>
            <pre className="mt-2 overflow-x-auto whitespace-pre-wrap">
              {error instanceof Error ? (error.stack ?? error.message) : String(error)}
            </pre>
          </details>
        )}
      </div>
    </main>
  )
}
