# CLAUDE.md — Prisma

> Controle financeiro pessoal para o contexto brasileiro: PIX, cartão de crédito com
> ciclo de fatura, parcelamento e nota fiscal eletrônica.
>
> **Nome de trabalho:** `Prisma`. Trocar o nome é um localizar-e-substituir no
> namespace raiz e no nome dos projetos. Nada mais depende dele.
>
> **Nome público:** `Prisma Money` (decisão do dono, 2026-10-13): domínio `prismamoney.com.br`, Instagram `@prismamoneybrasil`,
> página de apresentação (`docs/landing.md`, D14). O código continua `Prisma`.

---

## 1. Idioma

Regra de três camadas, sem exceção:

| Onde | Idioma |
|---|---|
| **Código** (classes, campos, métodos, endpoints, tabelas, colunas) | **Inglês** |
| **Documentação** (`CLAUDE.md`, `PLAN.md`, `docs/`, comentários) | Português |
| **Interface e mensagens ao usuário** (incluindo erros da API) | pt-BR |

Nada de código misto. `Transaction.SettlementDate` ao lado de `CreatedAt`, nunca
`Transacao.DataCaixa`.

**Exceção:** nomes próprios de sistemas brasileiros não se traduzem — `Pix`,
`Boleto`, `Ted`, `Cnpj`, `NfceKey`, `Ofx`.

**Exceção de documentação:** o `README.md` é em **inglês**, por ser a vitrine pública do repositório
(decisão do dono, 2026-09-28). Ele não repete números que envelhecem (contagem de testes) e nunca traz
dado real, nome de quem usa o app nem endereço de produção. Licença: todos os direitos reservados.

### Glossário do domínio

| Conceito (pt) | Código (en) | Observação |
|---|---|---|
| Fatura do cartão | `Statement` | não `Invoice`: invoice é cobrança de fornecedor |
| Data de fechamento | `ClosingDate` / `ClosingDay` | |
| Data de vencimento | `DueDate` / `DueDay` | |
| Data da compra | `PurchaseDate` | quando aconteceu |
| Data de caixa | `SettlementDate` | quando o dinheiro sai de fato |
| Compra parcelada | `InstallmentPurchase` | |
| Parcela | `Installment` / `InstallmentNumber` | |
| Estorno | `TransactionType.Refund` | abate despesa, nunca é receita |
| Estabelecimento | `Merchant` | |
| Meio de pagamento | `PaymentMethod` | |
| Valor em centavos | `AmountCents` | |
| Conta corrente | `AccountType.Checking` | |
| Carteira / dinheiro | `AccountType.Cash` | |
| Regra de categorização | `CategorizationRule` | |
| Catálogo de categorias padrão | `DefaultCategories` | versionado (`Version`); cada categoria com `TemplateKey` estável |
| Descrição original | `RawDescription` | texto cru da importação |
| Compra presa a uma fatura | `StatementPinned` | movida à mão; o recálculo não a devolve |
| Recálculo das faturas | `StatementReconciliation` | decide a fatura de cada compra no cartão |
| Lançamento que se repete | `Recurrence` | série: modelo do lançamento + agenda (`RecurrenceSchedule`); na tela, "se repete" |
| Ocorrência de uma série | `OccurrenceDate` (no `Transaction`, com `RecurrenceId`) | a data da agenda; fica mesmo se a data do lançamento mudar |
| Ocorrência pendente | `RecurrencePending` | cobrança que cairia numa fatura já paga; espera a pessoa decidir |
| Débito automático | `RecurrenceKind.AutoDebit` | série que o banco debita da conta corrente no próximo dia útil do vencimento |
| Valor que muda a cada mês | `AmountVaries` | no débito automático, o valor da série é a estimativa |
| Lançamento a conferir | `Transaction.AmountEstimated` | saiu com a estimativa; a pessoa confirma ou corrige (`ConfirmAmount`) |
| Ativo da bolsa | `Asset` (catálogo global) / `ListedAsset` (como o fornecedor o lista) | código em `Symbol`, tipo em `AssetKind`; `docs/investimentos.md` |
| Ativo na carteira | `Holding` | "eu tenho BBAS3", do usuário; aponta para o `Asset` global; sem quantidade até as operações |
| Provento | `Transaction` com `AssetId` + `PayoutKind` | receita na conta onde o dinheiro caiu; dividendo, JCP (`InterestOnEquity`), rendimento (`FundIncome`) |
| Mercado fracionário | `AssetSymbol.BaseOf` | "BBAS3F" é o mesmo ativo que BBAS3: nunca vira ativo próprio |
| Nota de corretagem | `TradeNote` | digitada (nunca importada): corretora, data, operações e custos; envelope do rateio |
| Operação (linha da nota) | `Trade` (`TradeSide.Buy`/`Sell`) | não mexe em saldo de conta: carteira não é caixa |
| Taxas da nota | `FeesCents` | liquidação, emolumentos, ISS, "outros"…: um total, rateado por valor; IRRF não é custo |
| Corretagem | `BrokerageFee` | "taxa operacional"; por ordem, só nos tipos marcados na nota (`BrokerageFeeKinds`, unit conta como ação; padrão: ação). Não é o "Clearing" da nota |
| Posição | `AssetPosition` | calculada das notas (`PositionCalculator`), nunca gravada |
| Carteira em um ano | `PortfolioSnapshot` | no fim de 31/12 (ou hoje), pelo custo; grupos (`PortfolioGroup`: unit com ações, fundos menores juntos) e pesos que somam 100% (`PortfolioWeights`) |
| Preço médio | `AveragePriceCents` (só preço) / `AverageCostCents` (com custos) | custo bruto e custo de aquisição (`CostBasis`) ÷ quantidade |
| Resultado da venda | `RealizedCents` | valor líquido − custo de aquisição que saiu; não é receita |
| Evento da empresa | `CorporateEvent` (catálogo global) | a regra (proporção, data-com), nunca a quantidade de alguém; só a curadoria escreve (`corporate-events.json`) |
| Desdobramento / grupamento | `CorporateEventKind.Split` | `from:to` (1:8, 10:1); custos ficam, quantidade muda |
| Conversão (incorporação, troca de código) | `CorporateEventKind.Conversion` | origem zera; destino recebe quantidade e custos inteiros, sem resultado |
| Liquidação paga em cotas | `CorporateEventKind.Liquidation` | origem sai como venda (cotas, fração e caixa: `TargetUnitPrice`, `CashPerUnit`, em 10⁻⁸ real); destino entra como compra pelo preço de referência |
| Ativo que saiu da bolsa antes do catálogo | `Asset.FromHistory` | entra inativo com o evento; nunca digitado pelo usuário |
| Aviso de ativo faltando | `AssetRequest` | "Não encontrou? Avise a gente": só o código, um por pessoa; "chegou" é calculado pelo catálogo, nunca gravado |
| Conferir um evento | `EventReview` | o que o evento fez com a quantidade da pessoa: "Confere" ou "Não confere" (para a curadoria); guarda a versão do evento, e o evento corrigido volta a pedir |
| Dia útil bancário | `BankCalendar` | fins de semana e feriados nacionais; débito automático, vencimento da fatura e os dias úteis do pré-fixado |
| Fila de e-mails | `OutboxEmail` (`EmailKind`, `EmailStatus`) | pedido gravado junto com o motivo; o conteúdo e o token saem na hora do envio (`IEmailComposer`); `docs/email.md` |
| Data de entrada da conta | `AppUser.CreatedAt` (`users.created_at`) | carimbada com o `IClock` na criação; nas contas de antes da coluna é aproximada (o registro mais antigo que a pessoa ainda tinha) |
| Aceite dos termos | `LegalTerms` (`TermsVersion`, `PrivacyVersion`, `TermsAcceptedAt` no usuário) | a caixa do cadastro; versão nova pede o aceite de novo ao entrar (`SessionUser.MustAcceptTerms`, A.4) |
| Conta apagada | `DeletedAccount` (`deleted_accounts`) | só o id e o instante, 90 dias, para apagar de novo depois de restaurar um backup (A.3) |
| Senhas erradas com prazo | `AppUserManager.AccessFailedAsync` / `LastAccessFailedAt` | só soma com as anteriores até 15 min depois da última; vale para login, confirmação, troca de senha e apagar a conta (H.4e) |
| Link de confirmação / de senha nova | `ConfirmEmail` / `ResetPassword` | token do Identity depois do `#` (`/confirmar-email#u=…&t=…`, `/nova-senha#…`); 3 dias e 1 hora, uma vez só |
| Objetivo (a "caixa") | `SavingsGoal` (`savings_goals`) | na tela, "objetivo": nunca "cofrinho". Tem nome, meta e % do CDI padrão; excluir um com aportes pergunta se os investimentos ficam ("Sem objetivo") ou vão junto; `docs/renda-fixa.md` |
| Aporte em renda fixa | `FixedIncomeDeposit` (`fixed_income_deposits`) | data, valor, taxa, vencimento opcional; `GoalId` opcional; produto `Cdb`, `Lci` ou `Lca`; índice `Cdi`, `CdiPlus` ou `Prefixed` (IPCA e IPCA+ são recusados, e continuam assim: a implementação está em espera); `RateE4` é a taxa (percentual do CDI, ou taxa ao ano); `ClientRequestId` contra duplicar |
| Aporte que saiu de uma conta | `FixedIncomeDeposit.TransferPairId` (`FixedIncomeFunding`) | cria a transferência junto, na mesma gravação; valor e data ficam travados; excluir pergunta se a transferência fica ou sai junto (R.4b) |
| Taxa do CDI | `ReferenceRate` (`reference_rates`, global) / `DailyRate` | série SGS 12 do Banco Central, só dias úteis; fração diária em 10⁻⁸ (`DailyRateE8`); o percentual do CDI viaja em E4 (100% = `1_000_000`, `IndexPercent`) |
| Rendimento de um aporte | `FixedIncomeEngine.Balance` | fator da B3 em `Int128`, 16 casas por passo e 8 no final; saldo = principal × fator, meio centavo para cima; a taxa do dia do aporte conta, a do dia da referência não; nunca gravado |
| Imposto estimado | `FixedIncomeTax` (`TaxEstimate`) | CDB: IOF abaixo de 30 dias (tabela do Decreto 6.306) + IR regressivo 22,5/20/17,5/15% sobre o rendimento líquido do IOF, por aporte. LCI e LCA (`FixedIncomeProductKind`): IR zero para pessoa física (Lei 11.033, art. 3º), IOF zero na LCA e tabela do CDB na LCI (R.13). Regra versionada por data e por produto; é o imposto de resgatar hoje, não imposto devido |
| Resgate de um aporte | `FixedIncomeRedemption` (`fixed_income_redemptions`) | total ou parcial, pelo valor bruto; principal, rendimento e imposto (calculado, ou o líquido do extrato) **congelados** na gravação, porque o resgate seguinte parte do que sobrou; só o último de cada aporte se exclui; aporte com resgate trava valor, data, produto, índice e taxa. Pode cair numa conta (`FixedIncomeRedemptionFunding`, R.24): o principal volta como transferência da conta de investimento e o rendimento líquido entra como receita, nunca o imposto como despesa; só aporte financiado por conta. API e tela prontas (R.23 a R.25): `docs/renda-fixa-resgate.md` |
| Resgate em lote | `RedemptionAllocator` (`Prisma.Domain.FixedIncome`) / `RedemptionBatches` (`Features/FixedIncome`) | um resgate do extrato que cobre vários aportes (R.27 a R.31): o valor bruto sai do aporte **mais antigo ao mais novo** (cada um pelo saldo inteiro, o último pode sair em parte; aporte sem saldo é pulado), e desmarcar um aporte na prévia redistribui o valor. **Cada aporte mantém o seu `FixedIncomeRedemption`**, calculado pelo `Create` de sempre (a divisão é do domínio, o imposto não é refeito); o lote os cria **juntos, tudo ou nada**, com uma conta de destino (aporte sem origem em conta entra só como registro) e se desfaz junto (`…/redemptions/batch`, `preview`, `undo`). Líquido do extrato **um só**, repartido na proporção do imposto estimado de cada aporte, nunca acima do rendimento dele. Pedido repetido devolve o que foi criado (um `ClientRequestId` por aporte, derivado do do lote); `Touch` em cada aporte dá 409 a dois lotes ao mesmo tempo. `docs/renda-fixa-resgate.md`, seções 15 e 16 |
| Histórico do CDI defasado ou incompleto | `ReferenceRateCoverage` (`Prisma.Domain.FixedIncome.Coverage`) | único uso do `BankCalendar` no CDI, só para diagnóstico: dia útil sem taxa avisa; o cálculo usa só as datas gravadas |
| Como o aporte rende | `Remuneration` (o índice é o `FixedIncomeIndexKind`: `Cdi`, `CdiPlus`, `Prefixed`) / `AnnualRate` | X % do CDI, CDI + taxa ao ano, ou taxa ao ano (pré-fixado, base 252 dias úteis); a taxa ao ano vai em E4 (14 % = `140_000`, de 0,01 % a 100 %). Domínio, API e tela prontos (R.10 a R.12); `docs/renda-fixa.md`, 5.5 |
| Renda fixa na carteira | `PortfolioGroup.FixedIncome` / `FixedIncomeHolding` | um grupo só para qualquer índice, pesado pelo aportado que sobra depois dos resgates (o custo da renda fixa), com uma linha por objetivo (conta só os aportes que ainda têm principal; o objetivo resgatado por inteiro some) e o rendimento bruto ao lado (R.9, R.26) |

