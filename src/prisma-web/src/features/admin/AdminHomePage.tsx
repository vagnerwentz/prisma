import { ChevronRight, Database, Users, type LucideIcon } from 'lucide-react'
import { Link } from 'react-router'
import { AdminBack } from './AdminBack'

// A entrada do admin (docs/admin-email.md, 3.2): um cartão por área. Área nova entra aqui como mais um cartão.
const areas: { to: string; title: string; description: string; icon: LucideIcon }[] = [
  {
    to: '/admin/mercado',
    title: 'Dados de mercado',
    description: 'As taxas do Banco Central: estado, próxima busca e sincronização na hora.',
    icon: Database,
  },
  {
    to: '/admin/pessoas',
    title: 'Pessoas',
    description: 'Quantas contas há e quantas estão sem acessar o app. Só números, nunca quem é.',
    icon: Users,
  },
]

export function AdminHomePage() {
  return (
    <main className="mx-auto flex max-w-2xl flex-col gap-6 px-4 pt-3 pb-32">
      <AdminBack to="/" label="Resumo" />
      <div>
        <h1 className="font-display text-3xl">Administração</h1>
        <div className="spectrum-line mt-2 opacity-80" />
        <p className="mt-3 text-sm text-muted-foreground">O que só você, como administrador, vê e faz no Prisma.</p>
      </div>

      <ul className="flex flex-col gap-3">
        {areas.map(({ to, title, description, icon: Icon }) => (
          <li key={to}>
            <Link
              to={to}
              className="surface flex items-center gap-4 rounded-3xl p-5 transition-colors outline-none hover:bg-muted/40 focus-visible:ring-3 focus-visible:ring-ring/50"
            >
              <span className="flex size-11 shrink-0 items-center justify-center rounded-2xl bg-muted">
                <Icon className="size-5" aria-hidden />
              </span>
              <span className="flex min-w-0 flex-1 flex-col gap-0.5">
                <span className="font-display text-xl">{title}</span>
                <span className="text-sm text-muted-foreground">{description}</span>
              </span>
              <ChevronRight className="size-5 shrink-0 text-muted-foreground" aria-hidden />
            </Link>
          </li>
        ))}
      </ul>
    </main>
  )
}
