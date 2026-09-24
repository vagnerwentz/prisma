# PLAN.md — Prisma

Mapa do projeto. Detalhamento da fase em execução fica em `docs/fase-N.md`.
Convenções permanentes ficam em `CLAUDE.md`.

> **Idioma:** código em inglês, documentação em português, interface e mensagens de
> erro em pt-BR. Glossário do domínio na seção 1 do `CLAUDE.md`.

---

## Visão

Controle financeiro pessoal para o contexto brasileiro. O diferencial não é categorizar
gastos, coisa que todo app faz: é **reduzir a fricção de registrar** e **identificar de
fato onde o dinheiro foi**, inclusive quando a fatura mostra `PG *TON JOSE M` em vez do
nome do estabelecimento.

Uso primário no celular, lançando na hora da compra. Cadastro aberto: cada usuário tem
seus próprios dados, sem conta compartilhada ou grupo familiar.

---

## Decisões tomadas

Estas estão fechadas. Não reabrir sem conversa explícita.

| Tema | Decisão |
|---|---|
| Backend | .NET 10 + ASP.NET Core Minimal APIs + EF Core + PostgreSQL |
| Frontend | React + TypeScript + Vite (escolhido pelo uso no celular e pelo PWA) |
| Arquitetura | Vertical Slice + domínio rico isolado + CQRS leve |
| Auth | ASP.NET Core Identity, cookie `httpOnly`; Google OAuth adiado |
| Idioma do código | Inglês |
| Visão do dashboard | **Caixa**: gasto do cartão conta no mês do vencimento da fatura |
| Dinheiro | `long` em centavos, sempre |
| Multiusuário | Dados por `UserId`, sem grupo familiar |
| Investimentos | Fase 1 registra aporte como transferência; rentabilidade fica para depois |
| Hospedagem | Adiada. Roda local via Docker; decisão quando o produto existir |

---

## Fases

| Fase | Objetivo | Detalhe |
|---|---|---|
| **0** | Fundação: solução, Docker, Postgres, testes de arquitetura | concluída; as etapas no próprio `PLAN.md` bastaram |
| **1** | Núcleo: auth, contas, categorias, transações, parcelamento, transferências | `docs/fase-1.md` |
| **2** | Dashboard: receitas, despesas, sobra, investido, por categoria, comparativo mensal | a escrever |
| **3** | Entrada inteligente: QR Code da NFC-e, CNPJ/CNAE, merchants, regras que aprendem | a escrever |
| **4** | Importação: OFX, fatura do Itaú em PDF, deduplicação | a escrever |
| **5** | PWA instalável, orçamentos por categoria, metas, E2E com Playwright | a escrever |

Cada `docs/fase-N.md` é escrito **quando a fase começa**, não antes. Detalhar a Fase 4
hoje seria adivinhação: as decisões dela dependem do que a Fase 3 revelar sobre dados
reais.

---

## Protocolo de execução

1. Pegue a **primeira etapa não marcada** abaixo.
2. Leia o `docs/fase-N.md` da fase correspondente.
3. Implemente **somente essa etapa**, com os testes do "Pronto quando" na mesma etapa.
4. `dotnet test` verde.
5. Marque a checkbox.
6. **Pare e reporte.** Não emende a próxima etapa.

Uma etapa que crescer demais deve ser quebrada em duas, com aviso antes.

---

## Fase 0 — Fundação

- [x] **0.1 Solução e projetos**
  `Prisma.Domain`, `Prisma.Api`, `Prisma.Domain.Tests`, `Prisma.Api.Tests`,
  `Prisma.Architecture.Tests`, com as referências corretas entre eles.
  *Pronto quando:* `dotnet build` e `dotnet test` passam com a solução vazia.

- [x] **0.2 Docker e Postgres**
  `docker-compose.yml` com PostgreSQL 17 e volume persistente. `AppDbContext`
  conectando, endpoint `/health` respondendo.
  *Pronto quando:* `docker compose up -d` sobe o banco e `/health` retorna 200.

- [x] **0.3 Testes de arquitetura**
  NetArchTest verificando que `Prisma.Domain` não referencia EF Core, ASP.NET Core nem
  Npgsql, e que nenhum tipo fora de `IClock` usa `DateTime.Now`/`UtcNow`.
  *Pronto quando:* os testes passam e falham de verdade ao introduzir uma violação
  proposital (verifique isso antes de marcar).