---

## 2. Stack

| Camada | Tecnologia |
|---|---|
| Runtime | .NET 10 (LTS) |
| API | ASP.NET Core Minimal APIs + `Microsoft.AspNetCore.OpenApi` (documento em `/openapi/v1.json`, só em desenvolvimento) |
| Auth | ASP.NET Core Identity + cookie `httpOnly` + rate limiter nativo do ASP.NET Core + chaves do Data Protection no Postgres (`Microsoft.AspNetCore.DataProtection.EntityFrameworkCore`); Google OAuth adiado; e-mail confirmado para entrar, senha nova pelo e-mail, trocar a senha logado e bloqueio com prazo (`AppUserManager`) (`docs/email.md`) |
| ORM | EF Core 10 + Npgsql + EFCore.NamingConventions (snake_case) + EF Core Design (migrations; `dotnet-ef` fixado em `dotnet-tools.json`) |
| Banco | PostgreSQL 18 em todo lugar: Docker no desenvolvimento, Testcontainers nos testes, Railway na produção. Mudou a versão da produção, mudam juntos o `docker-compose.yml`, o `PostgresFixture` e a imagem do backup |
| Validação | FluentValidation |
| Testes | xUnit + Shouldly + Testcontainers + NetArchTest + CsCheck + `Microsoft.AspNetCore.Mvc.Testing` (`WebApplicationFactory`) |
| Frontend | React 19 + TypeScript + Vite + React Router; a página de apresentação é uma segunda entrada do Vite, pré-renderizada no build com `react-dom/server` (`scripts/prerender-landing.mjs`); a política de privacidade e os termos são Markdown (`src/legal/*.md`) que o build transforma em HTML pronto, sem JavaScript (`scripts/prerender-legal.mjs`) |
| Cliente da API | `openapi-typescript` (tipos gerados) + `openapi-fetch` (chamadas tipadas) |
| Testes do frontend | Vitest (formatação e conversão de dinheiro e data) |
| UI | Tailwind CSS + shadcn/ui + `sonner` (avisos) + Instrument Serif (`@fontsource`, fonte hospedada no app) |
| Ícones | `lucide-react` (import nominal, mapa fechado em `categoryIcons.ts`) + logos de marca gerados do `simple-icons` (devDependency, `npm run gen:brands`) |
| Estado de servidor | TanStack Query v5 |
| Formulários | React Hook Form + Zod |
| Gráficos | Recharts |
| Hospedagem | Railway: serviço da API (`Dockerfile` da raiz), Postgres e serviço `backup` (cron), no mesmo projeto |
| Backup | Imagem Alpine (`ops/backup/Dockerfile`) com `pg_dump` 18, `age` (criptografia), `rclone` (envio) e `curl`; Cloudflare R2 (destino, fora do Railway); Healthchecks.io (alerta quando o backup falha ou não roda) |
| E-mail | Resend por HTTP (`HttpClient` tipado, sem pacote), atrás de `IEmailTransport`; fila no Postgres (`outbox_emails`); em desenvolvimento, `.html` numa pasta local (`docs/email.md`) |
| Dados de mercado | brapi.dev (lista de ativos da B3; `HttpClient` tipado do `IHttpClientFactory`, sem pacote). Fica atrás de `IAssetListSource`, em `Infrastructure/Market/Brapi` (`docs/investimentos.md`). CDI diário: API SGS 12 do Banco Central (`HttpClient` tipado, sem pacote), atrás de `IReferenceRateSource`, em `Infrastructure/Market/Bcb` (`docs/renda-fixa.md`, seção 3) |

