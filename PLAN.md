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
| Hospedagem | Uso próprio: Railway, com API, React e Postgres num só projeto e o React servido pela API (mesmo domínio). Cadastro público: reavaliar antes de abrir |

---

## Fases

| Fase | Objetivo | Detalhe |
|---|---|---|
| **0** | Fundação: solução, Docker, Postgres, testes de arquitetura | concluída; as etapas no próprio `PLAN.md` bastaram |
| **1** | Núcleo: auth, contas, categorias, transações, parcelamento, transferências | `docs/fase-1.md` |
| **2** | Dashboard: receitas, despesas, sobra, investido, por categoria, comparativo mensal | `docs/fase-2.md` |
| **H** | Hospedagem para uso próprio: build de produção e deploy no Railway | etapas no próprio `PLAN.md` |
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

- [x] **1.9b Despesa no cartão e parcelamento: editar**
  *Pronto quando:* editar a parcela 3 não altera as demais; alterar o valor total ou o
  número de parcelas redistribui sem perder centavo; `PATCH /statements/{id}`: editar as
  datas de uma fatura recalcula o `SettlementDate` das transações dela.

- [x] **1.13 Identidade visual e lançamento rápido v2** — *feita fora de ordem, a pedido*
  Identidade "Prisma" (espectro no lugar de verde e vermelho), categorias padrão com ícone e cor,
  logos de marca, modo escuro, lançamento rápido com grade de categorias e botões, telas sob
  demanda. Regras em `CLAUDE.md`, seção 7.1.
  *Pronto quando:* telas conferidas por captura nos modos claro e escuro; pacote inicial não
  maior que o de antes do redesign; testes de reconhecimento de marca e de categorias padrão.

- [x] **1.14 Frontend: editar e excluir** — *combinada antes da 1.10*
  O backend já permite; falta a tela. Tocar num lançamento abre um painel com detalhes,
  "Editar" e "Excluir"; excluir mostra aviso com "Desfazer" (restauração). Compra parcelada:
  painel com as parcelas e edição da compra inteira (total e número de parcelas); parcela
  isolada edita só descrição e categoria (`docs/fase-1.md`, 2.2).
  *Pronto quando:* dá para editar um lançamento simples, editar uma compra parcelada, excluir
  e desfazer, tudo pela tela, conferido por captura nos modos claro e escuro.
  *Feito:* o "Desfazer" da compra parcelada exigiu `POST /installment-purchases/{id}/restore`
  (`docs/fase-1.md`, 2.2), que volta só as parcelas excluídas junto com a compra.

- [x] **1.14b Mudar a data de compra no cartão**
  Compra à vista e compra parcelada no cartão passam a aceitar nova data de compra: cada parcela
  é recalculada para a fatura do seu ciclo a partir da nova data, com `SettlementDate` igual ao
  vencimento dessa fatura; os valores não mudam. Recusado se alguma parcela estiver em fatura
  paga. Parcela isolada continua sem mudar a data (as parcelas compartilham a data da compra).
  *Pronto quando:* testes de domínio provam a compra que muda de fatura, a que continua na mesma,
  as parcelas se deslocando juntas, a soma inalterada e o bloqueio por fatura paga; a data é
  editável pela tela nos dois casos.

- [x] **1.15 Frontend: contas e cartões**
  Detalhe do cartão com as faturas e edição de datas; editar e desativar contas; criar conta
  em painel.
  *Feito:* a tela da fatura lista as compras dela, com `GET /transactions?statementId=`
  (`docs/fase-1.md`, 3). Regras de "fatura atual" e status na mesma seção.

- [x] **1.10 Transferências entre contas**
  Inclui pagamento de fatura e aporte em investimento.
  *Pronto quando:* teste prova que transferência não entra em receita nem despesa, que
  as duas pontas se mantêm consistentes e que excluir uma trata a outra.
  *Decisões (com o usuário):* pagamento só pelo total; compra que cairia em fatura paga é
  recusada; cartão só recebe dinheiro pelo "Pagar fatura". Regras derivadas em
  `docs/fase-1.md`, 2.3. Tela: "Transferência" no lançamento rápido, "Pagar fatura" e
  "Desfazer pagamento" na fatura.

