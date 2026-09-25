import { LogOut } from 'lucide-react'
import { Outlet, useMatch, useNavigate } from 'react-router'
import { BottomNav } from '@/components/BottomNav'
import { Wordmark } from '@/components/brand/PrismLogo'
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuItem,
  DropdownMenuLabel,
  DropdownMenuSeparator,
  DropdownMenuTrigger,
} from '@/components/ui/dropdown-menu'
import { useCurrentUser, useLogout } from '@/features/auth/queries'
import { cn } from '@/lib/utils'

export function AppLayout() {
  // O cabeçalho acompanha a largura da tela aberta: a Análise alarga no computador (docs/fase-2.md, 4).
  const wide = useMatch('/analise') !== null
  return (
    <div className="min-h-dvh bg-background">
      <header className="sticky top-0 z-20 border-b border-border/60 bg-background/80 pt-[env(safe-area-inset-top)] backdrop-blur-xl">
        <div className={cn('mx-auto flex h-14 max-w-2xl items-center justify-between px-4', wide && 'lg:max-w-5xl')}>
          <Wordmark />
          <UserMenu />
        </div>
      </header>
      <Outlet />
      <BottomNav />
    </div>
  )
}

function UserMenu() {
  const { data: user } = useCurrentUser()
  const logout = useLogout()
  const navigate = useNavigate()
  const initial = user?.email.charAt(0).toUpperCase() ?? '·'

  return (
    <DropdownMenu>
      <DropdownMenuTrigger
        aria-label="Menu da conta"
        className="rounded-full p-[2px] outline-none focus-visible:ring-2 focus-visible:ring-ring"
        style={{ background: 'var(--spectrum-conic)' }}
      >
        <span className="flex size-8 items-center justify-center rounded-full bg-background text-sm font-semibold">
          {initial}
        </span>
      </DropdownMenuTrigger>
      <DropdownMenuContent align="end" className="w-60">
        <DropdownMenuLabel className="font-normal">
          <span className="block text-xs text-muted-foreground">Conectado como</span>
          <span className="block truncate text-sm">{user?.email}</span>
        </DropdownMenuLabel>
        <DropdownMenuSeparator />
        <DropdownMenuItem
          disabled={logout.isPending}
          onSelect={async () => {
            await logout.mutateAsync()
            navigate('/entrar', { replace: true })
          }}
        >
          <LogOut />
          Sair
        </DropdownMenuItem>
      </DropdownMenuContent>
    </DropdownMenu>
  )
}