### Proibido

- **MediatR** e **AutoMapper** — licença comercial nas versões atuais e sem ganho
  aqui. Handlers são classes comuns injetadas no endpoint; mapeamento é manual ou
  projeção LINQ.
- **FluentAssertions** — mesma questão de licença. Use **Shouldly**.
- **Repositório genérico** (`IRepository<T>`) sobre EF Core. O `DbContext` já é Unit
  of Work e os `DbSet` já são repositórios.
- **Provider InMemory do EF Core em testes.** Não aplica constraints e traduz LINQ
  diferente do Npgsql: passa no teste e quebra em produção. Use Testcontainers.

Adicionar dependência fora desta tabela exige perguntar antes.

---

## 3. Estrutura

```
src/
├── Prisma.Domain/              # Entidades, Value Objects, regras. ZERO dependência externa.
├── Prisma.Api/
│   ├── Features/               # Vertical slices: um arquivo por caso de uso
│   └── Infrastructure/         # DbContext, configurações EF, migrations, auth
└── prisma-web/                 # React + Vite (o app em index.html; a apresentação em landing.html + src/landing/; política e termos em legal.html + src/legal/)
tests/
├── Prisma.Domain.Tests/        # Unitários puros, sem banco
├── Prisma.Api.Tests/           # Integração por slice, com Testcontainers
└── Prisma.Architecture.Tests/  # NetArchTest
docs/
├── fase-N.md                   # Detalhamento da fase em execução
├── landing.md                  # Página de apresentação: o que promete, visual, LGPD, como é servida e o que falta para ir ao ar
├── email.md                    # Etapas H.4: envio (fila no Postgres + Resend), confirmação de e-mail, senha
├── investimentos.md            # Fase I: catálogo de ativos (brapi), carteira, proventos, logos, notas, eventos; decisões e achados
├── admin-mercado.md            # Admin dos dados de mercado (R.15 e R.16 feitas): rota e tela para disparar a sincronização do BCB à mão, mantendo o horário automático
├── renda-fixa-ipca.md          # IPCA e IPCA+ (R.17 a R.21, EM ESPERA por decisão do dono, 2026-10-19): fatos verificados das fontes, decisões, cálculo e fases; não implementar sem ele pedir
├── renda-fixa-resgate.md       # Resgate de renda fixa (R.22 a R.31, feitas): parcial e total, por aporte, e em lote (um valor do extrato dividido entre vários aportes); imposto calculado, com o líquido do extrato opcional; cai numa conta se a pessoa quiser; prévia do imposto vinda da API; a Carteira pesa pelo principal que sobra
├── renda-fixa.md               # Renda fixa (R.0 a R.16; o resgate e o lote em `renda-fixa-resgate.md`): objetivos, aportes em CDB/LCI/LCA (CDI, CDI+, pré-fixado), taxas do Banco Central, rendimento, imposto estimado; fórmulas, fontes, decisões e retomada
├── operacao.md                 # Roteiros da produção: usuários do banco, backup, restauração (e contas apagadas), logs; versão do Postgres local; página e domínio; e-mail; titulares e incidente; IP do cliente atrás do Railway
├── consultas-saude.md          # SQL só de leitura e agregado para ver como o sistema está (pessoas, uso, e-mail, limpeza); nunca devolve dado de ninguém
└── validacao-premissas.md      # Premissas do produto e o resultado de cada validação
ops/
├── backup/                     # Serviço de backup: Dockerfile, backup.sh, restore.sh, teste de ida e volta
├── postgres/                   # Usuários do banco de produção: prisma_app (API) e prisma_backup (só lê)
└── privacy/                    # Cópia dos dados de uma pessoa em JSON (pedido pelo contato@; docs/operacao.md, seção 9)
```

### Regra de dependência, verificada pelo build

`Prisma.Domain` **não pode** referenciar EF Core, ASP.NET Core, Npgsql ou qualquer
infraestrutura. Há teste NetArchTest que falha o build se isso acontecer. Não
desative o teste; ajuste o código.

### Anatomia de uma vertical slice

Um caso de uso vive em **um arquivo**: request, validador, handler e endpoint juntos.

```csharp
public static class CreateTransaction
{
    public record Request(/* ... */);

    public sealed class Validator : AbstractValidator<Request> { /* ... */ }

    public sealed class Handler(AppDbContext db, ICurrentUser currentUser)
    {
        public async Task<Result<Guid>> Execute(Request req, CancellationToken ct)
        {
            // Carrega, DELEGA ao domínio, salva. O handler é magro.
        }
    }

    public static void Map(IEndpointRouteBuilder app) =>
        app.MapPost("/transactions", /* ... */);
}
```

**Handler não contém regra de negócio.** Cálculo de parcela, decisão de fatura ou
validação de invariante pertencem ao `Prisma.Domain`.

### CQRS leve

- **Comandos** passam pelo domínio, com entidades rastreadas.
- **Consultas** usam `AsNoTracking()` com projeção direta para DTO, ou SQL à mão
  quando o dashboard exigir. Nunca carregue entidade de domínio só para somar valor.
- **Tarefa em segundo plano:** `RecurrenceRunner` (2.25) gera as ocorrências vencidas dos lançamentos que
  se repetem. Idempotente por desenho: grava o lançamento e o `GeneratedThrough` juntos, com índice único
  (série, ocorrência) que conta os excluídos e `xmin` na série; rodar de novo ou ao mesmo tempo não
  duplica, e atraso é alcançado na próxima execução (`docs/fase-2.md`, 2.14, A3 a A6). Excluir uma conta
  (só possível vazia) encerra as séries ativas dela e descarta as pendências dela na mesma gravação: nunca
  fica série "ativa" sem conta (2.14, regra 8). No débito automático (2.26), a ocorrência só é gerada quando
  chega o dia útil do débito, e a de valor que muda sai com `AmountEstimated`, a conferir pelo sino
  (`docs/fase-2.md`, 2.15).
- **Segunda tarefa em segundo plano:** `AssetListWorker` sincroniza o catálogo global de ativos com a brapi
  uma vez por dia, às 4h de São Paulo, e logo ao subir se o catálogo estiver vazio. O domínio decide
  (`AssetListReconciliation`): código novo entra, conhecido atualiza, o que sumiu fica inativo e nunca é
  apagado; lista com menos da metade dos ativos é recusada e nada é gravado (`docs/investimentos.md`, seção 4). Depois da lista, a
  mesma tarefa aplica a curadoria dos eventos da empresa (`CorporateEventSync`, também ao subir a API;
  `docs/operacao.md`, seção 4.2).
- **Terceira tarefa em segundo plano:** `EmailOutboxWorker` envia a fila de e-mails (`outbox_emails`) a cada 5 s. O
  pedido nasce na mesma gravação que o motivo (`EmailQueue`, sem `SaveChanges` próprio); o conteúdo e o token saem na
  hora do envio (`IEmailComposer`), nunca gravados. Falha passageira espera e tenta de novo por 24 h; o Resend fora do
  ar não derruba nem atrasa quem pede (`docs/email.md`, seção 3).