- [x] **1.11 Isolamento multiusuário**
  *Pronto quando:* integração com dois usuários prova que nenhum endpoint vaza dado do
  outro, inclusive em consulta por id direto (deve dar 404, não 403).

- [x] **1.16 Saldo das contas** — *pedida pelo usuário depois da 1.11*
  A tela de contas mostrava só o saldo inicial: pagar fatura ou receber transferência não mudava
  nada. Saldo atual e previsto nas contas; "a pagar" e limite disponível no cartão
  (`docs/fase-1.md`, 2.5). Só consulta, sem mudança no banco.
  *Pronto quando:* testes de domínio para o sinal de cada tipo de transação, o corte por data e o
  limite disponível; integração com o exemplo da regra 2.5; a lista e o detalhe da conta mostram
  os valores, conferidos nos modos claro e escuro.

---

## Fase 2 — Dashboard

Cada etapa entrega API e tela juntas, para o resumo crescer à vista.

- [x] **2.1 Resumo do mês como tela inicial**
  `GET /dashboard/summary`: receitas, despesas, sobra e investido por `SettlementDate`
  (`docs/fase-2.md`, 2.1). A tela Resumo passa a ser `/`, com navegação por mês; a lista de
  lançamentos vai para `/lancamentos`.
  *Pronto quando:* testes de domínio (antes) e de integração com o exemplo da seção 2; pagamento
  de fatura não duplica o gasto; despesa excluída some e volta ao restaurar; dois usuários não se
  enxergam; tela conferida nos modos claro e escuro.

- [x] **2.2 Gastos por categoria**
  `GET /dashboard/categories`: despesas do mês pela categoria raiz, "Sem categoria" à parte
  (`docs/fase-2.md`, 2.2). Barras na cor da categoria; tocar abre a lista filtrada.
  *Pronto quando:* integração com as categorias do exemplo; participação arredondada coberta
  por teste no frontend; tela conferida nos dois modos.

- [x] **2.3 Comparativo mensal**
  `GET /dashboard/history`: 6 meses terminando no escolhido (`docs/fase-2.md`, 2.3). Gráfico
  de barras com Recharts, carregado sob demanda; variação das despesas contra o mês anterior.
  *Pronto quando:* testes de domínio para meses vazios e virada de ano; variação percentual
  coberta no frontend; pacote principal sem o Recharts; tela conferida nos dois modos.

- [x] **2.4 Próximas faturas e saldo em contas**
  `GET /dashboard/upcoming-statements`: a próxima fatura não paga de cada cartão ativo; "Em
  contas" soma os saldos da 1.16 (`docs/fase-2.md`, 2.4).
  *Pronto quando:* integração com fatura paga, fatura zerada e cartão inativo ficando de fora;
  tela conferida nos dois modos.

- [x] **Revisão antes da Fase 3** (pedida pelo usuário)
  Sessão expirada volta ao login; tela de erro, de versão nova e 404; consultas não repetem
  erro 4xx; pagamento de fatura em duplicidade barrado com 409 (token `xmin`); exceção não
  tratada vira ProblemDetails em pt-BR; `.DS_Store` fora do Git.
  *Pronto quando:* teste de corrida do pagamento e testes do 401 e das respostas de erro;
  telas conferidas nos dois modos e em 320, 390 e 1280px.

- [x] **2.5 Estorno: regras e API** (pedida pelo usuário)
  Tipo `Refund`, que abate despesa e nunca é receita; no cartão, cai na fatura aberta na data do
  estorno; vínculo opcional com a compra, limitado ao valor dela; fatura negativa é saldo a favor;
  resumo, categorias, comparativo e saldos líquidos (`docs/fase-2.md`, 2.5).
  *Pronto quando:* testes de domínio escritos antes, com os valores do exemplo da 2.5; integração
  com o exemplo inteiro, limite do vínculo, fatura paga, restauração e isolamento.

- [x] **2.6 Estorno: telas**
  "Estorno" em `/lancar`, "Estornar" no painel da despesa, estorno na lista e na fatura, saldo a
  favor e aviso das categorias escondidas (`docs/fase-2.md`, 4).
  *Pronto quando:* "Estornar" preenchido e saldo a favor cobertos no Vitest; telas conferidas nos
  dois modos e em 320, 390 e 1280px.