---

## Fase 1 — Núcleo

Detalhamento completo em `docs/fase-1.md`.

- [x] **1.1 Value Object `Money`** — *teste antes da implementação*
  *Pronto quando:* teste de propriedade (CsCheck) prova que `SplitInto(n)` somado
  devolve o valor original para qualquer valor e qualquer n ≥ 1; exemplos
  R$ 100,00/3x → 3334+3333+3333 e R$ 0,01/2x → 1+0 passam.

- [x] **1.2 `IClock` e fuso**
  *Pronto quando:* teste prova que uma compra às 23h30 em `America/Sao_Paulo` fica no
  mesmo dia, e o relógio é substituível nos testes.

- [x] **1.3 Identity: cadastro e login com e-mail e senha**
  *Pronto quando:* integração cobre cadastro, login com cookie, login com senha errada
  e rate limit disparando.

- **1.4 Google OAuth** — *adiada por decisão de produto, sem checkbox de propósito para
  não travar o protocolo. Ver "Pendências de produto".*

- [x] **1.5 `Account` (CRUD)**
  Checking, CreditCard, Cash e Investment.
  *Pronto quando:* integração cobre criar, editar, listar e excluir; cartão sem
  `ClosingDay` é rejeitado com mensagem em pt-BR.

- [x] **1.6 `Category` e subcategorias, com seed padrão brasileiro**
  *Pronto quando:* novo usuário nasce com o conjunto padrão; editar e excluir funcionam;
  excluir categoria com subcategorias é bloqueado; nome repetido no mesmo nível (mesmo pai
  e tipo, sem diferenciar maiúsculas) é recusado. O bloqueio por transações vinculadas foi
  para a 1.8, porque a tabela de transações só nasce lá.

- [x] **1.7 `StatementCalculator` e entidade `Statement`** — *teste antes da implementação*
  *Pronto quando:* passam os casos de véspera, dia do fechamento, dia seguinte,
  fechamento dia 31 em mês de 30 dias e em fevereiro, e datas editadas manualmente
  sobrescrevendo o cálculo automático.

- [x] **1.8 Transações simples (receita e despesa; Pix, débito, dinheiro)**
  *Pronto quando:* CRUD completo; `SettlementDate` igual a `PurchaseDate`; soft delete
  some da listagem e é restaurável. Excluir categoria (ou subcategoria) com transações
  vinculadas é bloqueado, com mensagem sugerindo realocar antes (`docs/fase-1.md`, 2.4). Excluir
  conta com transações também é bloqueado.

- [x] **1.9a Despesa no cartão e parcelamento: criar e excluir**
  *Pronto quando:* compra em 10x gera 10 transações com `SettlementDate` no `Statement`
  certo de cada mês; faturas são criadas na primeira compra do ciclo e reaproveitadas nas
  seguintes; excluir a compra inteira remove todas as parcelas;
  `GET /accounts/{id}/statements` lista as faturas do cartão com o total de cada uma.

> **Ordem alterada:** a 1.12 foi antecipada para antes da 1.9b, a pedido, para ver o produto
> funcionando cedo. O backend já cobre o critério dela. A numeração ficou a original.

- [x] **1.12a Frontend: base e autenticação**
  Vite + React + TypeScript, Tailwind, shadcn/ui, TanStack Query, React Router, tipos gerados
  do OpenAPI (`npm run gen:api`). Telas de cadastro e login; rota protegida.
  *Pronto quando:* `npm run dev` sobe o frontend com proxy para a API; `npm run gen:api`
  gera os tipos; cadastro, login e logout funcionam pela tela, com os erros da API em pt-BR;
  rota protegida sem sessão leva ao login; `npm run build` passa sem erro de tipo.

- [x] **1.12b Frontend: lista de transações**
  *Pronto quando:* lista do mês agrupada por dia, valor em destaque; as parcelas de uma
  compra aparecem numa linha só, com o total e o número de parcelas (todas têm a mesma
  data da compra, então listá-las uma a uma repetiria a compra 10 vezes no mesmo dia);
  formatação de dinheiro (centavos → "R$ 1.234,56") e de datas coberta por teste.