- **Quarta tarefa em segundo plano:** `ErasureWorker` (A.3), ao subir e a cada 10 min: apaga de vez o que foi excluído há
  mais de 30 min (a política promete "em até 1 hora"), reaplica a exclusão das contas em `deleted_accounts` que voltaram
  com um backup e esquece os ids depois de 90 dias (`ErasureRunner`). Em SQL, para todos de uma vez, e sem ler dado de
  ninguém. "Apagar conta" é o `AccountEraser`, também em SQL. Tabela nova com `user_id` ou `deleted_at` entra neles: os
  testes leem o schema e acusam (`AccountDeletionTests`, `DeletedItemsPurgeTests`).
- **Quinta tarefa em segundo plano:** `ReferenceRateWorker` (R.3) sincroniza o CDI diário do Banco Central (`reference_rates`, global) às 19h30 e
  às 23h30 de São Paulo (o CDI de um dia sai à noite) e ao subir, se o histórico estiver vazio ou atrasado. É idempotente (índice
  único + releitura; um `pg_advisory_xact_lock` serializa duas execuções), toda ou nada: a fonte que falha, ou responde diferente de uma lista
  JSON (a API do BCB devolve erro com 200, 406 e até HTML), não grava nada. Valor revisado na fonte atualiza a linha (`RevisedAt`) e
  avisa no log. O domínio diz o que está defasado ou incompleto (`ReferenceRateCoverage`), e a tela mostra o aviso; sem taxa, o saldo
  mostra o aportado e diz isso (`docs/renda-fixa.md`, seção 3; `docs/operacao.md`, seção 4.5).
  **Admin (R.15 e R.16 feitas; `docs/admin-mercado.md`):** rotas `/admin/market-data` disparam essa mesma sincronização à mão (`sync` na janela normal, `refresh` num período de até 10 anos), pelo mesmo `ReferenceRateSync` e o mesmo lock, **sem tirar os horários**. A tarefa e o disparo gravam cada execução em `market_sync_runs` (`ReferenceRateSync.RunRecordedAsync`; tabela global, sem dado pessoal, 90 dias, quem pediu só no log). Falha da fonte, tempo esgotado (90 s no manual) e erro inesperado viram o desfecho gravado, não exceção; o código de erro da página de rejeição do BCB (`MarketDataException.SourceErrorCode`) fica na execução. O IPCA entra depois como uma série mensal própria (`docs/renda-fixa-ipca.md`), **mas está em espera**: o dono vai buscar dados melhores (extratos reais) antes de seguir.
- **Única consulta que grava:** `GET /categories` entrega ao usuário as categorias das versões novas do
  catálogo (`CategoryCatalog`, uma vez por versão, só para ele; `docs/fase-2.md`, 2.11). Sincronizar no
  login não bastaria: a sessão é persistente.

---

## 4. Regras de domínio invioláveis

Não são preferências. Quebrá-las gera erro de dinheiro que passa despercebido.

1. **Dinheiro em centavos, `long`.** Nunca `decimal`, nunca `double`. Use o Value
   Object `Money(long Cents)`. `R$ 129,90` é `12990`.

2. **Divisão de parcelas não perde centavo.** `Money.SplitInto(n)` distribui o resto
   nas primeiras parcelas: R$ 100,00 em 3x → `3334 + 3333 + 3333`. A soma das partes
   é sempre igual ao total, para qualquer entrada.

3. **Datas com `DateOnly`, fuso fixo em `America/Sao_Paulo`.** Nunca `DateTime.Now`
   ou `DateTime.UtcNow` no código de negócio: use a abstração `IClock`. Só `CreatedAt`
   e `UpdatedAt` usam `DateTime` em UTC.
   - "Hoje" é `IClock.Today`, o dia civil em São Paulo. Nunca `DateOnly.FromDateTime(clock.UtcNow)`.
   - Instante UTC para data de negócio: `SaoPauloTime.DateOf(utc)`.

4. **Toda transação tem duas datas.**
   - `PurchaseDate`: quando aconteceu.
   - `SettlementDate`: quando o dinheiro sai da conta. Em Pix e débito é igual a
     `PurchaseDate`; no cartão é o **`DueDate` do `Statement`** em que a compra caiu.
   - **Débito automático** (2.26): as duas datas são o dia do débito, o próximo dia útil do vencimento
     (`BankCalendar`); o vencimento fica em `OccurrenceDate`. O dia útil só vale aqui e, na 2.22, no
     vencimento da fatura: todo o resto fica na data em que aconteceu.

   **O dashboard agrega por `SettlementDate`** (visão de caixa). Decisão tomada.

   **Em qual fatura a compra do cartão cai** é decidido só pelo domínio (`StatementPlacement` na
   criação, `StatementReconciliation` depois), nesta ordem de força: fatura paga (intocável) > compra
   presa > datas editadas da fatura > previsão pelo cartão. Nunca troque `StatementId` à mão num
   handler: a data da compra não muda, e o caixa acompanha o vencimento (`docs/fase-2.md`, 2.9).

5. **Transferência nunca é receita nem despesa.** Pagamento de fatura, aporte em
   investimento e movimentação entre contas geram **duas** transações ligadas por
   `TransferPairId` e são **excluídas** de todo cálculo de receita e despesa. Contar
   transferência como gasto duplica o valor do mês. O aporte em renda fixa que saiu de uma conta também cria a
   transferência (R.4b): o "Investido" sobe, receita e despesa não mudam.

6. **Isolamento por usuário é automático.** Toda entidade tem `UserId` e o
   `AppDbContext` aplica Global Query Filter. Nunca escreva o filtro à mão e nunca use
   `IgnoreQueryFilters()` fora de seed e migration. Única exceção: restaurar registro excluído usa
   `IgnoreQueryFilters([AppDbContext.SoftDeleteFilter])`, que ignora só o filtro de exclusão.
   O filtro de dono (`OwnerFilter`) nunca é ignorado; teste de arquitetura garante.
   **Fora de uma requisição** (tarefa em segundo plano, 2.25), o código age como cada usuário: abre um
   escopo de DI e preenche `ScopedUser.UserId`, que o `HttpCurrentUser` lê antes do cookie. Filtro e
   trava de gravação valem igual; nunca desligue o filtro para "ver todos".
   **Exceção: apagar de vez** (A.3). A limpeza do excluído e o "Apagar conta" apagam em SQL, sem filtro (como as
   migrations): só `DELETE` por `deleted_at` vencido ou pelo `user_id` de quem pediu, nunca leitura de dado.
   **Exceção: dados de mercado** (decisão do dono, 2026-10-04). O catálogo de ativos da bolsa (`Asset`) e os
   eventos da empresa (`CorporateEvent`) são os mesmos para todo mundo, e as taxas diárias do CDI do Banco Central
   (`ReferenceRate`, `reference_rates`, R.2) também: não têm `UserId`, não herdam `Entity`, só a
   sincronização diária e a curadoria dos eventos (`corporate-events.json`, que traz também os ativos que saíram da
   bolsa antes do catálogo) escrevem, e o usuário só lê. Tabela sem dono só existe se estiver em `AppDbContext.GlobalTables`, com o motivo; o teste de
   arquitetura `OwnershipTests` recusa tabela que não tenha dono nem esteja na lista. Dado do usuário que
   aponta para um ativo (rendimento, operação) continua com dono e filtro, e o aporte em renda fixa (`FixedIncomeDeposit`), que só *lê* as taxas, também.

7. **Exclusão é soft delete** (`DeletedAt` + filtro global), para permitir desfazer. O "Desfazer" vale 30 minutos: depois
   a linha sai do banco de vez (`ErasureRunner`, A.3), e quem a apontava perde só o vínculo. O que vive dentro da entidade
   (tipo owned, ex.: as linhas da nota) fica junto: o `AppDbContext` impede o EF de apagá-lo (`KeepOwnedParts`).

8. **`RawDescription` é imutável.** O texto cru de importação ou NFC-e nunca é
   alterado; a normalização vive em campo separado.

---

## 5. Política de testes

Testes são o mecanismo pelo qual o comportamento é verificado sem revisar todo o
código gerado. Nesta aplicação o modo de falha típico é **errar de forma plausível**:
um número errado no dashboard não lança exceção e não aparece em log.