- [x] **2.7 Lançar de novo** (pedida pelo usuário, depois da Fase 2)
  Repetir um lançamento pelo painel ("Lançar de novo") ou pelo aviso de "salvo", com o `/lancar`
  preenchido e editável, data de hoje; sem requisição (`docs/fase-1.md`, 5.1). O valor preenchido
  é substituído pelo primeiro dígito digitado (vale também para estorno e edição). Ações
  secundárias ganham cor por faixa do espectro.
  *Pronto quando:* Vitest cobre o preenchimento (compra parcelada com total e parcelas, conta
  desativada, receita que cairia no cartão, estorno fora); telas conferidas nos dois modos e em
  320px.

- [x] **2.8 Compromissos herdados** (pedida pelo usuário, depois da Fase 2)
  No Resumo, "Parcelas de compras anteriores" separa do "Saiu" do mês as parcelas 2+ de compras
  feitas antes (`GET /dashboard/inherited`), e "Já comprometido" mostra o "Saiu" já lançado dos 6
  meses seguintes ao de hoje, com o mês da última parcela (`GET /dashboard/committed`)
  (`docs/fase-2.md`, 2.6). Resumo, comparativo e os dois blocos passam a usar as mesmas consultas
  agrupadas (`DashboardEntries`).
  *Pronto quando:* testes de domínio escritos antes, com o exemplo da 2.6; integração com o exemplo
  inteiro (pagamento de fatura e compra excluída de fora) e isolamento; Vitest da participação, das
  barras e dos textos; telas conferidas nos dois modos e em 320, 390 e 1280px.

---

## Hospedagem para uso próprio

Antes da Fase 3, a pedido: usar o Prisma no dia a dia exige que ele abra fora de casa, com
HTTPS. Só para o próprio usuário: o cadastro fica fechado, e LGPD, termos e confirmação de
e-mail continuam nas pendências, para quando o cadastro abrir.

*Decisões (com o usuário):*
- **Railway** para tudo: um serviço com a API e um Postgres no mesmo projeto, na mesma rede
  privada. Plano Hobby (US$ 5/mês com US$ 5 de uso incluído); estimativa de US$ 5 a 10/mês.
- **O React é servido pela própria API**, no mesmo domínio, como o `CLAUDE.md` já decidia.
  Cloudflare Pages foi descartado: com front e API em domínios diferentes (`*.pages.dev` e
  `*.up.railway.app`), o cookie `SameSite=Lax` não vai nas chamadas, e o Safari bloqueia
  cookie de terceiros; exigiria CORS e cookie `SameSite=None`, que o `CLAUDE.md` não admite.
  Servir os estáticos pela API não custa nada a mais.
- **Supabase descartado por ora:** a Auth dele duplicaria o Identity, e o plano grátis pausa o
  banco inativo e não tem backup. Com a API no Railway (sem região no Brasil) e o banco no
  Supabase (São Paulo), cada consulta atravessaria o continente: o resumo faz várias.
- **Terraform não entra:** um serviço e um banco não pagam a ferramenta. O Dockerfile e a
  configuração versionados bastam; reavaliar com mais de um ambiente.
- **Domínio próprio é opcional:** o endereço `*.up.railway.app` já tem HTTPS. Com domínio, o
  DNS pode ficar no Cloudflare (grátis).