- [x] **1.12c Frontend: lançamento rápido**
  *Pronto quando:* formulário mobile-first (valor, conta, categoria, data já com hoje),
  parcelas quando a conta for cartão; conversão do valor digitado para centavos coberta por
  teste; dá para cadastrar, logar, lançar uma despesa parcelada no cartão e vê-la na lista,
  tudo pelo celular (HTTPS na rede local, por causa do cookie `Secure`).
  Inclui uma tela de contas (listar e criar), sem a qual um usuário novo não teria onde lançar.

- [ ] **1.9b Despesa no cartão e parcelamento: editar**
  *Pronto quando:* editar a parcela 3 não altera as demais; alterar o valor total ou o
  número de parcelas redistribui sem perder centavo; `PATCH /statements/{id}`: editar as
  datas de uma fatura recalcula o `SettlementDate` das transações dela.

- [ ] **1.10 Transferências entre contas**
  Inclui pagamento de fatura e aporte em investimento.
  *Pronto quando:* teste prova que transferência não entra em receita nem despesa, que
  as duas pontas se mantêm consistentes e que excluir uma trata a outra.

- [ ] **1.11 Isolamento multiusuário**
  *Pronto quando:* integração com dois usuários prova que nenhum endpoint vaza dado do
  outro, inclusive em consulta por id direto (deve dar 404, não 403).

---

## Fases 2 a 5

Escopo em uma linha cada, para orientar decisões sem antecipar detalhe.

- **Fase 2 — Dashboard.** Resumo mensal por `SettlementDate`: receitas, despesas, sobra,
  investido, gastos por categoria, comparativo com meses anteriores, próximas faturas.
  Consultas com projeção direta, sem carregar entidades.
- **Fase 3 — Entrada inteligente.** Leitura do QR Code da NFC-e, extração do CNPJ pela
  chave de acesso, consulta de nome fantasia e CNAE, entidade `Merchant` normalizada e
  `CategorizationRule` que aprende com as correções do usuário.
- **Fase 4 — Importação.** Port `IStatementParser` com adapters para OFX e para a fatura
  do Itaú em PDF (PdfPig), mais deduplicação contra lançamentos manuais. Fixtures
  anonimizadas versionadas.
- **Fase 5 — Produto.** PWA instalável, orçamentos por categoria, metas, exportação em
  CSV e testes E2E com Playwright.

---

## Pendências de produto

Decidir quando a fase correspondente chegar:

- Envio de fatura para LLM externo é opt-in explícito do usuário? (Fase 4)
- Acompanhar rentabilidade de investimento ou só aporte? (Fase 5 ou depois)
- Onde hospedar e qual orçamento mensal. (antes de abrir cadastro público)
- Política de privacidade e termos de uso, por causa da LGPD. (antes de abrir cadastro público)
- Confirmação de e-mail no cadastro. Hoje o cadastro loga direto, sem confirmar; entra junto
  com o envio de e-mail (recuperação de senha). (antes de abrir cadastro público)
- Rate limit por IP depende do IP real do cliente: atrás de proxy reverso, configurar
  `ForwardedHeaders`, senão todos os usuários dividem a mesma cota. (ao decidir a hospedagem)
- Contador de senhas erradas não expira: o Identity só zera no login certo ou ao bloquear, então
  erros espalhados no tempo acumulam. Proposta: janela de 15 min com coluna
  `last_failed_login_at` e `IClock`, que não reduz a proteção contra força bruta. (antes de abrir cadastro público)
- Login com Google (antiga etapa 1.4), adiado. Critério original: entrar por e-mail e depois
  por Google cai na mesma conta. Atenção: sem confirmação de e-mail, juntar contas pelo
  e-mail permite pré-sequestro de conta. Regra proposta: só juntar se o Google marcar o
  e-mail como verificado; se a conta local nunca confirmou o e-mail, remover a senha e trocar
  o `security_stamp` ao juntar. (quando o login social voltar à pauta)
- Em produção, a API precisa ficar sob `/api` (ex.: `UsePathBase`) para não colidir com as
  rotas do SPA servido no mesmo domínio (`/transactions` é rota da API e da tela). Em
  desenvolvimento o proxy do Vite remove o prefixo. (ao decidir a hospedagem)
- Estorno no cartão (crédito que abate a fatura). Hoje o cartão aceita só despesa. (Fase 4,
  quando a importação de fatura trouxer estornos, ou antes se fizer falta)