### Regras

- **Teste e implementação na mesma etapa, nunca em etapa posterior.** Não existe
  "escrever os testes depois".
- **Para regras de dinheiro e de data, o teste vem antes da implementação.** O valor
  esperado sai da especificação em `docs/fase-N.md`, não do código já escrito.
- **Nunca ajuste um teste para fazê-lo passar.** Se o teste reflete a especificação e
  falha, o defeito está na implementação. Se a especificação é que está errada, pare e
  pergunte.
- Nenhuma etapa é marcada como concluída com `dotnet test` vermelho.

### Níveis

**Unitários de domínio** (`Prisma.Domain.Tests`) — a base da pirâmide. Sem banco, sem
mock, sem fixture: instancia, chama, verifica. Rodam em milissegundos, então devem ser
muitos.

**Baseados em propriedades** (CsCheck) para a aritmética de centavos. Em vez de vinte
exemplos, declare a invariante e deixe a biblioteca testar milhares de entradas:
`SplitInto(n)` somado sempre devolve o valor original.

**Integração por slice** (`Prisma.Api.Tests`) com Postgres real via Testcontainers.
Cobrem endpoint, EF Core, constraints e query filters de ponta a ponta.

**Arquitetura** (`Prisma.Architecture.Tests`): o domínio não referencia
infraestrutura (NetArchTest), e nenhum tipo fora da implementação de
`Prisma.Domain.IClock` lê o relógio do sistema: `DateTime.Now`/`UtcNow`/`Today` e
`DateTimeOffset.Now`/`UtcNow`. Essa segunda regra inspeciona o IL com Mono.Cecil (que
vem junto com o NetArchTest), porque o NetArchTest só enxerga dependência de tipo, não
de membro. Confere que toda tabela tem dono (herda `Entity`, com o filtro) ou está na lista fechada de
tabelas globais (`AppDbContext.GlobalTables`; `OwnershipTests`, regra 6). Também confere que todo ícone do catálogo de categorias (`DefaultCategories`) está no mapa
fechado do front (`categoryIcons.ts`); ícone novo no catálogo entra nos dois lugares. E que só a série
(débito automático), a fatura (vencimento em dia útil) e o diagnóstico do histórico do CDI (`ReferenceRateCoverage`,
no namespace `Prisma.Domain.FixedIncome.Coverage`) e os dias úteis do pré-fixado (`Prisma.Domain.FixedIncome.Prefixed`) usam o calendário bancário (`BankCalendar`,
`docs/fase-2.md`, 2.15; `docs/renda-fixa.md`, 3.3): o resto fica na data em que aconteceu. O rendimento do CDI e do CDI+ nunca o usa: conta só as datas
que o Banco Central publicou; só o pré-fixado, que não tem série, conta dias úteis.

**Fixtures de parser** (Fase 4): arquivos reais de OFX e de fatura em PDF,
anonimizados, com o resultado esperado versionado ao lado. Quando o banco mudar o
layout, o teste acusa antes de importar dado errado.

**Serviço externo** (hoje, a brapi e o Banco Central, SGS 12): os testes nunca o chamam. Um `HttpMessageHandler` falso responde
com fixtures de respostas reais (`tests/Prisma.Api.Tests/Market/Fixtures`). O contrato com o serviço de
verdade (`BcbLiveTests` para o Banco Central) fica num teste `[LiveFact]` (`Category=Live`), ignorado no `dotnet test` e no CI; rode-o ao mexer
no cliente ou desconfiar de mudança no serviço.

### Cobertura

Sem meta percentual, porque ela induz a testar CRUD para inflar número. A regra é
qualitativa: **toda regra que mexe em dinheiro ou em data tem teste; CRUD sem regra
não precisa.** `Category` com nome e cor não merece teste unitário;
`StatementCalculator` merece dez.

### Casos obrigatórios

- Soma das parcelas igual ao total, para qualquer valor e qualquer número de parcelas.
- Compra na véspera do fechamento, no dia do fechamento e no dia seguinte, cada uma
  no `Statement` correto.
- Cartão que fecha dia 31 em mês de 30 dias e em fevereiro.
- Compra às 23h30 em São Paulo não cai no dia seguinte.
- Transferência não aparece em receita nem em despesa; pagamento de fatura não duplica
  o gasto do mês.
- Estorno abate a despesa do mês em que cai e nunca conta como receita; no cartão, entra
  na fatura aberta na data do estorno.
- Editar a parcela 3 de 10 não altera as demais.
- Dois usuários distintos nunca enxergam dados um do outro (integração).
- Transação com soft delete some da listagem e do dashboard, mas é restaurável.

---

## 6. Convenções de código

- Identificadores em inglês, conforme a seção 1.
- `record` para DTOs e Value Objects; `sealed class` para o resto.
- Nullable reference types habilitado e tratado como erro.
- **Erros de negócio esperados** usam o `Result` pattern; exceções só para o que é de
  fato excepcional.
  Exceção que escapa vira ProblemDetails em pt-BR no `ProblemExceptionHandler` (500 sem
  detalhe interno; corpo ilegível é 400; `DbUpdateConcurrencyException` é 409).
  Erro que a tela precisa reconhecer sem depender do texto leva um código (`Error.Code`, que vai como `code` no
  ProblemDetails e em `ApiError.code` no front): `EmailNotConfirmed`, `InvalidLink`, `WrongPassword`.
- **Concorrência:** entidade que dois pedidos simultâneos podem corromper (hoje, `Statement`:
  pagar duas vezes a mesma fatura) usa a coluna `xmin` do Postgres como token
  (`Property<uint>("Version").IsRowVersion()`), com teste de corrida. Quem põe ou tira compra de uma
  fatura sem mudar a fatura em si (mover compra, editar datas da fatura, trocar a data ou o cartão da
  compra, mudar a data do estorno) chama `StatementTouch.Touch` nas faturas de origem e de destino,
  para a corrida com o pagamento dar 409. A série (`Recurrence`) também tem `xmin` (editar ou encerrar
  enquanto a tarefa gera), e ligar um lançamento a uma série confere o `recurrence_id` (token de
  concorrência do EF): dois toques em "se repete" nunca criam duas séries.
- **Compatibilidade da API com a tela já aberta:** depois do deploy, uma aba aberta antes continua com
  o código antigo até recarregar (a tela de versão nova só aparece quando falta um arquivo de tela,
  `lib/crash.ts`). Por isso a API só acrescenta: campo novo num pedido entra opcional, com o
  comportamento de antes quando ausente (ex.: `accountId` na edição da compra parcelada, 2.24).
- **Logs:** o código fala só com o `ILogger` nativo, nunca com Serilog nem `Log.Information` estático.
  Todo evento é declarado em `Infrastructure/Logging/AppLog.cs` com `[LoggerMessage]` (id único,
  mensagem em inglês, propriedades nomeadas); chamar `logger.LogX(...)` direto ou montar a mensagem com
  interpolação quebra o build (CA1848 e CA2254 como erro em `src/Prisma.Api/.editorconfig`). Níveis:
  `Information` para o que se quer contar (login, conta criada, fatura paga), `Warning` para recusa ou
  abuso (login errado, rate limit, conflito 409), `Error` para exceção. Cada requisição da API gera uma
  linha (`RequestFinished`: rota modelo, status, duração) e todo log da requisição leva `traceId` e
  `userId` (escopo); a resposta de erro devolve o mesmo `traceId`. Produção escreve uma linha JSON por
  evento (`JsonLineConsoleFormatter`, formato do Railway); desenvolvimento, texto numa linha. Trocar de
  provedor (Serilog, OpenTelemetry para Datadog/CloudWatch) é mexer só na inicialização.
  Erro do navegador (tela quebrada no `ErrorBoundary`, erro ou promessa não tratados) vai ao servidor
  por `reportClientError` (`lib/clientErrors.ts` → `POST /api/client-errors`) e vira o evento
  `ClientError`, com o `userId` da sessão: só mensagem, pilha, tela sem consulta, tipo e versão. Erro
  da API, falta de conexão e versão nova publicada não são relatados. O `traceId` não aparece na tela
  (decisão para poucos usuários; rever se o projeto crescer, `PLAN.md`, H.3b).
