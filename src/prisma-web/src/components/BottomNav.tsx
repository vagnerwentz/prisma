import { List, Plus, Wallet } from 'lucide-react'
import { Link, NavLink } from 'react-router'
import { cn } from '@/lib/utils'

const itemClass = ({ isActive }: { isActive: boolean }) =>
  cn(
    'flex flex-1 flex-col items-center gap-1 py-2.5 text-[0.7rem] font-medium transition-colors',
    isActive ? 'text-foreground' : 'text-muted-foreground',
  )

export function BottomNav() {
  return (
    <nav className="fixed inset-x-0 bottom-0 z-20 border-t border-border/60 bg-background/80 pb-[env(safe-area-inset-bottom)] backdrop-blur-xl">
      <div className="mx-auto flex max-w-md items-center px-6">
        <NavLink to="/" end className={itemClass}>
          <List className="size-5" />
          Lançamentos
        </NavLink>
        <Link
          to="/lancar"
          aria-label="Novo lançamento"
          className="group relative -mt-7 flex size-14 items-center justify-center rounded-full"
        >
          {/* Halo espectral atrás do botão: a luz que entra no prisma. */}
          <span
            aria-hidden
            className="absolute inset-0 rounded-full opacity-60 blur-md transition-opacity group-hover:opacity-90"
            style={{ background: 'var(--spectrum-conic)' }}
          />
          <span
            className="relative flex size-14 items-center justify-center rounded-full p-[2px]"
            style={{ background: 'var(--spectrum-conic)' }}
          >
            <span className="flex size-full items-center justify-center rounded-full bg-foreground text-background">
              <Plus className="size-6" strokeWidth={2.5} />
            </span>
          </span>
        </Link>
        <NavLink to="/contas" className={itemClass}>
          <Wallet className="size-5" />
          Contas
        </NavLink>
      </div>
    </nav>
  )
}
