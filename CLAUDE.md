# CLAUDE.md — Prisma

> Controle financeiro pessoal para o contexto brasileiro: PIX, cartão de crédito com
> ciclo de fatura, parcelamento e nota fiscal eletrônica.
>
> **Nome de trabalho:** `Prisma`. Trocar o nome é um localizar-e-substituir no
> namespace raiz e no nome dos projetos. Nada mais depende dele.

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
| Descrição original | `RawDescription` | texto cru da importação |

---

## 2. Stack

| Camada | Tecnologia |
|---|---|
| Runtime | .NET 10 (LTS) |
| API | ASP.NET Core Minimal APIs + `Microsoft.AspNetCore.OpenApi` (documento em `/openapi/v1.json`, só em desenvolvimento) |
| Auth | ASP.NET Core Identity + cookie `httpOnly` + rate limiter nativo do ASP.NET Core + chaves do Data Protection no Postgres (`Microsoft.AspNetCore.DataProtection.EntityFrameworkCore`); Google OAuth adiado |
| ORM | EF Core 10 + Npgsql + EFCore.NamingConventions (snake_case) + EF Core Design (migrations; `dotnet-ef` fixado em `dotnet-tools.json`) |
| Banco | PostgreSQL 17 (Docker) |
| Validação | FluentValidation |
| Testes | xUnit + Shouldly + Testcontainers + NetArchTest + CsCheck + `Microsoft.AspNetCore.Mvc.Testing` (`WebApplicationFactory`) |
| Frontend | React 19 + TypeScript + Vite + React Router |
| Cliente da API | `openapi-typescript` (tipos gerados) + `openapi-fetch` (chamadas tipadas) |
| Testes do frontend | Vitest (formatação e conversão de dinheiro e data) |
| UI | Tailwind CSS + shadcn/ui + `sonner` (avisos) + Instrument Serif (`@fontsource`, fonte hospedada no app) |
| Ícones | `lucide-react` (import nominal, mapa fechado em `categoryIcons.ts`) + logos de marca gerados do `simple-icons` (devDependency, `npm run gen:brands`) |
| Estado de servidor | TanStack Query v5 |
| Formulários | React Hook Form + Zod |
| Gráficos | Recharts |

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
└── prisma-web/                 # React + Vite
tests/
├── Prisma.Domain.Tests/        # Unitários puros, sem banco
├── Prisma.Api.Tests/           # Integração por slice, com Testcontainers
└── Prisma.Architecture.Tests/  # NetArchTest
docs/
└── fase-N.md                   # Detalhamento da fase em execução
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

   **O dashboard agrega por `SettlementDate`** (visão de caixa). Decisão tomada.

5. **Transferência nunca é receita nem despesa.** Pagamento de fatura, aporte em
   investimento e movimentação entre contas geram **duas** transações ligadas por
   `TransferPairId` e são **excluídas** de todo cálculo de receita e despesa. Contar
   transferência como gasto duplica o valor do mês.

6. **Isolamento por usuário é automático.** Toda entidade tem `UserId` e o
   `AppDbContext` aplica Global Query Filter. Nunca escreva o filtro à mão e nunca use
   `IgnoreQueryFilters()` fora de seed e migration. Única exceção: restaurar registro excluído usa
   `IgnoreQueryFilters([AppDbContext.SoftDeleteFilter])`, que ignora só o filtro de exclusão.
   O filtro de dono (`OwnerFilter`) nunca é ignorado; teste de arquitetura garante.

7. **Exclusão é soft delete** (`DeletedAt` + filtro global), para permitir desfazer.

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
de membro.

**Fixtures de parser** (Fase 4): arquivos reais de OFX e de fatura em PDF,
anonimizados, com o resultado esperado versionado ao lado. Quando o banco mudar o
layout, o teste acusa antes de importar dado errado.

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
- **Concorrência:** entidade que dois pedidos simultâneos podem corromper (hoje, `Statement`:
  pagar duas vezes a mesma fatura) usa a coluna `xmin` do Postgres como token
  (`Property<uint>("Version").IsRowVersion()`), com teste de corrida.
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
dotnet run --project src/Prisma.Api

dotnet ef migrations add <Name> -p src/Prisma.Api -s src/Prisma.Api
dotnet ef database update -p src/Prisma.Api -s src/Prisma.Api

cd src/prisma-web && npm install && npm run dev    # http://localhost:5173, proxy /api → API
npm run gen:api       # tipos TS do OpenAPI; exige a API rodando (lê http://localhost:5151)
npm run dev:lan       # rede local com HTTPS, para testar no celular (aceite o certificado local)
npm test              # Vitest
npm run build         # checagem de tipos + build
npm run lint          # oxlint

docker build -t prisma .                           # imagem de produção (API + React), na raiz
```

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
imagem liga `ForwardedHeaders:Enabled` (IP real atrás do proxy) e `Database:MigrateOnStartup`. As
chaves do cookie ficam no Postgres (`data_protection_keys`): deploy não derruba a sessão. O cookie
fica no caminho `/`, nunca herdando o `/api`: dois cookies de mesmo nome em caminhos diferentes
convivem, e o ASP.NET lê o último (o velho), derrubando a sessão logo depois do login. Cadastro
fechado fora de desenvolvimento (`Registration:Open`); libere e-mails em
`Registration__AllowedEmails__0`. A connection string vem de `ConnectionStrings__Default`.

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
  viram marrom).
- **Tipografia:** `font-display` (Instrument Serif) em valores grandes e títulos; Geist no resto.
- **Marcas:** `findBrand` reconhece a marca pela descrição ou nome da conta. Logo do
  `simple-icons` quando existe; monograma na cor da marca quando a marca saiu do projeto (Amazon,
  bancos brasileiros). Nunca desenhe um logo à mão.
- **Modo escuro** segue o sistema só com CSS (`prefers-color-scheme`). Toda tela nova é
  conferida nos dois modos.
- **Desempenho:** nada de importar conjuntos inteiros de ícones; telas secundárias com `lazy`;
  confira o tamanho do pacote principal no `npm run build` ao adicionar dependência.
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
- Rate limit em login e recuperação de senha.
- Nunca logar valor, descrição de transação, e-mail ou token.
- Segredos via User Secrets em desenvolvimento e variáveis de ambiente em produção.
  Nada de connection string ou client secret commitado.

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