- Tabelas e colunas em `snake_case` no Postgres, mapeadas a partir dos nomes em inglês.
- Endpoints em inglês e no plural: `/transactions`, `/accounts`, `/statements`.
- **Mensagens de erro e textos de interface em pt-BR**, ainda que o código à volta
  esteja em inglês.

---

## 7. Comandos

Configuração inicial, uma vez por máquina. A senha do Postgres fica no `.env` (lido
pelo `docker compose`, que não enxerga JSON) e a connection string da API fica em
User Secrets. Os dois precisam ter a mesma senha.

```bash
dotnet tool restore                                   # dotnet-ef na versão do repositório
cp .env.example .env                                  # e troque a senha
dotnet user-secrets set "ConnectionStrings:Default" \
  "Host=localhost;Port=5432;Database=prisma;Username=prisma;Password=<senha do .env>" \
  --project src/Prisma.Api
```

Dia a dia:

```bash
docker compose up -d                                  # Postgres

dotnet build
dotnet test
PRISMA_LIVE_TESTS=1 dotnet test tests/Prisma.Api.Tests --filter Category=Live   # contrato com a brapi e o Banco Central reais
PRISMA_LIVE_TESTS=1 PRISMA_RESEND_API_KEY=<chave> dotnet test tests/Prisma.Api.Tests --filter Category=Live   # e o Resend real (gasta 1 da cota)
PRISMA_EMAIL_PREVIEW=<pasta> dotnet test tests/Prisma.Api.Tests --filter FullyQualifiedName~EmailPreview      # prévia dos e-mails em .html
dotnet run --project src/Prisma.Api

dotnet ef migrations add <Name> -p src/Prisma.Api -s src/Prisma.Api
dotnet ef database update -p src/Prisma.Api -s src/Prisma.Api

cd src/prisma-web && npm install && npm run dev    # http://localhost:5173, proxy /api → API
npm run gen:api       # tipos TS do OpenAPI; exige a API rodando (lê http://localhost:5151)
npm run dev:lan       # rede local com HTTPS, para testar no celular (aceite o certificado local)
npm test              # Vitest
npm run build         # checagem de tipos + build (+ pré-renderização da página de apresentação)
npm run lint          # oxlint

docker build -t prisma .                           # imagem de produção (API + React), na raiz
```

**CI:** `.github/workflows/ci.yml` roda `dotnet test`; no frontend, lint, Vitest e build; e o backup de ida e
volta (`ops/backup/test/roundtrip.sh`: schema real das migrations num Postgres 18, backup, restauração e
comparação), a cada push na `main` e em pull requests. Os três devem ser checks obrigatórios no ruleset da `main` (confira em *Settings* → *Rules*). Comando
novo de verificação entra lá também.

Testes manuais: `src/Prisma.Api/Http/*.http` (HTTP Client do Rider), com a API rodando
pelo perfil `https`. Rode o Login de `auth.http` antes dos demais: o Rider guarda o cookie
de sessão. Ao criar ou mudar um endpoint, atualize o `.http` correspondente.

**A API responde sob `/api`** (`/api/health`, `/api/auth/me`), em qualquer ambiente; as rotas são
mapeadas sem o prefixo (`FrontendHosting`, `UsePathBase`). Em desenvolvimento o Vite repassa
`/api/...` para a API como está. Rode o `npm run gen:api` sempre que um endpoint mudar, e versione o
`src/lib/api-types.ts` gerado. Nos testes de integração, `CreateHttpsClient()` acrescenta o `/api`:
escreva as rotas sem ele.

**Produção** (`Dockerfile` na raiz): o ASP.NET serve os estáticos do React **no mesmo domínio**,
eliminando CORS e problemas de SameSite com o cookie de sessão. Fora de `/api`, arquivo existente é
servido (`/assets` com cache eterno, o resto revalidado) e rota de tela devolve o `index.html`. A
imagem liga `ForwardedHeaders:Enabled` (IP real atrás do proxy) e `Database:MigrateOnStartup`. O IP do cliente é o
`X-Real-IP` que o Railway escreve, **nunca o `X-Forwarded-For`**: o último valor dele é um IP da borda do Railway, o
mesmo para todo mundo, e o rate limit juntava todos os usuários (S.1b; IPv4 inteiro, IPv6 pelo /64). Se o formato
mudar, sai o aviso `ClientIpHeadersUnexpected` (`docs/operacao.md`, seção 11). O
domínio `prismamoney.com.br` (GoDaddy, DNS na Cloudflare) aponta para o Railway com a **nuvem cinza**: a laranja
poria a Cloudflare na frente, e o rate limit por IP passaria a ver o IP dela (`docs/operacao.md`, seção 7). As
chaves do cookie ficam no Postgres (`data_protection_keys`): deploy não derruba a sessão. O cookie
fica no caminho `/`, nunca herdando o `/api`: dois cookies de mesmo nome em caminhos diferentes
convivem, e o ASP.NET lê o último (o velho), derrubando a sessão logo depois do login. Em `/`, quem
chega sem sessão recebe a página de apresentação (`landing.html`, React pré-renderizado no `npm run build`, `docs/landing.md`),
mas só com o cadastro aberto: o botão dela leva ao cadastro. Com o cadastro fechado, `/` segue para o login.
O front sai comprimido (Brotli/gzip); a API não, por causa do ataque BREACH (`docs/landing.md`, D20).
`/landing.html` direto dá 404 (a página só existe em `/`). A política de privacidade e os termos ficam sempre no ar em `/privacidade` e `/termos`
(HTML pronto, com ou sem sessão e com o cadastro fechado); o arquivo pelo nome também dá 404. `robots.txt`, `sitemap.xml` e `og.jpg` ficam em `public/`, e as
telas do app levam `noindex`.
Cadastro **aberto** na produção desde 2026-10-15 (`Registration__Open=true`, A.5): a página de apresentação aparece em
`/` para quem chega sem sessão. Para fechar de novo, `Registration__Open=false`; para liberar só alguns e-mails com ele
fechado, `Registration__AllowedEmails__0` (hoje vazia). A connection string vem de `ConnectionStrings__Default`.
**E-mail** (`docs/email.md`; configuração em `docs/operacao.md`, seção 8): a chave do Resend vem de
`Email__Resend__ApiKey`, e sem ela a API não sobe fora de desenvolvimento (variável nova se aplica no Railway **antes**
do merge). Remetente `nao-responda@`, responder-para `contato@` e `App:PublicUrl` (raiz dos links) ficam no
`appsettings.json`. Em desenvolvimento os e-mails viram `.html` em `src/Prisma.Api/.dev-emails/` (fora do git), com os
links apontando para o Vite.
Em produção a API entra no banco como `prisma_app` (dono das tabelas, sem superusuário; criado por
`ops/postgres/app-user.sql`), e o `postgres` fica só para administração. O TCP Proxy do Postgres fica
desligado. O backup diário é um serviço à parte no mesmo projeto (`ops/backup/`, cron): `pg_dump` como
`prisma_backup` (só lê), criptografado com `age` (a chave privada fica só com o dono) e guardado no R2, com
alerta pelo Healthchecks.io; o job `Backup (round trip)` do CI faz backup e restauração a cada push. A imagem
do backup segue a versão do Postgres da produção (hoje 18): se o Railway mudar, ela muda junto. Onde está cada
peça (serviço, bucket, token, check, chave) e a próxima restauração de prova: `docs/operacao.md`, seção 5. Roteiros de operação (senhas, backup, restauração, logs, a tarefa das recorrências, as taxas do CDI da renda fixa e a mudança de versão do Postgres local) em `docs/operacao.md`; senha nunca passa
por conversa, arquivo versionado ou histórico do shell.

---

## 7.1 Identidade visual (frontend)

**Prisma = luz branca decomposta num espectro.** Evite o visual padrão de app de finanças.

- **Nada de verde para receita e vermelho para despesa.** Receita é "luz entrando": texto em
  `text-spectrum` (gradiente frio violeta→ciano). Despesa é tinta (`foreground`) com o sinal "−".
