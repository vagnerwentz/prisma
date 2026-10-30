import { ArrowLeft } from 'lucide-react'
import { Link } from 'react-router'
import { Button } from '@/components/ui/button'

// Volta de uma área do admin para a entrada (docs/admin-email.md, 3.2). A entrada volta para o Resumo.
export function AdminBack({ to = '/admin', label = 'Administração' }: { to?: string; label?: string }) {
  return (
    <nav>
      <Button asChild variant="ghost" size="sm" className="-ml-2 rounded-full text-muted-foreground">
        <Link to={to}>
          <ArrowLeft />
          {label}
        </Link>
      </Button>
    </nav>
  )
}
