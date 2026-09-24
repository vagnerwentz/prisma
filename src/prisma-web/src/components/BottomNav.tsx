import { List, Plus, Wallet } from 'lucide-react'
import { Link, NavLink } from 'react-router'
import { cn } from '@/lib/utils'

const itemClass = ({ isActive }: { isActive: boolean }) =>
  cn('flex flex-1 flex-col items-center gap-1 py-2 text-xs', isActive ? 'text-foreground' : 'text-muted-foreground')

export function BottomNav() {
  return (
    <nav className="fixed inset-x-0 bottom-0 z-10 border-t bg-background pb-[env(safe-area-inset-bottom)]">
      <div className="mx-auto flex max-w-2xl items-center">
        <NavLink to="/" end className={itemClass}>
          <List className="size-5" />
          Lançamentos
        </NavLink>
        <Link
          to="/lancar"
          aria-label="Novo lançamento"
          className="-mt-6 flex size-14 items-center justify-center rounded-full bg-primary text-primary-foreground shadow-lg"
        >
          <Plus className="size-7" />
        </Link>
        <NavLink to="/contas" className={itemClass}>
          <Wallet className="size-5" />
          Contas
        </NavLink>
      </div>
    </nav>
  )
}
