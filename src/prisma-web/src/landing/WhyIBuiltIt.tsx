import { PrismLogo } from '@/components/brand/PrismLogo'

// Por que eu fiz (docs/landing.md, D18): a história de quem fez, em primeira pessoa, a partir das dores reais que
// levaram ao app. É a prova mais honesta para um produto que está começando e ainda não tem depoimentos. Sem nome de
// quem usa.
export function WhyIBuiltIt() {
  return (
    <section className="mx-auto max-w-3xl px-5 py-16 sm:px-8 sm:py-24">
      <figure className="surface relative overflow-hidden rounded-3xl p-6 sm:p-10">
        <div aria-hidden className="spectrum-line absolute inset-x-0 top-0 rounded-none" />
        <p className="text-sm font-medium text-muted-foreground">Por que eu fiz o Prisma Money</p>
        <blockquote className="mt-6 flex flex-col gap-4 text-lg leading-relaxed text-pretty">
          <p className="font-display text-3xl leading-tight sm:text-4xl">Eu controlava tudo numa planilha. Funcionava até o cartão.</p>
          <p>
            O meu cartão fecha sete dias antes do vencimento. A compra feita no dia do fechamento já ia para a fatura seguinte,
            e a planilha não sabia disso. No começo do mês parecia que sobrava dinheiro. No vencimento, faltava.
          </p>
          <p>
            Testei aplicativos. Quase todos somavam a compra e somavam de novo o pagamento da fatura. As parcelas apareciam
            todas no mês da compra.
          </p>
          <p>
            Nos investimentos, era pior. Eu tinha anos de notas de corretagem e nenhum lugar que entendesse uma nota inteira,
            com vários ativos e as taxas divididas entre eles.
          </p>
          <p>
            Então eu fiz o Prisma Money. Primeiro para mim, depois para a minha família. Cada regra que está aqui nasceu de uma
            conta que não fechava.
          </p>
        </blockquote>
        <figcaption className="mt-8 flex items-center gap-3 text-sm text-muted-foreground">
          <PrismLogo className="size-6" />
          Quem fez o Prisma Money
        </figcaption>
      </figure>
    </section>
  )
}
