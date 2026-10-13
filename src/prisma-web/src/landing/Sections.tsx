import { Check, Minus } from 'lucide-react'
import { faq } from './faq'
import { Cta, CtaNote, SectionTitle } from './parts'

// Seções de texto da página de apresentação (docs/landing.md). Cada frase precisa existir no app (seção 2).

export function Problem() {
  const pains = [
    'Comprou no dia 26 e só vai pagar em dezembro. A planilha acha que o gasto foi em outubro.',
    'O app soma a compra e soma o pagamento da fatura. Seu mês parece o dobro.',
    'Dez parcelas aqui, uma assinatura ali. Quanto da próxima fatura já está comprometido?',
  ]
  return (
    <section className="mx-auto max-w-6xl px-5 pt-8 pb-8 sm:px-8 sm:pt-16">
      <SectionTitle>Dinheiro no Brasil não é só somar e subtrair</SectionTitle>
      <ul className="mt-10 grid gap-4 sm:grid-cols-3">
        {pains.map((pain) => (
          <li key={pain} className="surface rounded-3xl p-6">
            <p className="text-lg leading-snug text-pretty">{pain}</p>
          </li>
        ))}
      </ul>
    </section>
  )
}

// Comparação honesta: sem citar concorrente, e "nem sempre" em vez de "nunca" para o que alguns apps fazem.
type Cell = { ok: boolean; text: string }
const comparison: { label: string; sheet: Cell; app: Cell; prisma: Cell }[] = [
  {
    label: 'A compra entra na fatura em que vai ser paga',
    sheet: { ok: false, text: 'você calcula' },
    app: { ok: false, text: 'nem sempre' },
    prisma: { ok: true, text: 'sempre' },
  },
  {
    label: 'Pagar a fatura não conta o gasto duas vezes',
    sheet: { ok: false, text: 'você cuida' },
    app: { ok: false, text: 'nem sempre' },
    prisma: { ok: true, text: 'sempre' },
  },
  {
    label: 'Cada parcela no mês em que cai, sem perder centavo',
    sheet: { ok: false, text: 'você calcula' },
    app: { ok: false, text: 'nem sempre' },
    prisma: { ok: true, text: 'sempre' },
  },
  {
    label: 'Nota de corretagem inteira, com as taxas rateadas',
    sheet: { ok: false, text: 'você calcula' },
    app: { ok: false, text: 'raro' },
    prisma: { ok: true, text: 'sim' },
  },
  {
    label: 'Sem pedir a senha do banco',
    sheet: { ok: true, text: 'sim' },
    app: { ok: false, text: 'nem sempre' },
    prisma: { ok: true, text: 'sim' },
  },
]

function CellView({ cell, strong = false }: { cell: Cell; strong?: boolean }) {
  return (
    <span className={strong ? 'flex items-center gap-1.5 font-medium' : 'flex items-center gap-1.5 text-muted-foreground'}>
      {cell.ok ? (
        <span className="grid size-5 shrink-0 place-items-center rounded-full bg-[image:var(--spectrum-cool)] text-white">
          <Check aria-hidden className="size-3" strokeWidth={3} />
        </span>
      ) : (
        <Minus aria-hidden className="size-4 shrink-0" />
      )}
      {cell.text}
    </span>
  )
}