- **A cor vem do espectro, com parcimônia.** Base neutra (papel quente no claro, tinta no escuro);
  cada categoria é uma faixa do espectro (`Category.Color`), exibida pelo `CategoryTile`, que
  deriva fundo e ícone da cor com `oklch(from …)`. Subcategoria herda a cor da categoria pai.
- **Assinaturas:** logo `PrismLogo` (SVG próprio), filete `spectrum-line` sob títulos, anel
  `spectrum-ring` no item selecionado, halo `--halo` (só tons frios: complementares desfocados
  viram marrom), e o espectro da carteira (`PrismSpectrum`, I.7e: a luz sai do prisma em faixas, uma cor fixa por tipo
  de ativo; a renda fixa é um grupo só, em cinza-ardósia `#64748b`, R.9).
- **Ações com cor:** ação secundária leva a faixa do espectro que a representa (`.tinted-action`
  com `data-tint`): lançar de novo em violeta, estornar no azul do "saldo a favor". Editar fica em
  tinta e excluir, neutro. Ação dentro de aviso ("Desfazer", "Lançar de novo") usa o espectro frio
  (`.toast-action`) e funciona no primeiro toque mesmo com um painel aberto: o `BottomSheet` não se
  fecha com toque no aviso, e o `Toaster` religa o toque que o Radix desliga fora do painel. Ação é
  verbo + destino ("Mover para dezembro"); substantivo com seta ("Próxima fatura ›") lê como navegar.
  Interação se confere com toque real (eventos de mouse), não com `element.click()`, que pula as camadas.
- **Tipografia:** `font-display` (Instrument Serif) em valores grandes e títulos; Geist no resto.
- **Superfície e contorno:** bloco de conteúdo é `.surface` (tom e sombra suave, sem contorno), em
  qualquer tela; contorno só no que se preenche ou escolhe (campo, chip, seletor, botão secundário).
  Divisória interna em `border-border/60`. Títulos e rótulos em caixa normal, nunca em maiúsculas
  espaçadas (exceção: o selo de status da fatura).
- **Movimento:** só para mostrar mudança de estado, nunca decoração (exceção: a página de apresentação, que pode
  encenar uma vez, sem repetir e parada com `prefers-reduced-motion`; `docs/landing.md`, D8). Curvas dos tokens (`ease-out`,
  `ease-in-out`, `ease-drawer`), nada de `ease-in` nem de `transition-all`; animar `transform`,
  `opacity` ou `clip-path`, não largura. Até 300 ms na interface (painel: 420 ms na entrada, 260 ms na
  saída). Ação frequente (trocar de mês, filtrar) não anima: mantém o dado anterior esmaecido
  (`placeholderData` + `StaleFade`). Todo movimento respeita `prefers-reduced-motion` sem apagar o
  retorno visual. Barras que medem valor usam `.meter-fill`.
- **Nada com cara de carregando fora do carregamento.** Brilho que corre (o gesto do *skeleton*), texto
  esmaecido no lugar do conteúdo e círculo tracejado leem como "carregando" (achado com testes de
  usuários na 2.18). Campo vazio se mostra com cursor aceso e texto na cor normal; espaço reservado
  para ícone leva um ícone parado (ex.: o lápis da descrição), nunca vazio que muda de largura.
- **Formulário em três níveis de texto:** título de seção pequeno e discreto (`text-xs`,
  `muted-foreground`), conteúdo que se toca (chips, categorias) na cor do texto, e o valor em
  destaque. Uma mensagem por intenção: não repetir em rótulo, dica e botão o mesmo "digite o valor".
- **Skills de design** (`.claude/skills`, locais, fora do git): as do Emil Kowalski e a Impeccable são
  consultivas. Onde discordarem desta seção (texto em gradiente, halo, número em destaque), vale esta
  seção.
- **Marcas:** `findBrand` reconhece a marca pela descrição ou nome da conta. Logo do
  `simple-icons` quando existe (`npm run gen:brands` reescreve o `logos.generated.ts`; desenho
  acrescentado à mão vai no `logos.manual.ts`); bancos e corretoras com o ícone da instituição em
  `lib/brands/institutions/*.svg` (já com o fundo, otimizado com SVGO, mostrado como imagem);
  monograma na cor da marca quando não há desenho (Amazon, Inter, C6). Ativo da bolsa: `AssetTile`. Ação, unit
  e BDR mostram o logo da empresa, guardado e servido pelo Prisma (`/api/assets/{id}/logo`, sempre como `<img>`);
  fundo (o logo do fornecedor é o da gestora) e ativo sem logo mostram o código (raiz em destaque, número menor) numa
  faixa do espectro tirada da raiz (`logoSrc`, decisão do dono). Nome de marca que é palavra
  comum ("agora", "rico") casa só com o texto inteiro (`=agora`). Nunca desenhe um logo à mão.
- **Modo escuro** segue o sistema até a pessoa escolher claro ou escuro (botão do cabeçalho, ou o
  menu da conta, que também volta a seguir o sistema). O CSS lê só `[data-theme="dark"]` no `<html>`
  (variante `dark:` inclusive), nunca `prefers-color-scheme`; `lib/theme.ts` define o atributo, e o
  script do `index.html` o aplica antes da primeira pintura. A escolha fica no `localStorage`
  (`prisma.theme`): é preferência de tela, não dado financeiro. Toda tela nova é conferida nos dois
  modos.
- **Desempenho:** nada de importar conjuntos inteiros de ícones; telas secundárias com `lazy`;
  confira o tamanho do pacote principal no `npm run build` ao adicionar dependência. Linha de lista
  longa é memoizada (`memo`) e recebe só dados e callbacks estáveis (ex.: o setter do estado, com a
  chave da linha): abrir um painel não pode redesenhar a lista inteira (`TransactionsPage`).
- **O sino** (2.26) é o que falta conferir, não uma caixa de avisos: conta os lançamentos a conferir, e cada
  um sai quando é resolvido. Os eventos da carteira a conferir (I.7c2) entram no mesmo número, mas viram uma linha só no
  painel, que leva a Investimentos (onde se confere cada um): o sino não cresce com eles. Nada de "marcar todas como lidas", que deixaria a estimativa passar por valor
  real. Ele está em toda tela, então o painel dele carrega sob demanda (`lazy`), fora do pacote principal.
  Com algo a conferir, o número ganha o anel `--spectrum-conic` (o do avatar), em tinta para ler bem; o sino
  balança uma vez quando a contagem cresce, nunca ao abrir a tela nem em repetição. O último número fica guardado
  fora do componente enquanto a página está aberta: o "Novo lançamento" não tem cabeçalho, e o sino que volta dele
  precisa saber que o número cresceu. É a única consulta que volta ao servidor ao ganhar o foco (o débito nasce
  sozinho, com o app aberto); sem consulta periódica, decisão do dono.
- **Renda fixa na tela** (`docs/renda-fixa.md`, seção 7): a aba "Objetivos" de Investimentos (`?aba=objetivos&objetivo=<id>|sem`) e o
  grupo "Renda fixa" da Carteira. Nunca "cofrinho": "objetivo" e "aporte". O rendimento é `text-spectrum` e o imposto, tinta;
  a chave **Bruto | Líquido estimado** vale para a lista e o detalhe, o gráfico é sempre bruto (e diz isso). Toda tela que mostra
  esses números leva a nota "Estimativa; o extrato do banco manda" com a data da última taxa, o aviso do que ainda não existe
  (só CDB, LCI e LCA, em % do CDI, CDI + taxa ou pré-fixado, sem IPCA nem projeção; os resgates têm tela: botão "Resgatar" em cada aporte, "Selecionar" na lista para resgatar vários de uma vez (a linha vira caixa de seleção, o quadro "Resgatar N aportes" fica dentro da seção, nunca colado embaixo por causa da `BottomNav`; o painel do lote lista os aportes do mais antigo ao mais novo, e "Resgatar tudo" é um modo que acompanha o saldo da data), "Resgatados", linha "Já resgatado" com o líquido e o imposto pago, e a prévia do imposto vem da API, nunca refeita na tela, R.25) e, no líquido, a explicação do imposto de cada produto que há na lista; as letras mostram "Isento de IR" e, no líquido, a explicação do imposto; "taxas defasadas" e "incompletas" têm
  texto próprio. Nunca "lucro" nem "você ganhou" para o líquido.