- [x] **H.1 Build de produção**
  `Dockerfile` em estágios: build do React (Node), `dotnet publish` e imagem final só com o
  runtime, com o `dist` do React em `wwwroot`. A API passa a servir os estáticos e a rota de
  fallback do SPA; os endpoints ficam sob `/api` (`UsePathBase`), separados das rotas das telas
  (hoje em português, mas nada impede uma coincidência como `/accounts`), e o proxy do Vite deixa
  de remover o prefixo.
  `ForwardedHeaders` para o IP e o esquema reais atrás do proxy do Railway (o rate limit do
  login depende do IP). Chaves do Data Protection persistidas (volume ou banco), senão cada deploy
  derruba a sessão. Migrations aplicadas no deploy. Cadastro fechado por configuração
  (`/register` recusado em produção, salvo e-mail liberado). Log sem valor, descrição, e-mail
  nem token, como na seção 8 do `CLAUDE.md`.
  *Pronto quando:* a imagem sobe local com `docker run` apontando para o Postgres do
  `docker compose`; integração prova o `/api`, o fallback do SPA (rota de tela devolve o
  `index.html`, rota de API inexistente devolve 404 em JSON), o IP do `X-Forwarded-For` no rate
  limit, o cadastro fechado e a sessão sobrevivendo a um reinício do contêiner.
  *Feito:* conferido também com a imagem real num banco vazio (13 migrations aplicadas ao subir,
  cadastro fechado com e-mail liberado, sessão válida depois de `docker restart`). Rota
  inexistente da API sem sessão responde 401, como antes: o 404 só aparece com login. O cookie de
  sessão ficou fixo no caminho `/`: herdando o `/api`, convivia com o cookie antigo e a sessão caía
  logo depois do login (achado pelo usuário no teste pela tela).

- [x] **CI no GitHub Actions** (pedida pelo usuário, antes da H.2)
  `.github/workflows/ci.yml`: a cada push na `main` e a cada pull request para ela, o backend roda
  `dotnet test` (a integração sobe o próprio Postgres com Testcontainers, no Docker do runner) e o
  frontend roda lint, Vitest e build, em paralelo.
  *Pronto quando:* a primeira execução no GitHub fica verde.

- [ ] **H.2 Deploy no Railway**
  Projeto com o serviço da API (build pelo `Dockerfile`, deploy a cada push na `main` só com o CI
  verde: "Wait for CI") e o
  Postgres do Railway; connection string montada a partir das variáveis do Postgres (as chaves
  do cookie já vão no banco, sem volume); seu e-mail em `Registration__AllowedEmails__0`; porta
  8080; health check em `/api/health`. Backup diário do
  banco (o do Railway, se o plano incluir; senão `pg_dump` agendado para fora dele) e um teste
  de restauração. Instruções no `CLAUDE.md`, seção 7.
  *Pronto quando:* o app abre pelo celular fora da rede de casa, com HTTPS; login, lançamento e
  resumo funcionam; um deploy novo não derruba a sessão; um backup foi restaurado num banco
  local e conferido.

---

## Fases 3 a 5

Escopo em uma linha cada, para orientar decisões sem antecipar detalhe.

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
- Hospedagem para cadastro público: o Railway basta, ou os dados devem ficar numa região no
  Brasil por causa da LGPD? Qual orçamento mensal? (antes de abrir cadastro público)
- Política de privacidade e termos de uso, por causa da LGPD. (antes de abrir cadastro público)
- Confirmação de e-mail no cadastro. Hoje o cadastro loga direto, sem confirmar; entra junto
  com o envio de e-mail (recuperação de senha). (antes de abrir cadastro público)
- Contador de senhas erradas não expira: o Identity só zera no login certo ou ao bloquear, então
  erros espalhados no tempo acumulam. Proposta: janela de 15 min com coluna
  `last_failed_login_at` e `IClock`, que não reduz a proteção contra força bruta. (antes de abrir cadastro público)
- Login com Google (antiga etapa 1.4), adiado. Critério original: entrar por e-mail e depois
  por Google cai na mesma conta. Atenção: sem confirmação de e-mail, juntar contas pelo
  e-mail permite pré-sequestro de conta. Regra proposta: só juntar se o Google marcar o
  e-mail como verificado; se a conta local nunca confirmou o e-mail, remover a senha e trocar
  o `security_stamp` ao juntar. (quando o login social voltar à pauta)
- Mudar o dia de fechamento ou de vencimento do cartão não recalcula as faturas já criadas
  (inclusive as futuras, abertas pelas parcelas). Proposta: recalcular as não editadas e não
  pagas, e o `SettlementDate` das parcelas delas; `DatesEditedManually` já permite distinguir.
  (quando fizer falta)
- Cancelar as parcelas futuras de uma compra parcelada estornada (a 2.5 faz só o crédito único).
  (quando fizer falta)
