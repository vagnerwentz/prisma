import { PrismLogo } from '@/components/brand/PrismLogo'
import { instagram } from '@/lib/brands/logos.generated'
import { DemoVideo } from './DemoVideo'
import { Hero } from './Hero'
import { InstallmentsDemo } from './InstallmentsDemo'
import { Closing, Comparison, Faq, HowToStart, Problem, Trust } from './Sections'
import { StatementDemo } from './StatementDemo'
import { StickyBar } from './StickyBar'
import { TradeNoteScene } from './TradeNoteScene'
import { WhyIBuiltIt } from './WhyIBuiltIt'

// Página de apresentação (docs/landing.md): servida em "/" para quem chega sem sessão, com o cadastro aberto. React
// pré-renderizado no build (D9). Sem script, fonte ou imagem de terceiros e sem cookie (seção 9, LGPD).
const instagramUrl = 'https://www.instagram.com/prismamoneybrasil/'

// Quem é o site para o Google: o nome público e o perfil oficial.
const organizationSchema = JSON.stringify({
  '@context': 'https://schema.org',
  '@type': 'Organization',
  name: 'Prisma Money',
  url: 'https://prismamoney.com.br/',
  sameAs: [instagramUrl],
})

export function LandingPage() {
  return (
    <>
      {/* O halo começa no topo da página, atrás do cabeçalho e da abertura. A moldura recorta o que passa da largura. */}
      <div aria-hidden className="pointer-events-none absolute inset-x-0 top-0 h-[40rem] overflow-hidden">
        <div className="halo absolute -top-48 left-1/2 size-[44rem] -translate-x-1/2 rounded-full opacity-25 blur-3xl" />
      </div>

      <StickyBar after="abertura" />

      <header className="relative mx-auto flex max-w-6xl items-center justify-between px-5 pt-5 sm:px-8 sm:pt-7">
        <a href="/" className="flex items-center gap-2" aria-label="Prisma Money, início">
          <PrismLogo className="size-7" />
          <span className="font-display text-2xl leading-none">Prisma Money</span>
        </a>
        <a
          href="/entrar"
          className="rounded-full px-4 py-2 text-sm font-medium text-muted-foreground transition-colors duration-150 ease-out hover:text-foreground"
        >
          Entrar
        </a>
      </header>

      <main className="relative">
        <Hero />
        <Problem />
        <DemoVideo />
        <StatementDemo />
        <InstallmentsDemo />
        <TradeNoteScene />
        <Comparison />
        <WhyIBuiltIt />
        <HowToStart />
        <Trust />
        <Faq />
        <Closing />
      </main>

      <footer className="mx-auto flex max-w-6xl flex-col gap-3 border-t border-border/60 px-5 py-8 text-sm text-muted-foreground sm:flex-row sm:items-center sm:justify-between sm:px-8">
        <div className="flex flex-col gap-2">
          <p>Prisma Money. Controle financeiro pessoal, feito no Brasil.</p>
          {/* Etapa A.4: a política e os termos (HTML pronto, /privacidade e /termos) e o canal de contato. */}
          <nav aria-label="Documentos" className="flex flex-wrap gap-x-4 gap-y-1">
            <a href="/privacidade" className="underline-offset-4 hover:text-foreground hover:underline">
              Política de privacidade
            </a>
            <a href="/termos" className="underline-offset-4 hover:text-foreground hover:underline">
              Termos de uso
            </a>
            <a href="mailto:contato@prismamoney.com.br" className="underline-offset-4 hover:text-foreground hover:underline">
              contato@prismamoney.com.br
            </a>
          </nav>
        </div>
        <div className="flex items-center gap-5">
          {/* Link comum: o navegador só vai ao Instagram se a pessoa tocar (nada é carregado dele aqui, seção 9). */}
          <a
            href={instagramUrl}
            target="_blank"
            rel="me noopener noreferrer"
            aria-label="Instagram do Prisma Money: @prismamoneybrasil"
            className="group flex items-center gap-2 font-medium text-foreground"
          >
            {/* Logo do simple-icons (npm run gen:brands), na cor do texto: a marca colorida brigaria com o espectro. */}
            <svg viewBox="0 0 24 24" aria-hidden="true" className="size-4 fill-current transition-colors duration-150 ease-out group-hover:text-[#FF0069]">
              <path d={instagram.path} />
            </svg>
            <span className="underline-offset-4 group-hover:underline">@prismamoneybrasil</span>
          </a>
          <a href="/entrar" className="font-medium text-foreground underline-offset-4 hover:underline">
            Entrar
          </a>
        </div>
      </footer>
      <script type="application/ld+json" dangerouslySetInnerHTML={{ __html: organizationSchema }} />
    </>
  )
}