- **Resumo × Análise:** o Resumo (`/`) é de relance (destaque, Entrou/Saiu/Investido, Hoje); o que
  explica o mês vai na Análise (`/analise`), e o que olha para a frente, na seção "Daqui para frente"
  dela. Bloco novo entra na tela da pergunta que responde, não no fim do Resumo. O `?mes=` passa
  entre as telas (`withMonth`).
- **Página de apresentação** (`docs/landing.md`): as mesmas peças e tokens do app; o movimento pode encenar (D8). O app
  aparece por vídeos e números gravados de um usuário de exemplo (nunca dado real); mudou muito uma tela mostrada,
  grava-se de novo. Toda conta que a página mostra (fatura, parcelas, rateio, preço médio) sai de `demoMath.ts`, com
  teste contra as regras do domínio.
- **E-mails** (`EmailLayout`, `docs/email.md`, seção 4): cabeçalho em tinta com o logo (PNG servido pelo domínio) e a
  faixa do espectro em células, título serifado, botão em tinta, quadro de dados e nota. Tabelas e estilos em linha,
  cores em hexadecimal: leitor de e-mail não é navegador. Texto novo ou mudança de layout se confere pela prévia
  (`EmailPreviewTests`) em tela estreita e larga, claro e escuro.
- **Telas de erro:** `ErrorScreen` com a cena `BrokenPrism` (espectro desalinhado que se
  recompõe ao passar o ponteiro na ação), para erro inesperado (`ErrorBoundary`), versão nova
  publicada com o app aberto e endereço inexistente. Ficam no pacote principal, porque precisam
  aparecer quando o resto não carrega. Em desenvolvimento: `/dev/erro` e
  `/dev/erro?tipo=atualizacao`.
- **Sessão expirada:** o primeiro 401 de qualquer consulta ou mutação leva a `/entrar`
  (`createQueryClient`, em `lib/queryClient.ts`); consultas não repetem erro 4xx.
- **Cache de dados:** o TanStack Query é o único cache (nada de Redux nem de dados financeiros no
  `localStorage`). Respostas valem 60 s (`freshFor`); por isso **toda mutação precisa invalidar
  as chaves que afeta** (`invalidateMoney`, `lib/queryKeys.ts`), senão a tela fica com número velho.

## 8. Segurança

- Senha pelo hasher do Identity. Não implemente hash à mão.
- Sessão em cookie `httpOnly` + `Secure` + `SameSite=Lax`. **Nunca JWT em localStorage.**
- Rate limit em login, cadastro, confirmação de e-mail, senha nova e apagar a conta, por cliente (o `X-Real-IP`,
  seção 7). Cinco senhas erradas (no login, ao confirmar o e-mail, ao trocar a senha ou ao apagar a conta, na mesma
  contagem) bloqueiam a conta por 15 minutos; só somam as que vêm até 15 minutos depois da anterior (`AppUserManager`, H.4e). A senha nova pelo e-mail desbloqueia.
  "Trocar senha" pelo menu da conta pede a atual; esta sessão fica e as outras caem (H.4d).
- **Conta (`docs/email.md`, 5.1 a 5.5):** sem o e-mail confirmado, não entra. Nenhuma resposta conta quem tem conta
  (cadastro, "esqueci a senha" e reenvio respondem igual; o login só revela "falta confirmar" a quem acertou a senha).
  Confirmar pede a senha do cadastro (contra o pré-sequestro de conta); a senha nova pelo e-mail confirma a conta e
  tira o intruso. Os links valem uma vez (o `SecurityStamp` muda), vencem (3 dias e 1 hora), levam o token depois do
  `#` e nunca são gravados nem logados. A sessão confere o `SecurityStamp` a cada 5 min: senha trocada derruba os
  outros aparelhos.
- **Apagar a conta** (A.3): pelo menu da conta, com a senha (errada conta para o bloqueio); tudo da pessoa sai na hora,
  sem "Desfazer", e o id fica 90 dias em `deleted_accounts` para reaplicar depois de restaurar um backup
  (`docs/operacao.md`, 5.6). O evento `AccountDeleted` leva o `userId`: é a lista de reserva se o banco se perder.
- Nunca logar valor, descrição de transação, nome, e-mail, senha ou token: só ids, tipos e motivos. Um
  teste de arquitetura recusa evento de `AppLog` com parâmetro de nome sensível.
- **LGPD:** as skills `lgpd-*` (`.claude/skills`, locais, fora do git; goul4rt/lgpd-skills) são consultivas e
  não substituem advogado. A v1 da política e dos termos foi ao ar antes da revisão jurídica, por decisão do dono
  (2026-10-14); a revisão segue pendente (`PLAN.md`, A.2). O texto vive em `src/prisma-web/src/legal/*.md` (sem as notas
  internas, que ficam nos rascunhos em `.lgpd/`, fora do git) e a versão vigente em `LegalTerms`: mudança que pede novo
  aceite sobe a versão nos dois lugares (um teste confere), e quem aceitou a anterior vê a tela de aceite ao entrar.
  Pedido de titular e incidente de segurança têm roteiro (`docs/operacao.md`, seções 9 e 10); o registro das operações
  (art. 37) e a avaliação do legítimo interesse ficam em `.lgpd/`. Tabela nova com `user_id` entra também na consulta
  da cópia (`ops/privacy/export-user.sql`).
  Página para quem não entrou não carrega script, fonte nem imagem de terceiros e não grava cookie.
- Segredos via User Secrets em desenvolvimento e variáveis de ambiente em produção.
  Nada de connection string ou client secret commitado.
- **Superfície de admin (R.15, `docs/admin-mercado.md`):** toda rota `/admin` leva a política `Admin`, no grupo (`AdminModule`): e-mail confirmado e presente em `Admin:Emails` (variável de ambiente `Admin__Emails__0`; sem diferenciar maiúsculas; lista vazia = ninguém é admin; entrada que não parece e-mail impede a API de subir), conferido no banco a cada pedido (`AdminAuthorizationHandler`). Os `POST` têm limite de taxa por pessoa (`admin-sync`, 6 por minuto), teto de janela (10 anos) e série validada numa lista fechada; o pedido vai ao log (`MarketDataSyncRequested`, só ids e tipos). Um teste recusa rota `/admin` sem a política (`AdminMarketDataApiTests`). `isAdmin` vai em `/auth/me` só para a tela decidir se mostra a entrada. A tela `/admin` (`features/admin`, lazy) só abre para quem é admin (`RequireAdmin`; para os outros é o 404 de sempre), com a entrada "Dados de mercado" no menu da conta. Nada de dado de usuário passa por ela, e a lista de admins nunca entra no repositório.
- **O repositório é público.** Falha de segurança ainda aberta não se descreve em nada versionado (`PLAN.md`, `docs/`,
  comentário, commit, PR): o relatório da auditoria (2026-10-15), os cenários e o desenho de cada correção ficam em
  `.security/` (fora do git, como o `.lgpd/`), e a lista viva do que falta é o `.security/pendencias.md`. O `PLAN.md`
  só diz o que cada etapa faz (seção "Segurança", S.2 a S.8); depois de corrigida, a falha pode entrar lá com o histórico.

---

## 9. Protocolo de trabalho neste repositório

1. Leia o `PLAN.md` e identifique a **primeira etapa não marcada**.
2. Leia o `docs/fase-N.md` correspondente à fase dessa etapa.
3. Execute **apenas essa etapa**. Não avance para a próxima.
4. Escreva os testes indicados no "Pronto quando" da etapa, junto com a implementação.
5. Rode `dotnet test`. Tudo verde.
6. Marque a checkbox no `PLAN.md`.
7. Pare e reporte o que foi feito, aguardando revisão.

Não refatore fora do escopo da etapa atual. Se uma decisão de produto não estiver clara
no `PLAN.md` ou no `docs/fase-N.md`, **pergunte em vez de assumir**.