export function Comparison() {
  return (
    <section className="mx-auto max-w-5xl px-5 py-16 sm:px-8 sm:py-24">
      <SectionTitle>Planilha, app comum ou Prisma Money</SectionTitle>
      <div className="mt-10 overflow-hidden rounded-3xl">
        <table className="compare w-full border-separate border-spacing-0 text-left text-sm sm:text-base">
          <thead>
            <tr>
              <th scope="col" className="sr-only">
                O que importa
              </th>
              <th scope="col" className="px-2 pb-3 font-medium text-muted-foreground sm:px-4">
                Planilha
              </th>
              <th scope="col" className="px-2 pb-3 font-medium text-muted-foreground sm:px-4">
                App comum
              </th>
              <th scope="col" className="compare-prisma rounded-t-2xl px-2 pt-3 pb-3 font-display text-xl sm:px-4 sm:text-2xl">
                Prisma
              </th>
            </tr>
          </thead>
          <tbody>
            {comparison.map((row, i) => (
              <tr key={row.label} className="compare-row">
                <th scope="row" className="py-4 pr-2 align-top font-normal sm:pr-6">
                  {row.label}
                </th>
                <td className="px-2 py-4 align-top sm:px-4">
                  <CellView cell={row.sheet} />
                </td>
                <td className="px-2 py-4 align-top sm:px-4">
                  <CellView cell={row.app} />
                </td>
                <td className={`compare-prisma px-2 py-4 align-top sm:px-4 ${i === comparison.length - 1 ? 'rounded-b-2xl' : ''}`}>
                  <CellView cell={row.prisma} strong />
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>
    </section>
  )
}

export function HowToStart() {
  const steps = [
    { title: 'Crie sua conta.', text: 'Só e-mail e senha.' },
    { title: 'Cadastre suas contas e cartões.', text: 'Com o dia em que o cartão fecha e vence.' },
    { title: 'Lance na hora, pelo celular.', text: 'Em segundos, ainda na fila do caixa.' },
  ]
  return (
    <section className="mx-auto max-w-6xl px-5 py-16 sm:px-8 sm:py-24">
      <SectionTitle>Comece em três passos</SectionTitle>
      <ol className="mt-10 grid gap-4 sm:grid-cols-3">
        {steps.map((step, i) => (
          <li key={step.title} className="surface flex flex-col gap-2 rounded-3xl p-6">
            <span className="step">{i + 1}</span>
            <p className="text-lg font-medium">{step.title}</p>
            <p className="text-muted-foreground">{step.text}</p>
          </li>
        ))}
      </ol>
      <div className="mt-10 flex flex-col items-start gap-3">
        <Cta />
        <CtaNote />
      </div>
    </section>
  )
}

export function Trust() {
  const facts = [
    { title: 'Sem senha do banco', text: 'O Prisma Money não se conecta à sua conta bancária. Você lança o que gastou.' },
    { title: 'Só você vê', text: 'Nenhum outro usuário enxerga seus lançamentos.' },
  ]
  return (
    <section className="mx-auto max-w-6xl px-5 py-16 sm:px-8 sm:py-24">
      <SectionTitle>Seu dinheiro é assunto seu</SectionTitle>
      <dl className="mt-10 grid gap-x-10 gap-y-8 sm:grid-cols-2">
        {facts.map((fact) => (
          <div key={fact.title}>
            <dt className="text-lg font-medium">{fact.title}</dt>
            <dd className="mt-1 text-muted-foreground">{fact.text}</dd>
          </div>
        ))}
      </dl>
    </section>
  )
}

// As perguntas também vão para o Google como FAQPage, do mesmo texto.
const faqSchema = JSON.stringify({
  '@context': 'https://schema.org',
  '@type': 'FAQPage',
  mainEntity: faq.map((item) => ({
    '@type': 'Question',
    name: item.question,
    acceptedAnswer: { '@type': 'Answer', text: item.answer },
  })),
})

export function Faq() {
  return (
    <section className="mx-auto max-w-3xl px-5 py-16 sm:px-8 sm:py-24">
      <SectionTitle>Perguntas</SectionTitle>
      <div className="faq mt-8">
        {faq.map((item) => (
          <details key={item.question}>
            <summary>{item.question}</summary>
            <p>{item.answer}</p>
          </details>
        ))}
      </div>
      <script type="application/ld+json" dangerouslySetInnerHTML={{ __html: faqSchema }} />
    </section>
  )
}

export function Closing() {
  return (
    <section className="relative overflow-hidden">
      <div
        aria-hidden
        className="halo pointer-events-none absolute top-1/2 left-1/2 h-64 w-[28rem] -translate-x-1/2 -translate-y-1/2 rounded-full opacity-20 blur-3xl"
      />
      <div className="relative mx-auto flex max-w-3xl flex-col items-center gap-6 px-5 py-20 text-center sm:px-8 sm:py-28">
        <h2 className="font-display text-4xl leading-[1.05] text-balance sm:text-6xl">Veja o seu dinheiro em cores.</h2>
        <p className="text-lg text-muted-foreground">Comece pelo cartão e pelas contas. O resto aparece sozinho.</p>
        <Cta />
        <CtaNote />
      </div>
    </section>
  )
}
