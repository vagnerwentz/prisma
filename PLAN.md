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
| Auth | ASP.NET Core Identity, cookie `httpOnly`; e-mail confirmado para entrar (confirmar pede a senha), senha nova pelo e-mail (H.4); Google OAuth adiado |
| E-mail | Resend atrás de `IEmailTransport`, fila no Postgres e tarefa em segundo plano, sem broker (`docs/email.md`) |
| Idioma do código | Inglês |
| Visão do dashboard | **Caixa**: gasto do cartão conta no mês do vencimento da fatura |
| Dinheiro | `long` em centavos, sempre |
| Multiusuário | Dados por `UserId`, sem grupo familiar |
| Investimentos | O "Investido" do Resumo vem dos ativos (renda fixa inteira e ações ao custo), não do saldo das contas, e o caixa das corretoras é opcional por conta (I.9, `docs/patrimonio.md`). Aporte é transferência (Fase 1). Carteira e proventos na Fase I (`docs/investimentos.md`): provento é receita na conta onde caiu, ligada ao ativo; catálogo de ativos global, vindo da brapi. Operações e preço médio pelas notas digitadas, com os eventos da empresa (I.7); cotação fica para depois |
| Renda fixa | Objetivos ("caixas") com aportes em CDB (a % do CDI, CDI + taxa ou pré-fixado) cadastrados à mão (`docs/renda-fixa.md`): rendimento pela convenção da B3 em inteiro (`Int128`) com o CDI do Banco Central (SGS 12, tabela global `reference_rates`); imposto estimado (IOF + IR regressivo) por aporte, só como "se resgatar hoje"; com resgate total ou parcial, por aporte ou em lote (R.22 a R.31), sem projeção e sem IPCA por ora. Aporte pode sair de uma conta (cria a transferência); o Resumo e as Contas separam o Disponível do Investido, com o rendimento (R.32 a R.34). Na Carteira é um grupo só, pesado pelo aportado |
| Hospedagem | Uso próprio: Railway, com API, React e Postgres num só projeto e o React servido pela API (mesmo domínio). Cadastro público: reavaliar antes de abrir |
| Nome público | **Prisma Money** (2026-10-13): domínio `prismamoney.com.br`, Instagram `@prismamoneybrasil`. O código continua `Prisma` |
| Página de apresentação | React pré-renderizado em `/` para quem não tem sessão, só com o cadastro aberto (`docs/landing.md`) |

---

## Fases

| Fase | Objetivo | Detalhe |
|---|---|---|
| **0** | Fundação: solução, Docker, Postgres, testes de arquitetura | concluída; as etapas no próprio `PLAN.md` bastaram |
| **1** | Núcleo: auth, contas, categorias, transações, parcelamento, transferências | `docs/fase-1.md` |
| **2** | Dashboard: receitas, despesas, sobra, investido, por categoria, comparativo mensal | `docs/fase-2.md` |
| **H** | Hospedagem para uso próprio: build de produção e deploy no Railway | etapas no próprio `PLAN.md` |
| **I** | Investimentos: catálogo de ativos (brapi), carteira, proventos e logos | `docs/investimentos.md` |
| **R** | Renda fixa: objetivos, aportes em CDB/LCI/LCA (CDI, CDI+, pré-fixado), taxas do Banco Central, rendimento, imposto estimado, resgate, grupo na Carteira | `docs/renda-fixa.md` |
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

- [x] **2.9 Resumo e Análise** (pedida pelo usuário, depois da 2.8)
  O Resumo fica de relance (destaque, "Entrou / Saiu / Investido", "Hoje" e atalhos); a nova tela
  Análise (`/analise?mes=`) recebe categorias, parcelas herdadas, comparativo e "Daqui para frente";
  aba nova na barra; duas colunas a partir de 1024px (`docs/fase-2.md`, 4). Só frontend.
  *Pronto quando:* o mês passa entre as telas; a Recharts sai do carregamento do Resumo (conferido no
  `npm run build`); telas conferidas nos dois modos e em 320, 390 e 1280px.
  *Feito:* o Resumo deixou de baixar o comparativo (102 kB gzip do Recharts) ao abrir; o pacote
  principal ficou igual. "Já comprometido" virou o primeiro bloco da seção "Daqui para frente".

- [x] **2.10 Por que o gasto mudou** (pedida pelo usuário, depois da 2.8)
  Decompõe a diferença do "Saiu" para o mês anterior em motivos que somam exatamente a diferença:
  parcelas de compras anteriores e o decidido no mês por categoria, com o maior lançamento de cada
  alta (`docs/fase-2.md`, 2.7). `GET /dashboard/variation`. No topo da Análise, com uma linha no
  Resumo.
  *Pronto quando:* testes de domínio escritos antes, com o exemplo da 2.7 e a propriedade da soma
  exata; integração com o exemplo e isolamento; Vitest das frases; tela conferida nos dois modos e
  em 320, 390 e 1280px.
  *Feito:* `SpendingVariation` no domínio (11 testes, com a propriedade da soma exata em CsCheck);
  `GET /dashboard/variation` traz as despesas e os estornos dos dois meses numa consulta. Na Análise,
  bloco sob demanda (1,3 kB gzip); no Resumo, a linha substitui o atalho "Ver análise". Decisões da
  tela registradas em `docs/fase-2.md`, 4.

### Acabamento visual (pedido pelo usuário, depois da 2.10)

Revisão com as skills de design do Emil Kowalski (animação) e da Impeccable (crítica e auditoria),
instaladas só neste projeto e consultivas: a identidade da seção 7.1 do `CLAUDE.md` vence quando elas
discordam (texto em gradiente, halo e o número em destaque do Resumo ficam).

- [x] **2.11 Movimento**
  Tokens de curva (`--ease-out`, `--ease-in-out`, `--ease-drawer`). O painel de baixo sobe da borda com
  a curva de gaveta (420 ms) e sai mais rápido (260 ms). Trocar de mês mantém os números anteriores
  esmaecidos até o mês novo chegar (`placeholderData` + `StaleFade`), sem o esqueleto piscando; as
  barras correm até o valor novo (`.meter-fill`, com `clip-path`). Sem `transition-all` no botão, o
  filete do valor anima `transform`, o selo "Aberta" parou de pulsar. Menos movimento: nada se
  desloca, os painéis viram um fade curto.
  *Pronto quando:* a animação computada do painel é a nova, e o fade com `prefers-reduced-motion`;
  trocar de mês não mostra esqueleto; lint, Vitest e build verdes.

- [x] **2.12 Uma linguagem visual**
  Bloco de conteúdo é superfície (`.surface`, sem contorno) em todas as telas, não só no dashboard:
  lista de lançamentos, contas, detalhe da conta, painéis do lançamento e da fatura. Contorno fica
  para o que se preenche ou escolhe (campo, chip, seletor, botão secundário). Títulos em caixa normal
  (`text-sm font-medium`), sem maiúsculas espaçadas; o selo de status continua em maiúsculas.
  *Pronto quando:* telas conferidas nos dois modos e em 320, 390 e 1280px.

- [x] **2.13 Densidade de Lançamentos e Contas**
  O mês numa superfície só, com o dia como subtítulo (antes, um cartão por dia). Em Contas, o cartão
  mostra "Fecha 26 · vence 5", que não é mais cortado ao lado do saldo.
  *Pronto quando:* sem rolagem lateral em 320px; conferido nos dois modos.

- [x] **2.14 Troca de tema** (pedida pelo usuário)
  Botão no cabeçalho: um sol com um raio por faixa do espectro, que recolhe os raios e vira lua; o
  tema novo se abre num círculo a partir do botão (View Transitions; sem ela, ou com menos movimento,
  a troca é imediata). No menu da conta, "Seguir o sistema", "Claro" e "Escuro". O CSS passou a ler
  `data-theme` no `<html>`, aplicado antes da primeira pintura.
  *Pronto quando:* Vitest da resolução do tema e do raio da revelação; no navegador, o sistema
  escuro aplica o escuro, o botão troca e a escolha sobrevive a recarregar, e "Seguir o sistema"
  apaga a escolha.

- [x] **2.15 Autocompletar da descrição** (pedida pelo usuário, com protótipo aprovado)
  Em `/lancar`, sugestões de descrições já usadas, com categoria, conta e meio da última vez
  (`docs/fase-2.md`, 2.8). `GET /transactions/descriptions` devolve o vocabulário uma vez; o filtro e
  a ordem rodam no aparelho, sem requisição por tecla. A sugestão nunca desfaz uma escolha da pessoa
  nem preenche o valor.
  *Pronto quando:* testes de domínio escritos antes, com o exemplo da 2.8; integração com o exemplo e
  isolamento; Vitest da ordenação e do preenchimento; tela conferida pelo teclado, nos dois modos e em
  320, 390 e 1280px.
  *Feito:* `DescriptionHistory` no domínio (7 testes) e `GET /transactions/descriptions` (integração
  com o exemplo e isolamento). A consulta segue o índice `(user_id, purchase_date)` com teto de 5.000
  linhas: 28 ms para um usuário com 26,8 mil lançamentos, uma vez por sessão. No frontend, a busca é
  local (13 testes Vitest) e o pacote principal não mudou (Lançar já é carregada sob demanda). Além do
  protótipo: a sugestão também não desfaz a categoria escolhida, e a seta com a lista fechada abre já
  destacando a primeira.

- [x] **2.16 Mostrar a senha** (pedida pelo usuário)
  No login e no cadastro, um olho à direita do campo (`PasswordField`). Mostrar é o momento da marca:
  um feixe de luz branca que se decompõe no espectro inteiro atravessa o texto (650 ms, só o trecho do
  texto; no escuro ele soma luz ao fundo), as letras aparecem acesas por onde ele passa e o campo ganha
  um halo do espectro que se dissipa; o olho fechado, uma pálpebra com cílios, abre com a pupila no
  espectro e um pulso. Esconder é imediato. Ajustado com o usuário: a primeira versão, com uma faixa
  fria e desfocada, ficou fraca. O toque não tira o foco do campo (o teclado do celular fica) e o cursor fica onde estava;
  ao enviar, a senha volta a ficar oculta (gerenciador de senhas e troca de tela); sem correção
  automática com a senha visível; com menos movimento, sem animação.
  *Pronto quando:* conferido no navegador, nos dois modos: foco, cursor e valor mantidos, oculta ao
  enviar, rótulo e `aria-pressed` do botão.

- [x] **2.17 Valor com cara de campo** (pedida pelo usuário, depois de testes com outras pessoas)
  Duas pessoas passaram batido pelo valor em `/lancar`: sem contorno, sem cursor e com o rótulo só
  para leitor de tela, o "R$ 0,00" parecia texto. Agora: rótulo "Valor" visível, linha neutra sempre à
  mostra (vira o filete do espectro no foco), "R$ 0,00" mais forte, cursor fino do espectro à direita
  dos dígitos e "Toque para digitar" enquanto vazio e sem foco. "Lançar" sem valor leva o foco ao
  campo no próprio toque (no iPhone, o teclado abre) e o destaca em erro, sem bloquear o resto
  do formulário; um passo a passo que travasse o formulário foi descartado por atrasar quem já sabe
  usar. Vale para os painéis de edição, que usam o mesmo `AmountField`.
  Tempero, escolhido pelo usuário: vazio e sem foco, um feixe do espectro corre pela linha a cada
  4 s (passada de 900 ms; repetir sem parar foi escolha do usuário) para chamar o olho onde a pessoa
  passava batido; cada dígito digitado entra subindo de leve
  (160 ms). Com menos movimento, nenhum dos dois.
  *Decidido (com o usuário):* o valor fica no topo. O pai do usuário sugeriu levá-lo ao rodapé, acima
  do botão; testado lado a lado (`?valor=rodape`, já removido): o rodapé fixo ocupava ~190px (quase
  um terço da tela em 320×640), escondia que a tela rola até data e parcelas e arriscava ficar atrás
  do teclado do iPhone. A ideia dele foi para o botão: com o valor vazio, ele diz "Digite o valor"
  ("Digite o valor do estorno" no estorno) e o toque leva ao campo; com valor, "Lançar R$ 45,90".
  Próximo passo discutido: teclado numérico próprio (2.18), porque o Safari não abre o teclado do
  sistema sem um toque da pessoa.
  *Pronto quando:* conferido no navegador nos dois modos e em 320, 390 e 1280px: foco ao tocar em
  "Lançar" vazio, erro em destaque, cursor visível e o formulário sem saltar ao digitar.

- [x] **2.18 Lançar mais limpo** (pedida pelo usuário, depois da 2.17)
  A tela parecia poluída e com cara de carregando. Três sinais de "carregando" saíram: o feixe que
  corria pela linha (o mesmo gesto do *skeleton*), o "R$ 0,00" esmaecido e o ladrilho tracejado à
  esquerda da descrição (parecia um spinner). No lugar dele, um lápis parado que vira o ícone da
  categoria ou o logo da marca reconhecida, sem o texto pular (sem nada ali, o logo aparecer no meio da
  digitação empurrava o texto; ajustado com o usuário). O valor
  virou número a preencher: "R$" pequeno, dígitos na cor do texto e um cursor fino aceso ao lado,
  parado enquanto vazio e piscando no foco. Sem mensagens repetidas: saem o rótulo visível "Valor" e o
  "Toque para digitar" (o botão já diz "Digite o valor"); o "opcional" vai para dentro do campo. Três
  níveis de texto: títulos de seção pequenos e discretos, conteúdo (chips e categorias) na cor do
  texto, o valor em destaque. Pagamento deixa de ser uma linha que abre e vira chips, como a data (no
  cartão, some: é sempre crédito).
  *Adiado, discutido com o usuário:* o tipo numa linha só (menor alvo de toque e menos visível para
  quem usa pouco; volta se o topo ainda pesar) e a seleção em tinta no lugar do anel do espectro (é
  regra de identidade, `CLAUDE.md` 7.1; se vier, vale para o app todo).
  *Pronto quando:* conferido no navegador nos dois modos e em 320 e 390px: nenhum sinal de
  carregamento com a tela parada, cursor aceso ao lado do valor vazio, Pagamento em chips.

- [x] **2.19 Primeira conta sem desvio** (pedida pelo usuário, com investigação antes)
  Sem conta, o "+" mostrava "Primeiro, uma conta" com um link para Contas, que repetia o mesmo aviso;
  eram três botões "Criar conta" seguidos e, ao salvar, Contas abria o detalhe da conta nova: o
  lançamento se perdia. Agora a conta nasce no próprio Lançar, num painel (`NewAccountSheet`, o mesmo
  de Contas, que segue abrindo o detalhe ao criar); ao salvar, o cache das contas se atualiza e a tela
  vira o formulário de lançamento com a conta nova escolhida e o foco no valor. Contas só desativadas
  deixaram de ouvir "Primeiro, uma conta": a mensagem diz que estão desativadas, com "Criar conta" e
  "Ver contas" (é lá que se reativa). O texto dos dois estados vazios ficou separado de propósito:
  contextos diferentes (lançar × lista de contas).
  *Pronto quando:* Vitest da regra (`accountGate`: nenhuma, só desativadas, pronta); no navegador,
  com usuários novos: do "+" ao lançamento salvo sem sair do Lançar, contas desativadas com a mensagem
  certa e Contas ainda abrindo o detalhe depois de criar; nos dois modos e em 320px.

---

### Faturas alinhadas ao banco (pedido pelo usuário, depois da 2.19)

O Prisma prevê a fatura de cada compra por dias fixos, mas o banco decide: o cartão Itaú do dono fecha
7 dias antes do vencimento (já foram 10), e compras perto do fechamento podem cair na fatura seguinte
(evidência em `docs/validacao-premissas.md`, seção 11). Desenho decidido: prever pela regra do
cartão, alinhar pela realidade (datas da fatura e compra presa), um único recálculo que respeita
fatura paga > compra presa > datas editadas > previsão.

- [x] **2.20 Fatura alinhada ao banco (base)**
  Mover uma compra (à vista ou parcelada inteira) para a fatura seguinte ou anterior sem mudar a data
  da compra, ficando presa; editar as datas de uma fatura passa a mover as compras e não pode
  atravessar as faturas vizinhas; um recálculo único no domínio (`docs/fase-2.md`, 2.9, com o plano
  de tarefas).
  *Pronto quando:* testes de domínio escritos antes (os 8 casos da 2.9 e as propriedades do
  recálculo); integração com o cenário dos lanches de ponta a ponta, a parcelada, as recusas, a
  corrida com o pagamento e o isolamento; Vitest das ações do painel; telas conferidas nos dois modos
  e em 320, 390 e 1280px; o dono move os lanches de 25/09 para a fatura de novembro em produção.
  *Andamento (2026-09-27):* domínio, banco e API prontos (tarefas 1 a 6, 518 testes verdes, sem
  commit); checkpoint B conferido pelo dono no Postman; tarefas 7 e 8 feitas (522 testes no backend, 216 no
  Vitest); falta o checkpoint C: mover os lanches em produção, depois do commit e do deploy. Com a
  2.20b (prévia), 526 testes no backend e 223 no Vitest. *Concluída em 2026-09-28:* push em `8ca8265`;
  checkpoint C conferido pelo dono no local (27/09) e em produção (28/09): os lanches movidos para a
  fatura de novembro.

- [x] **2.20b Prévia do ajuste de datas** (pedido do dono)
  No "Ajustar datas" da fatura, a lista ao vivo das compras que mudariam de fatura, as recusas antes de
  salvar e o botão "Salvar e mover N compras" (`docs/fase-2.md`, 2.10).
  *Pronto quando:* integração provando que a prévia lista exatamente o que o salvar move e não grava
  nada; Vitest dos textos; tela conferida com toques reais em 320, 390 e 1280px, nos dois modos.

- [x] **2.23 Catálogo de categorias que evolui** (pedido do dono, antes da 2.21)
  Categorias padrão com chave estável e versão; cada usuário recebe uma vez as categorias das versões
  novas, sem perder o que personalizou nem ver voltar o que excluiu. Primeira entrada nova: Seguros
  (Vida, Residencial, Veicular), pedido do pai do dono (`docs/fase-2.md`, 2.11).
  *Pronto quando:* testes de domínio antes (as regras da 2.11); integração com usuário antigo, exclusão
  que não volta, corrida sem duplicar e isolamento; teste que confere os ícones do catálogo contra o
  mapa do front; tela conferida. *(Feita em 2026-09-28: 544 testes no backend, 223 no Vitest.)*

- [ ] **2.21 Cartão que fecha N dias antes do vencimento** *(adiada em 2026-09-28, decisão do dono)*
  *Evidência nova (2026-09-29):* um segundo cartão Itaú fechou em 29/09 (vence 07/10, 8 dias antes) e
  no próprio dia 29 o banco mostrava fechada e o Prisma, aberta. O dia do fechamento do banco é
  exclusivo; o Prisma o trata como o último dia da fatura (um dia de diferença na compra e no status).
  Contorno até a 2.21: cadastrar o fechamento um dia antes do que o banco mostra. Detalhes em
  `docs/validacao-premissas.md`, seção 11.
  *Por que adiada:* o "Mover para" e o ajuste de datas da 2.20 já corrigem a fatura quando a previsão
  erra, e a etapa é grande (cálculo central da fatura, migration, telas) para o ganho com três pessoas.
  *Para retomar:* a especificação está pronta em `docs/fase-2.md`, 2.12, e o plano em `tasks/plan.md` e
  `tasks/todo.md` (fora do git). Falta a revisão do dono: os textos da tela, o ajuste da regra 5 ("não
  fechou pela regra antiga ou pela nova") e qual modelo vem marcado num cartão novo. Depois disso, a
  tarefa 1.
  Dois modelos no cartão: dia fixo (como hoje) ou N dias antes do vencimento (1 a 20); a compra feita
  no melhor dia de compra vai para a fatura seguinte. Os cartões existentes ficam no dia fixo até a
  pessoa mudar. Mudar a regra (nos dois modelos) realinha as faturas que ainda não fecharam pelo
  recálculo da 2.20, com aviso e "Desfazer". A tela diz "Compras até dd/MM" no lugar de "Fechamento",
  para não competir com o termo do banco; o formulário explica como funciona e mostra a próxima fatura
  nos termos do banco, para conferir. Não resolve compra processada com atraso pelo banco (isso é o
  "Mover para"). Especificação em `docs/fase-2.md`, 2.12; tarefas em `tasks/todo.md`.
  *Pronto quando:* testes de domínio antes (as datas dos 9 PDFs do Itaú, bordas de mês, troca de
  regra, propriedade da ordem das faturas); integração com troca de regra, corrida e isolamento;
  Vitest do exemplo ao vivo; checkpoint C: o dono troca o Itaú e confere 3 faturas com o app do banco.

- [ ] **2.22 Vencimento em dia útil**
  A data de caixa passa a ser o vencimento efetivo: próximo dia útil pelo calendário bancário (fins de
  semana, feriados nacionais fixos e os móveis a partir da Páscoa, inclusive Corpus Christi, que o
  Itaú tratou como não útil em 04/06/2026). O calendário é o `BankCalendar` da 2.26 (`docs/fase-2.md`,
  2.15), já testado; se a 2.22 vier antes, ela o cria. Detalhar quando a 2.21 terminar. *(Não depende da 2.21: o
  fechamento sai do vencimento nominal, `docs/fase-2.md`, 2.12, regra 3; pode vir antes, se preciso.)*

- [x] **2.24 Trocar o cartão de uma compra** (pedido de um usuário, 2026-09-28)
  *Concluída em 2026-09-29: conferida pelo dono na tela (à vista e parcelada), junto com a lista
  memoizada (editar, trocar cartão, mover de fatura e tocar em sequência).* Especificação em
  `docs/fase-2.md`, 2.13 (as três tarefas registradas lá). 568 testes no backend e 228 no Vitest; 16
  mutações pegas (10 no domínio, 6 na API); tela conferida com toques reais a 390, 320 e 1280px, claro e
  escuro. A proteção na troca de data do estorno ficou sem teste de corrida próprio.
  Quem lança a compra no cartão errado hoje precisa excluir e lançar de novo (na parcelada, redigitar
  tudo): o domínio recusa com "Em compra no cartão, conta, tipo e meio de pagamento não mudam". Ao
  editar a compra, a pessoa passa a escolher outro cartão. **Escopo: só de cartão para cartão.** Entre
  cartão e Pix ou débito a compra ganharia ou perderia fatura (e a parcelada viraria à vista): fica
  para quando alguém pedir.
  *Regras (detalhar em `docs/fase-2.md`, 2.13, antes do código):*
  1. Trocar o cartão é como trocar a data: a compra inteira (a parcelada, a partir de qualquer parcela,
     pela edição da compra) vai para as faturas do cartão novo pela regra dele, a partir da data da
     compra (a nova, se ela também mudar), abrindo as que faltarem; o caixa segue o vencimento novo.
     A data da compra não muda por causa da troca.
  2. Recusada se alguma parcela está em fatura paga, ou se cairia numa fatura paga do cartão novo
     (as mesmas regras e o mesmo texto da troca de data, com "cartão" no lugar de "data").
  3. Compra com estorno não troca de cartão: o estorno ficaria no cartão antigo. Na à vista isso já é
     recusado (`Refund.CheckPurchaseEdit`: "Esta despesa tem estornos; o tipo e a conta não mudam."); na
     parcelada ainda não (hoje ela nem recebe a conta).
  4. A compra movida à mão (`StatementPinned`) deixa de estar presa, como na troca de data.
  5. Só para cartão de crédito: outra conta continua recusada, com mensagem que diz o que fazer.
  6. Concorrência: `StatementTouch` nas faturas de origem e de destino (409 com o pagamento).
     **Achado:** a troca de data na edição da compra (`UpdateTransaction` e `UpdateInstallmentPurchase`)
     também põe e tira compra de fatura e hoje não chama o `StatementTouch`, contra o que o `CLAUDE.md`
     (seção 6) pede; corrigir junto, por ser o mesmo trecho.
  *Onde mexe:*
  - Domínio: `CardPurchase.EditTransaction` (à vista; a checagem da conta vira a troca) e
    `CardPurchase.Edit` (parcelada; recebe o cartão novo e trata como a troca de data);
    `Transaction` e `InstallmentPurchase` ganham o jeito interno de mudar `AccountId`.
  - API: `UpdateTransaction` (carrega as faturas do cartão novo); `UpdateInstallmentPurchase` (o
    `Request` ganha `AccountId`, confere o estorno e carrega as faturas do cartão novo); `StatementTouch`
    nos dois; `api-types.ts` regerado; `cards.http` e `transactions.http`.
  - Front (`EntrySheet.tsx`): seção "Cartão" com os cartões ativos (e o atual) no `CardForm` e no
    `PurchaseForm`; a nota "Conta e meio de pagamento não mudam…" muda; aviso depois de salvar: "Agora no
    <cartão>, na fatura de <mês>." (sem migration).
  *Testes:*
  - **Mudam** (hoje afirmam a recusa): `CardTransactionUpdateTests.Account_type_and_method_never_change`
    e `CardPurchaseDateTests.Account_type_and_method_still_never_change` (a troca para outro cartão sai do
    caso de recusa; tipo, meio e conta que não é cartão continuam recusados);
    `CardPurchasesTests` (integração, linha 195: a recusa passa a ser para conta que não é cartão).
  - **Novos, domínio, antes do código:** à vista vai para a fatura certa do cartão novo (regras de
    fechamento diferentes, caixa no vencimento novo); parcelada 3x troca inteira, com a soma igual;
    troca junto com a data; parcela em fatura paga e destino pago recusados; compra presa solta; estorno
    recusado na parcelada.
  - **Novos, integração:** troca à vista e parcelada de ponta a ponta (faturas dos dois cartões, total
    de cada fatura, Resumo do mês do caixa); estorno recusado; cartão de outro usuário "Conta não
    encontrada"; corrida com o pagamento (409), também para a troca de data.
  - **Vitest:** o texto do aviso e a lista de cartões oferecidos (ativos e o atual).
  *Pronto quando:* testes acima verdes, com mutação provando a regra 2 e o estorno da parcelada;
  conferido na tela em 320, 390 e 1280px, claro e escuro, com toques reais.

- [x] **2.25 Lançamentos que se repetem** (pedido do dono, 2026-09-29)
  *Concluída em 2026-09-29: T1 (agenda), T2 (série no domínio), T3 (tabela, migration
  `AddRecurrences` e gerador `RecurrenceRunner`), T4 (`RecurrenceWorker`, ao subir e de hora em hora), T5 (API:
  criar, listar, editar, encerrar e resolver pendências), T6 (tela: "se repete" no Novo lançamento e no painel),
  T7a (excluir o lançamento de uma série pergunta se ela também acaba), T7 (tela Recorrências), T8 (previsão),
  lacunas de teste fechadas e a conta excluída encerrando as séries dela. Checkpoint B conferido pelo dono.
  Checkpoint C: o dono deu por conferido pelos testes e pelas conferências de tela. As três migrations da
  etapa viraram uma (`AddRecurrences`) antes do commit. Tarefas em `tasks/2.25/todo.md`, fora do git.*
  Pix de todo mês ou toda semana e cobrança recorrente no cartão (o pet do pai: R$ 400,00 todo dia 25).
  Uma abstração (`Recurrence`) com dois destinos: lançamento simples fora do cartão e compra à vista no
  cartão. Cada ocorrência é gerada **só no dia dela**, por uma tarefa em segundo plano (`BackgroundService`,
  sem Hangfire) que alcança o atraso e nunca duplica; o futuro aparece como previsão no "Daqui para
  frente" e na fatura aberta do cartão, sem mudar Resumo nem saldo. Especificação em `docs/fase-2.md`,
  2.14 (decisões de arquitetura, regras, exemplos e as 8 tarefas).
  *Pronto quando:* testes de domínio antes (agenda, geração, previsão); integração de idempotência,
  atraso, fatura paga e isolamento; tela conferida em 320, 390 e 1280px, claro e escuro; o dono cadastra
  a cobrança do pet e o aluguel e vê a previsão.

- [x] **2.26 Débito automático** (pedido do dono, 2026-09-29)
  *Concluída em 2026-09-30: tarefas 1 a 6 (calendário, domínio, gerador, API, tela, Recorrências) e o
  checkpoint do dono, que testou os fluxos com um usuário de teste e cadastrou a Copel dele (o primeiro débito
  sai em 20/10). Depois do checkpoint, a pedido dele, o sino ganhou o anel do espectro no número e balança uma
  vez quando chega um débito. Especificação e notas em `docs/fase-2.md`, 2.15.*
  Contas de consumo em débito automático (luz, água, gás, escola; qualquer empresa): a série da 2.25 com
  o tipo `AutoDebit`, só despesa, só conta corrente, mensal. O débito cai no **próximo dia útil** do
  vencimento, pelo calendário bancário que esta etapa cria primeiro (fins de semana, feriados nacionais
  fixos e os móveis a partir da Páscoa; puro, no domínio, com dois usos só: o débito automático e, na
  2.22, o vencimento da fatura). Quando **o valor muda a cada mês**, a série guarda a estimativa: o
  lançamento sai com ela no dia do débito, conta no saldo e no "Saiu" marcado "≈ a conferir", e a pessoa
  confirma ou corrige. O **sino** no cabeçalho conta o que falta conferir e abre o painel "Para conferir";
  cada item sai quando é conferido, sem "marcar todas como lidas". Especificação em `docs/fase-2.md`,
  2.15 (decisões, regras, exemplos e as 6 tarefas, na ordem: calendário, domínio, gerador, API, tela,
  Recorrências).
  *Pronto quando:* testes de domínio antes (tabela do calendário de 2025 a 2027, propriedades do próximo
  dia útil, vencimento × data do débito, conferir); teste de arquitetura do calendário; integração de
  fim de semana, feriado, atraso, idempotência, conferir, Resumo e isolamento; Vitest dos textos; tela
  conferida em 320, 390 e 1280px, claro e escuro, com toques reais; o dono cadastra a Copel e a Sabesp
  e confere um débito pelo sino.

- [x] **2.27 Ícones de bancos e corretoras** (pedido do dono, 2026-09-30)
  *Concluída em 2026-09-30; o dono conferiu o tamanho do ícone na tela.*
  Itaú, Banco do Brasil, Bradesco, Caixa, Santander, BTG Pactual, Safra, XP e as corretoras Rico, Clear,
  Toro, Ágora, Avenue, Íon e Modalmais aparecem com o ícone da instituição (SVG com o fundo da marca,
  trazido pelo dono e otimizado com SVGO) no lugar do monograma, nas contas e nos lançamentos. Nome que
  também é palavra comum ("agora", "rico", "clear") só vale como nome inteiro ou por extenso ("Rico
  Investimentos"). A "Nova conta" (e a edição) mostra o ícone dentro do campo "Nome" enquanto se digita,
  com "<marca> reconhecido" ao lado do rótulo, como no Lançar. O Petz, acrescentado à mão no arquivo
  gerado, foi para `logos.manual.ts`: o `npm run gen:brands` não o apaga mais.
  *Pronto quando:* Vitest do reconhecimento (ordem, nome inteiro, falsos positivos), provado com mutação;
  telas de contas e da "Nova conta" conferidas em 390px, claro e escuro; pacote principal do mesmo tamanho.

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

*Estado em 2026-09-26:* o app está no ar no Railway desde 2026-09-25 (connection string montada com
`${{Postgres.PGHOST}}` etc., cadastro liberado por e-mail, porta 8080) e já tem usuários além do dono
(o pai usa a produção). Faltavam backup, "Wait for CI" e instruções; a senha do banco apareceu numa
conversa e o TCP Proxy do Postgres ficou ligado. A H.2 foi dividida em três (decisões com o usuário):

- [x] **H.2a Conta e acesso**
  Plano Hobby antes do fim do teste (o teste acaba em 30 dias ou US$ 5 e cai no Free, com US$ 1/mês;
  volumes de conta de teste são apagados 30 dias depois) e limite de gasto. Usuário próprio da API no
  banco, `prisma_app`, dono das tabelas do app e sem superusuário (`postgres` só para administração);
  senha nova para os dois, gerada fora da conversa; TCP Proxy desligado (ligar só quando precisar do
  DBeaver). 2FA no Railway, no GitHub e no e-mail. "Wait for CI" no deploy.
  *Pronto quando:* o script do `prisma_app` foi testado numa cópia do banco local (a API sobe e aplica
  migration como `prisma_app`); em produção, login, lançamento e resumo funcionam com o `prisma_app`;
  a senha antiga do `postgres` não entra mais; o TCP Proxy está desligado.
  *Feito (2026-09-26, pelo usuário, com o roteiro de `docs/operacao.md`):* API em produção como
  `prisma_app` (login, lançamento e resumo conferidos no celular); senha nova do `postgres`, a antiga
  recusada; TCP Proxy desligado com o app funcionando; "Wait for CI" ligado; 2FA no GitHub e no
  e-mail. **Pendentes do usuário:** plano Hobby (continua no Free, com US$ 1/mês, que não sustenta a
  API e o Postgres o mês todo; volume de conta vinda do teste é apagado 30 dias depois do fim do
  crédito), limite de gasto e 2FA no Railway.

- [x] **H.2b Backup fora do Railway**
  O backup nativo do Railway (se o plano incluir) só restaura no mesmo projeto e some junto com o
  volume. Serviço agendado no próprio projeto: `pg_dump` diário pela rede privada, criptografado com
  `age` (chave pública no serviço, privada só com o usuário) e enviado ao Cloudflare R2. Roteiro de
  restauração em `docs/`.
  *Decisões (com o usuário, 2026-10-02):* cron diário às 03:00 de São Paulo, num serviço próprio
  (`ops/backup/`), pela rede privada: o TCP Proxy continua desligado (GitHub Actions ou o Mac exigiriam
  o banco exposto). Usuário `prisma_backup`, só leitura (`pg_read_all_data`). A cópia é criptografada
  antes de sair do contêiner e só sobe se o `pg_dump` terminar bem (nunca uma cópia cortada com cara de
  válida). **Retenção de 90 dias** (regra de ciclo de vida do R2) e **trava do bucket de 30 dias**
  (Bucket Lock: nem o token apaga, mesmo com o Railway invadido). **Alerta pelo Healthchecks.io**
  (grátis): o backup avisa ao começar, ao terminar e ao falhar, e o silêncio de um dia vira e-mail.
  Cada execução registra no log a contagem de linhas por tabela (só números), para comparar com a
  restauração.
  *Pronto quando:* um dump do R2 foi descriptografado, restaurado no Postgres local e conferido
  (contagens e o resumo de um mês iguais aos da produção).
  *Código pronto (2026-10-02), falta a parte do dono:* `ops/backup/` (imagem, `backup.sh`, `restore.sh`),
  `ops/postgres/backup-user.sql` e o roteiro em `docs/operacao.md`, seção 5. O job `Backup (round trip)`
  do CI sobe um Postgres com o schema real das migrations e confere: o `prisma_backup` não grava; o arquivo
  é do age, sem dado em claro; a restauração devolve as mesmas contagens e os mesmos dados; ela recusa um
  banco com tabelas; um `pg_dump` que morre no meio, senha errada e variável faltando não deixam arquivo,
  e a falha é avisada. Provado com quatro violações propositais (sem `pipefail`, usuário que grava,
  restauração por cima, falha sem aviso).
  *Feito (2026-10-02, com o dono):* produção no Postgres 18 (imagem do backup com o `pg_dump` 18); R2 com
  ciclo de vida de 90 dias e trava de 30; Healthchecks.io; `prisma_backup` criado e conferido (lê, não grava);
  serviço `backup` no Railway, cron `0 6 * * *`. Primeira execução: 13 tabelas, 57.743 bytes, aviso recebido.
  Restauração de prova com a chave do cofre (app Senhas), num Postgres 18 provisório: as 13 contagens iguais
  às do log e o Resumo igual ao da produção. Ajustes do roteiro achados no caminho (serviço vazio antes do
  repositório, `REGION=auto`, porta só em `127.0.0.1`, ordem do `pbpaste`) já estão em `docs/operacao.md`.
  Pendente do dono: a cópia da chave fora do computador.
  *Depois (2026-10-02, PR próprio):* o Postgres local e o dos testes alinhados ao 18 da produção
  (`docker-compose.yml` e `PostgresFixture`). Os dados locais passaram do 17 por `pg_dumpall` para o volume
  novo `prisma_postgres18-data`, montado em `/var/lib/postgresql` (a imagem 18 mudou o caminho): as 13
  contagens iguais. O volume antigo `prisma_postgres-data` fica como plano B; o dono o apaga quando quiser
  (`docker volume rm prisma_postgres-data`).

- [ ] **H.2c Operação**
  Senha esquecida: resolvida pela própria pessoa com o "Esqueci a senha" (H.4c); falta só o roteiro para quem perdeu
  o acesso ao e-mail. Monitor de disponibilidade em `/api/health`,
  backup manual antes de migration arriscada e instruções no `CLAUDE.md`, seção 7.

- [x] **H.3a Logs estruturados no backend** (pedida pelo usuário, 2026-09-26)
  *Decisões (com o usuário):* só o `ILogger` nativo, sem Serilog (dependência nova e segunda
  configuração; trocar de provedor depois é mexer só na inicialização, porque o código fala com o
  `ILogger`); mensagens em inglês (en-US), propriedades em inglês; o `traceId` pode aparecer ao cliente.
  Produção escreve uma linha JSON por evento, no formato que o Railway filtra (`level`, `message` e as
  propriedades no primeiro nível: `@level:error`, `@traceId:…`, `@userId:…`); desenvolvimento, texto
  legível numa linha. Uma linha por requisição da API (método, rota modelo, status, duração, `userId`),
  e eventos declarados num lugar só com `[LoggerMessage]` (login, cadastro, rate limit, conflito,
  exceção, migrations, conta criada, fatura paga), sem valor, descrição, e-mail, senha ou token (regra 8).
  O `traceId` da requisição vai na resposta de erro (ProblemDetails) para achar os logs dela.
  *Pronto quando:* teste unitário do formatador (uma linha, campos, só `traceId`/`spanId`/`userId` dos
  escopos); teste de arquitetura que recusa evento com propriedade sensível e `LogX` fora dos eventos
  declarados (CA1848 e CA2254 como erro); integração: linha por requisição com rota, status e usuário,
  o `traceId` da resposta de erro igual ao do log, login errado sem e-mail nem senha em nenhum log, 500
  com a exceção, e rate limit registrado.
  *Feito:* `AppLog` (13 eventos, ids por faixa), `JsonLineConsoleFormatter` (horário pelo `IClock`),
  `UseRequestLogging` e `UseUserLogScope`, `traceId` em todo ProblemDetails. As travas foram provadas
  com violação proposital: `LogInformation` direto e interpolação derrubam o build; um evento com
  `email` derruba o teste de arquitetura. Na saída real, o `traceId` da resposta de erro é o mesmo da
  linha da requisição. Login com e-mail desconhecido virou evento próprio (`LoginUnknownEmail`), para a
  frase não sair com "user (null)". Roteiro de investigação em `docs/operacao.md`, seção 3.

- [x] **H.3b Erros do navegador no servidor**
  *Decisão (com o usuário, 2026-09-26):* o `traceId` **não** aparece na tela. Erro esperado já tem
  mensagem clara; API fora do ar e erro do navegador nem têm `traceId`; e, com poucas pessoas, o
  `userId` e o minuto do erro bastam para achar tudo no Railway. O `traceId` continua na resposta de erro
  da API, fora da interface. **Rever se o projeto crescer** (muitos usuários, suporte que não conhece
  quem reclama): aí um código na tela de erro inesperado passa a valer.
  O buraco real é o erro do navegador (a tela quebra e nada chega ao servidor): `POST /api/client-errors`,
  com rate limit, recebendo só mensagem, pilha, tela (rota) e versão do app, nunca dados da tela;
  registrado como `Warning` pelo `AppLog`, já com o `userId` da sessão. Chamado pelo `ErrorBoundary` e
  por erros e promessas não tratados. Mensagens do frontend conferidas: erro inesperado genérico e
  honesto; sem conexão com mensagem própria.
  *Pronto quando:* integração do endpoint (registra com o `userId`, recusa corpo grande, rate limit,
  anônimo também registra) e teste de arquitetura cobrindo o evento novo; Vitest do que o navegador
  envia (sem dados além dos permitidos); no navegador, um erro provocado em `/dev/erro` chega ao log.
  *Feito:* `ReportClientError` (validação de tamanho, tipo e tela; a consulta e o fragmento saem do
  caminho) e o evento `ClientError`, com a pilha do navegador como exceção do log (`BrowserError`), no
  campo `exception`: o gerador do `[LoggerMessage]` descarta parâmetro fora da frase. Rate limit próprio
  (10 por minuto por IP). No navegador, `reportClientError` (uma vez por erro, envio que falha é
  engolido) no `ErrorBoundary` e nos eventos `error` e `unhandledrejection`; versão = momento do build.
  Conferido com a API servindo um build de desenvolvimento: `/dev/erro` (crash), erro num `setTimeout`
  e promessa rejeitada chegaram ao log. As mensagens de erro inesperado e de falta de conexão já eram
  genéricas e em pt-BR: não mudaram.
  Conferido em produção pelo usuário (2026-09-27): os logs da H.3a e os relatos do navegador aparecem
  no Railway, filtráveis.

- **H.2 Deploy no Railway** (texto original, coberto pelas etapas acima)
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

### E-mail e conta (antes do cadastro público)

Pedida pelo dono (2026-10-13). Especificação, decisões e o porquê de outbox em vez de RabbitMQ/Kafka em
`docs/email.md`; configuração da Cloudflare, do Resend e do Gmail em `docs/operacao.md`, seção 8.

- [x] **H.4a Envio de e-mail**
  `IEmailTransport` (Resend por HTTP, pasta local em desenvolvimento, falso nos testes), fila `outbox_emails`
  gravada na mesma transação do motivo, `EmailOutboxWorker` com tentativas e espera crescente, limites por usuário,
  retenção de 30 dias, logs sem endereço. Nenhum e-mail ainda é pedido por um fluxo: a etapa entrega a peça.
  *Pronto quando:* integração da fila (pedido junto com a gravação e nada sem ela; envio com o transporte falso;
  falha passageira reagenda e a definitiva marca `Failed`; 24 h sem sucesso marcam `Failed`; duas execuções ao mesmo
  tempo enviam uma vez; limite por minuto e por dia; limpeza dos 30 dias); teste do `ResendEmailTransport` com
  `HttpMessageHandler` falso (corpo, cabeçalhos, 2xx/429/5xx/4xx/tempo esgotado) e um `[LiveFact]`
  contra o Resend real; a API não sobe fora de desenvolvimento sem a chave; teste de arquitetura com os eventos novos
  do `AppLog`.
  *Feito (2026-10-13):* `Infrastructure/Email`: `IEmailTransport` (`ResendEmailTransport`, `FolderEmailTransport`),
  `IEmailComposer` (nenhum ainda), `EmailQueue`, `EmailOutboxProcessor` e `EmailOutboxWorker`; tabela `outbox_emails`
  (migration `AddOutboxEmails`, global, com cascata do usuário). Duas mudanças na especificação, registradas no
  `docs/email.md`: a tarefa consulta a cada 5 s em vez de ser avisada (o aviso chegaria antes do commit de quem
  pede), e sem `Idempotency-Key` (o token muda a cada tentativa; o raro e-mail em dobro é inofensivo). 401/403 do
  Resend esperam na fila (chave ou domínio a consertar), em vez de perder o e-mail. Testes provados com mutação:
  sem o `SKIP LOCKED`, sem a reserva, limites, limpeza, 401, pasta em produção, espera, cascata e o `Skipped`
  derrubam cada um o seu teste. A primeira versão não pegava a falta do `SKIP LOCKED`; entrou o teste da linha
  travada por outra conexão.

- [x] **H.4b Confirmação de e-mail no cadastro**
  Repetir a senha no cadastro; cadastro sem entrar e tela "Confira seu e-mail" com reenviar; `/confirmar-email`
  entra direto; login recusado sem confirmar (só com a senha certa); e-mail já cadastrado com a mesma resposta e o
  aviso ao dono do e-mail; contas existentes marcadas como confirmadas na migration.
  *Pronto quando:* integração de cada caso do `docs/email.md`, 5.1 (inclusive: a resposta do cadastro não revela se o
  e-mail existe; token vencido, usado e de outra conta recusados; login com senha errada não revela o estado da
  conta); telas conferidas em 320/390/1280, claro e escuro; o e-mail aberto pela pasta local em desenvolvimento.
  *Feito (2026-10-14):* `EmailLayout` (cabeçalho em tinta, faixa do espectro, quadro de dados; `docs/email.md`, seção 4,
  "Visual"), `ConfirmEmailComposer` e `AlreadyRegisteredComposer`, `/auth/confirm-email` e `/auth/resend-confirmation`,
  cadastro sem entrar (202), login que confere a senha antes do estado, `Error.Code` no ProblemDetails, `App:PublicUrl`,
  provedor de token próprio (3 dias) e migration `ConfirmExistingEmails`. Telas: repetir a senha, "Confira seu e-mail",
  aviso no login com "Reenviar", `/confirmar-email` (pede a senha, entra e avisa; link que não vale mais pede outro).
  Revisão de segurança pedida pelo dono: o link não se adivinha, mas entrar só pelo link permitia o pré-sequestro de
  conta (`docs/email.md`, 5.1 e 5.5); por decisão dele, confirmar pede a senha do cadastro. O token foi para depois
  do `#` (fora dos logs de acesso) e sai da barra de endereço. Até a H.4c, a senha errada não tem saída na tela
  ("criar uma senha nova" entra com ela); o cadastro segue fechado na produção. O aviso de
  e-mail já cadastrado leva só ao `/entrar`; o `/esqueci-senha` entra na H.4c. Testes provados com mutação (10
  defeitos); o do reenvio a quem já confirmou não era pego pelo envio (o composer também pula) e passou a conferir a
  fila. Na tela, o roteiro achou o que os testes não acham: "Enviamos outro e-mail" mentia dentro do minuto do limite.

- [x] **H.4c Esqueci a senha**
  `/esqueci-senha` e `/nova-senha` (link de 1 hora); ao trocar, cai o resto das sessões, zera o bloqueio, confirma o
  e-mail, avisa por e-mail e entra.
  *Pronto quando:* integração do `docs/email.md`, 5.2 (resposta igual para e-mail com e sem conta; link vencido,
  usado, e depois de outra troca de senha recusado; sessão antiga cai; conta bloqueada destrava); telas conferidas.
  *Feito (2026-10-14):* `ForgotPassword` e `ResetPassword`, `ResetPasswordTokenProvider` (1 hora), e-mails
  `ResetPassword` e `PasswordChanged` (com link "Crie uma senha nova agora"), conferência da sessão a cada 5 min
  (`AuthSetup.SessionCheckInterval`). Telas `/esqueci-senha` e `/nova-senha`, "Esqueci a senha" no login e a saída
  "Criar uma senha nova" na confirmação de e-mail; o aviso de e-mail já cadastrado ganhou o link. Fecha o
  pré-sequestro: criar a senha pelo e-mail confirma a conta e mata a senha do intruso (teste de ponta a ponta).
  Testes provados com mutação (8 defeitos, todos pegos); fluxo inteiro conferido na tela.

- [x] **H.4d Trocar a senha**
  "Trocar senha" no menu da conta: senha atual, nova e repetir. Esta sessão fica, as outras caem, aviso por e-mail.
  *Pronto quando:* integração do `docs/email.md`, 5.3 (senha atual errada recusada e contada para o bloqueio; sessão
  atual continua e outra cai depois da validação); painel conferido.
  *Feito (2026-10-15):* `POST /auth/change-password` (`ChangePassword`): senha atual errada é 400 `WrongPassword` e conta
  para o bloqueio junto com o login; conta bloqueada não troca; o cookie desta sessão é renovado (`RefreshSignInAsync`),
  as outras caem na conferência seguinte; vai o `PasswordChanged`. Tela `/trocar-senha` pelo menu da conta, em vez de
  painel, igual ao "Apagar conta" (`docs/email.md`, 5.3). Conferida em 320/390/1280, claro e escuro, com toque real
  (validação, senha atual errada, senhas diferentes, troca com o aviso). 8 testes de integração.

- [x] **H.4e Contador de senhas erradas com prazo**
  A pendência de produto: erros espalhados no tempo não acumulam mais (janela de 15 min, coluna
  `last_failed_login_at`, `IClock`). Entra aqui por mexer no mesmo login.
  *Pronto quando:* integração: 4 erros, 16 min, mais 4 erros não bloqueiam; 5 erros seguidos bloqueiam como hoje.
  *Feito (2026-10-15):* `AppUserManager` sobrescreve o `AccessFailedAsync`: vale para entrar, confirmar o e-mail, trocar
  a senha e apagar a conta sem cada um lembrar da regra. A janela conta da última senha errada (5 erros a cada 14 min
  ainda bloqueiam; teste). Coluna `last_access_failed_at` (o nome cobre todos os lugares que pedem a senha, não só o
  login), migration `AddLastAccessFailedAt`, que zera as contagens antigas sem data. `docs/email.md`, 5.3b. Testes das
  duas etapas provados com 7 mutações, todas pegas.

### Abertura do cadastro (pedida pelo dono, 2026-10-14)

O dono quer abrir o cadastro e, com ele, a página de apresentação (que só aparece com o cadastro aberto, D5). Antes,
o que a LGPD pede e a regra do `CLAUDE.md`, seção 8 (texto jurídico só com revisão de advogado). Rascunhos em
`.lgpd/policies/`.

*Decisões (com o dono, 2026-10-14):* controlador **pessoa física** (Vagner Wentz), contato `contato@prismamoney.com.br`;
dados no **Railway US West** (transferência internacional a declarar); **18 anos ou mais**, por declaração no cadastro
(provisório, a confirmar com o advogado); **sem cobrança hoje**, com aviso antes de qualquer cobrança e nada cobrado
sem aceite. Depois da revisão assistida (GPT) dos rascunhos: o que a pessoa exclui é **apagado de vez em até 1 hora**
(o tempo do "Desfazer"; de ocorrência de série fica só a marca da data, sem valor nem descrição); mensagens do
`contato@` guardadas por **1 ano**; encerramento do serviço com **30 dias** de aviso e de exportação.

- [x] **A.1 Política de privacidade e termos de uso (rascunho)**
  Escritos a partir do que o app faz (dados, operadores, retenção, direitos), com as perguntas abertas para o
  advogado no fim de cada um. *Feito o rascunho (2026-10-14):* `.lgpd/policies/privacy-policy-v1-draft.md` e
  `terms-of-use-v1-draft.md`, revisão 2 com os apontamentos da revisão assistida (cada documento traz o histórico).
  Pendência crítica: a transferência internacional (art. 33). Revisão 3 com a A.3; a v1 pública sai dela na A.4.
- [ ] **A.2 Revisão jurídica** (dono, com advogado). Respostas às perguntas abertas (no fim de cada rascunho, em
  `.lgpd/`). *Decisão do dono (2026-10-14):* a v1 vai ao ar antes da revisão, sem as notas internas; o que o advogado
  pedir entra como v1.0.x (redação) ou v1.1 (muda direitos ou o tratamento, e pede novo aceite).
  *Revisão com a skill `lgpd` (Sushegaad, 2026-10-14), que não substitui advogado:* o dono não tem advogado por agora
  e ajusta se alguém apontar uma falha. Aplicados: "Hoje," no que o app não faz com o banco (deixa espaço para o Open
  Finance sem prometer); a caixa do cadastro passa a "Li e aceito os Termos de uso, li a Política de privacidade e tenho
  18 anos ou mais" (a política informa, não pede consentimento); registro das operações (art. 37) e avaliação do
  legítimo interesse (art. 10) em `.lgpd/`; roteiros de pedido de titular e de incidente (`docs/operacao.md`, 9 e 10)
  e a cópia dos dados por SQL (`ops/privacy/export-user.sql`). **Em aberto, o maior risco:** a base da transferência
  internacional (art. 33) não está na política; candidata o art. 33, IX (necessária ao contrato com a pessoa).
- [x] **A.3 Apagar a própria conta e apagar de vez o excluído** (direito de eliminação): "Apagar conta" no menu da
  conta, com a senha, apagando todos os dados da pessoa; tarefa que apaga de vez o que foi excluído; lista mínima das
  contas apagadas nos últimos 90 dias, para reaplicar a exclusão depois de restaurar um backup. A política promete os três.
  *Feito (2026-10-14), decisões do dono:* o excluído sai do banco depois de **30 minutos** (o "Desfazer" vale até lá),
  numa passada a cada 10 minutos (no máximo 40 minutos, dentro do "até 1 hora"); a ocorrência de série excluída **não
  deixa marca** (a série só gera depois de `GeneratedThrough`, que nunca volta); **sem e-mail** ao apagar a conta.
  `AccountEraser` (SQL, numa transação: tudo com `user_id`, depois o usuário, que leva em cascata o login e a fila de
  e-mails) e `ErasureRunner`/`ErasureWorker` (limpeza, reaplicação pela tabela `deleted_accounts` ao subir e a cada 10
  minutos, esquecimento depois de 90 dias). Quem é apontado por algo vivo perde só o vínculo: estorno vira avulso, série
  fica sem a categoria, série da conta apagada sai com a conta. `POST /auth/delete-account` (senha errada: 400
  `WrongPassword`, conta para o bloqueio); `/auth/me` responde 401 para usuário que não existe mais (conta apagada em
  outro aparelho). Tela `/apagar-conta`. Testes conferem pelo schema que toda tabela com `user_id` sai e toda tabela com
  `deleted_at` é limpa. Restauração: `docs/operacao.md`, 5.6. Novo teste do bloqueio por senha errada: dura 15 minutos e depois
  acaba, com a contagem zerada (`AuthTests`).
- [ ] **A.3b Cópia dos dados** (acesso e portabilidade): "Baixar meus dados" em JSON (lançamentos, contas, cartões,
  carteira, notas), no menu da conta; a política promete a cópia por pedido, e o botão evita o trabalho manual.
  *Adiada (decisão do dono, 2026-10-14):* a LGPD pede atender o pedido (art. 18, II; cópia completa em até 15 dias,
  art. 19), não um botão. Com poucas pessoas, o pedido vem pelo `contato@` e é atendido à mão. Volta se os pedidos
  ficarem frequentes, antes de encerrar o serviço (os termos prometem 30 dias de exportação) ou se o advogado pedir.
- [x] **A.4 Páginas `/privacidade` e `/termos`, aceite no cadastro** ("Li e aceito… e tenho 18 anos ou mais", com a
  versão e a data guardadas) e links no rodapé da página de apresentação e no cadastro.
  *Feito (2026-10-14), decisões do dono:* páginas em **HTML pronto**, como a apresentação: o texto é Markdown versionado
  (`src/prisma-web/src/legal/*.md`, tirado dos rascunhos sem as notas [ADVOGADO]/[ANTES DE PUBLICAR]), convertido no
  build por um conversor próprio e pequeno (`markdown.ts`, com teste), sem JavaScript, sem cookie, com sumário e tabela
  que empilha no celular; servidas em `/privacidade` e `/termos` sempre (cadastro aberto ou fechado), no `sitemap.xml`.
  Vigência da v1: 15/10/2026. Cadastro exige `acceptedTerms` (400 sem ele; nada é criado) e grava `TermsVersion`,
  `PrivacyVersion` e `TermsAcceptedAt`. **Quem já tinha conta aceita ao entrar:** `SessionUser.MustAcceptTerms` (em
  `/auth/me` e em tudo que entra) leva à tela `/aceitar-termos` antes do app, com "Sair" e "Prefiro apagar minha conta";
  `POST /auth/accept-terms`. Versão nova num dos dois textos pede o aceite de novo (`LegalTerms`; teste liga a versão do
  texto à da API). Links no rodapé da apresentação (com o `contato@`) e no pé das telas de entrada. No `npm run dev`, o
  Vite monta as duas páginas a cada pedido com o mesmo código do build (`scripts/legal-pages.mjs`).
- [x] **A.5 Abrir:** `contato@` recebendo (`docs/operacao.md`, 8.1), `Registration__Open=true` no Railway; a página de
  apresentação aparece sozinha em `/`.
  *Feito (2026-10-15):* Email Routing da Cloudflare repassando o `contato@` ao Gmail do dono (MX, SPF e DKIM na raiz;
  filtro no Gmail para não cair no spam); `Registration__Open=true` no Railway e as `Registration__AllowedEmails__*`
  removidas (com o cadastro aberto, a lista não é lida, e guardava e-mails de pessoas sem necessidade). Produção
  conferida: a página de apresentação em `/` sem sessão, `/privacidade` e `/termos` no ar. Antes da A.5, os
  investimentos do dono (proventos, notas, carteira, conferências, avisos) foram levados do banco local para a produção,
  com ensaio e conferência por md5 nas duas pontas. Próximos:
  H.4e e H.4d juntas, monitor de disponibilidade (H.2c), o nome "Prisma Money" nas telas do app.

### Segurança (auditoria pedida pelo dono, 2026-10-15)

Auditoria só de leitura do projeto inteiro: sem achado crítico ou alto; alguns médios e baixos, que viraram as etapas
abaixo. **O repositório é público:** o relatório, os cenários e o desenho de cada correção ficam em `.security/` (fora
do git; `.security/pendencias.md` é a lista viva), e aqui só entra o que cada etapa faz. Falha aberta não se descreve
em arquivo versionado (`CLAUDE.md`, seção 8); depois de corrigida, entra aqui com o histórico, como a S.1.

Testando um dos achados em produção, apareceu o **M4** (resolvido na S.1): o rate limit por IP juntava todos os
usuários. Prova do dono: o Mac no Wi-Fi mandando pedidos com um e-mail que não existe bloqueou o login real dele no
celular, no 4G, com a senha certa. Qualquer um, com 5 pedidos por minuto, travava o login, o cadastro e a senha nova de
todo mundo.

*Causa:* a API lia o **último** valor do `X-Forwarded-For` (`Program.cs`, `ForwardLimit = 1`), e o Railway põe ali um
IP da borda dele, o mesmo para todo mundo (a S.1a mostrou que é público, não um salto interno como se supunha). *Em
aberto até a S.1a:* qual cabeçalho traz o IP real. A documentação do Railway cita só o `X-Real-IP` "para
identificar o IP remoto do cliente" e não fala do `X-Forwarded-For`; no fórum, um funcionário disse (2026-03) que o
`X-Real-IP` traz o IP da CDN num dos caminhos e recomendou o primeiro valor do `X-Forwarded-For`, e um moderador disse
(2026-09) que os dois são reescritos na borda. O teste com o `X-Forwarded-For` forjado não decidiu nada: a API lia o
último valor, e ignoraria o forjado de qualquer jeito. Decisão do dono: medir antes de trocar.

- [x] **S.1a Diagnóstico dos cabeçalhos de IP**
  Cada pedido à API, atrás do proxy, registra o evento `ClientIpHeaders`: se veio o `X-Real-IP` e se é interno,
  quantos valores tem o `X-Forwarded-For` e se o primeiro e o último são internos, se o `X-Real-IP` bate com um deles,
  o `X-Railway-Edge` e o `X-Railway-Request-Id`. Nenhum IP vai para o log: um endereço interno forjado pelo cliente
  aparece como "interno". Não muda o comportamento.
  *Pronto quando:* unitários do que é endereço interno (rede privada, 100.64.0.0/10, loopback, link-local, IPv6 local)
  e do retrato (formato documentado, forjado, linhas repetidas, porta, sem cabeçalho, texto que não é IP, ids cortados);
  integração: o evento sai atrás do proxy sem IP em nenhum log, não sai sem proxy nem para o frontend.
  *Feito (2026-10-15):* `ClientAddress.IsInternal` e `ClientIpHeaders` (`Infrastructure/Http`), evento 1006, ligado em
  `Program.cs` antes do `UseForwardedHeaders`. 26 testes, provados com 4 mutações, todas pegas. Roteiro do teste em
  produção: `docs/operacao.md`, seção 11.
- [x] **S.1b IP real nos limites por cliente**
  Com o resultado do diagnóstico, a API passa a ler o cabeçalho certo (de preferência o `X-Real-IP`, o documentado), o
  IPv6 conta pelo bloco /64, e o diagnóstico vira alerta: só registra quando o IP lido falta ou é interno.
  *Resultado do diagnóstico (2026-10-15, em produção, três pedidos do dono):* o `X-Real-IP` vem, não é interno e é
  igual ao **primeiro** valor do `X-Forwarded-For`; o último valor, o que a API lia, é um IP **público** da borda do
  Railway (`edge: gru1`), não um salto interno como se supunha; forjados pelo cliente, o `X-Real-IP` e o
  `X-Forwarded-For` são descartados pelo Railway. Decisão: o `X-Real-IP`. Como o IP errado era público, o alerta
  "interno" não pegaria o M4: ele passa a disparar também quando o `X-Real-IP` difere do começo da lista.
  *Pronto quando:* integração no formato medido: cada cliente com a própria cota pela mesma borda, o mesmo cliente por
  outra borda sem cota nova, IPv6 pelo /64; alerta só fora do formato, sem IP, só para pedidos que vieram pela borda e
  só da API; em produção, o teste do terminal e do celular da seção 11 dá o resultado oposto ao de antes.
  *Feito (2026-10-15):* `ForwardedForHeaderName = "X-Real-IP"` (`Program.cs`), `ClientAddress.PartitionKey` nas
  políticas do login e dos relatos do navegador (`AuthSetup`), o evento por pedido da S.1a virou o aviso
  `ClientIpHeadersUnexpected` (1006, `ClientIpReport.LooksWrong`). Testes provados com 5 mutações, todas pegas; com o
  código antigo de volta, o teste de integração reproduz o M4 (outra pessoa pela mesma borda recebe 429).
  `docs/operacao.md`, seção 11, e a nota da nuvem cinza (seção 7).
  *Conferido em produção pelo dono (2026-10-15):* com o Mac no Wi-Fi mandando um pedido a cada 3 segundos, o terminal
  mostrou `401` cinco vezes, `429` até o minuto virar e de novo cinco `401` (o limite de 5 por minuto, por cliente), e
  o login real pelo celular no 5G entrou durante o laço; nenhum `ClientIpHeadersUnexpected` no log. Antes da S.1b, o
  mesmo teste bloqueava o celular. M4 resolvido.
- [ ] **S.2 Senhas erradas: bloqueio por e-mail digitado + lugar** (P1). A mesma resposta para qualquer e-mail, com
  conta ou sem, inclusive no bloqueio e no tempo de resposta; quem erra a senha bloqueia só o próprio par (e-mail, IP),
  e não quem está em outro lugar; um freio por e-mail contra tentativas vindas de muitos IPs; limites por rota no lugar
  dos 5 pedidos por minuto divididos entre login, cadastro e senha nova; "Esqueci a senha" continua liberando.
  Desenho aprovado em princípio pelo dono (2026-10-16); faltam 5 decisões (números, contador na memória ou no banco,
  se confirmação, troca de senha e apagar conta entram na regra, apelido do IP no log, tempo igual junto), cada uma com
  recomendação em `.security/s2-bloqueio-por-email-e-ip.md`.
- [ ] **S.3 Proteção contra cadastro e tentativas automatizadas** (P1): limite global de cadastros, limpeza de contas
  nunca confirmadas, recusa de senha já vazada (consulta por k-anonimato), e MFA opcional (TOTP) mais adiante.
  Depende da S.2 (limites por rota). Decisões de produto em aberto.
- [ ] **S.4 Sessão revogável** (P1): o "Sair" invalida a sessão no servidor, "Sair de todos os aparelhos" no menu da
  conta, prazo máximo da sessão e cookie com o prefixo `__Host-`. Sessão guardada no servidor é tabela nova com
  `user_id`: entra no `AccountEraser`, na cópia dos dados (`ops/privacy/export-user.sql`) e nos testes de schema.
  Trocar o formato do cookie derruba as sessões uma vez.
- [ ] **S.5 Cabeçalhos de segurança** (P2): CSP, HSTS, proibição de iframe, `nosniff`, `Referrer-Policy` nas páginas do
  app, da apresentação e jurídicas. Conferir todas as telas nos dois temas (estilos em linha de bibliotecas).
- [ ] **S.6 Chaves do cookie cifradas** (P2): as chaves do Data Protection protegidas em repouso, e a restauração de
  prova (`docs/operacao.md`, 5.2) sem elas na cópia local.
- [ ] **S.7 Verificações de segurança no CI** (P2): dependências com falha conhecida (NuGet e npm), segredos no
  histórico, análise de código; Actions fixadas por SHA; Dependabot. Começa como aviso, para não travar o deploy por
  falso positivo. Instalar ou rodar ferramenta que fala com serviço externo pede autorização do dono.
- [ ] **S.8 Miúdos** (P3): lista de endereços permitidos para os logos, papel de banco separado para as migrations,
  relatos do navegador truncados e mascarados, aceite de termos conferido no servidor, teto na lista de lançamentos,
  limpeza do `<style>` nos logos, conferência do pacote npm `cn`. Lista com o porquê de cada um em `.security/pendencias.md`.
- **Fora do código, com o dono** (lista em `.security/pendencias.md`): TLS entre a API e o banco, 2FA em todos os
  painéis, DMARC em `p=reject`, DNSSEC e CAA, escopo do token do R2, HSTS na borda, ruleset da `main`.

---

## Fase I — Investimentos

Pedida pelo dono (2026-10-02), fora da ordem das fases. Especificação, decisões e achados de cada etapa em
`docs/investimentos.md`, seção 8. I.1 a I.6 na branch `feat/brapi-asset-list` (com commits); a I.7 na
`feat/investment-trades`, criada dela: a até e com commit (2026-10-12), sem push até o dono pedir.
O dono lançou todas as notas que tem (2026-10-12).

- [x] **I.1 Cliente da brapi** — lista inteira de ativos da B3, traduzida para `ListedAsset`/`AssetKind`.
- [x] **I.2 Catálogo e sincronização diária** — tabela global `assets` (exceção à regra 6), tarefa às 4h.
- [x] **I.3 Busca de ativos** — `GET /assets?q=` sem acento, e o `AssetPicker`; fracionário junto do lote padrão.
- [x] **I.5a Carteira** — `Holding`, Investimentos dentro de Contas, adicionar e tirar com "Desfazer".
- [x] **I.5b Proventos** — receita na conta onde caiu, ligada ao ativo; agrupados por dia nos Lançamentos.
- [x] **I.4 Logos** — baixados, limpos e servidos pelo Prisma; ação, unit e BDR mostram o logo, fundo o código.
- [x] **I.6 Importar o histórico de proventos** — 142 proventos na Íon (banco local), e a soma deles sai do saldo
  inicial (sem saída para zerar). Na produção desde 2026-10-06, junto com os dados do dono (`docs/investimentos.md`, seção 9).
- **I.7 Operações** — compra e venda pela nota, preço médio, eventos da empresa; depois, cotação. Decisões do dono
  (2026-10-06) e especificação em `docs/investimentos.md`, seção 8 ("I.7"). Branch `feat/investment-trades`.
  - [x] **I.7a Cálculo** — só domínio: rateio de taxas e corretagem da nota (`Money.Allocate`), compra, venda, os
    dois preços médios, resultado da venda e histórico por ativo.
  - [x] **I.7b1 Notas pela API** — tabelas, gravar/editar/excluir/desfazer conferindo a carteira inteira, posição e
    histórico por ativo, tirar da carteira só zerado; medir com 5.000 operações.
  - [x] **I.7b2 Notas na tela** — lançar nota (corretora da lista, a última usada), "Meus ativos" com quantidade e os
    dois preços médios, histórico no painel do ativo, "Últimas notas".
  - [x] **I.7b3 Corretagem por tipo** — "Corretagem cobrada em" com caixas de seleção dos tipos da nota (padrão: ação);
    substitui a marcação "vale também para FII, ETF e outros" (decisão do dono, 2026-10-08).
  - **I.7c Eventos da empresa** — catálogo comum de eventos, aplicados sozinhos pela data-com; ativo antigo nasce com
    o evento, por curadoria (decisões do dono, 2026-10-07).
    - [x] **I.7c1 Eventos e ativos antigos** — arquivo de eventos versionado e tabela global; desdobramento, grupamento
      e conversão no cálculo; nota de ativo já convertido recusada; começa com BCFF11 → BTHF11.
    - [x] **I.7c2 Conferir eventos** — "Confere / Não confere" do que cada evento fez com a quantidade da pessoa: uma
      linha no sino e o bloco em Investimentos (`docs/investimentos.md`, I.7c2). O dinheiro de frações fica para depois.
    - [x] **I.7c3 Ativo faltando** — "Não encontrou? Avise a gente", a fila e o aviso quando o ativo chega
      (decisões e especificação em `docs/investimentos.md`, 2026-10-08).
    - [x] **I.7c4 Liquidação paga em cotas** — a origem sai como venda (cotas + fração + caixa) e o destino entra pelo
      valor oficial da cota; BCFF11 → BTHF11 a R$ 10,25 (fato relevante, `docs/investimentos.md`, I.7c4) e IRDM11 → IRIM11 a
      R$ 83,39 (2026-10-12).
    - Depois: bonificação (com custo atribuído), evento com opções (CRFB3), parte em dinheiro da conversão (PETZ3) e das
      frações, confirmar PETZ3 e CVBI11, tela de administração. Subscrição: nota de compra (a confirmar).
      Casos em `docs/investimentos.md`, "Casos reais da curadoria".
  - [ ] **I.7d Cotação** — valor de hoje e ganho não realizado.
  - [x] **I.7e Visão da carteira** — três abas (Carteira, Proventos, Notas) com o ano escolhido: investido pelo custo, o
    espectro por tipo, peso de cada ativo, proventos com filtros, mês a mês e ano a ano (`docs/investimentos.md`, I.7e).

- **I.8 Caixa da corretora** — pedida pelo dono (2026-10-27): a nota de corretagem passa a debitar (compra) ou creditar
  (venda) o caixa da conta da corretora, com vínculo **opcional**; as notas antigas ficam como estão. Especificação,
  regras e valores esperados em `docs/investimentos.md`, seção "I.8". Etapa por etapa, uma de cada vez, com a revisão do
  dono entre elas.
  - [x] **I.8a Cálculo** — só domínio: `NetCashCents` da nota (compras + custos − vendas + custos, pelo rateio da I.7a),
    sinal e zero, `TransactionType.TradeSettlement`, o saldo (`AccountBalance`) e o resumo do mês tratando o valor novo
    (fora de "Entrou", "Saiu" e "Investido"); teste antes da implementação, com os valores da tabela da spec, e CsCheck
    para a soma. Sem banco, API nem tela.
    Feita em 2026-10-27, na branch `feat/caixa-corretora`: `TradeNote.NetCashCents()`, `TransactionType.TradeSettlement` (no
    fim do enum), `AccountBalance` e `MonthlySummary` tratando o valor novo; 13 testes novos (valores da spec e uma propriedade
    CsCheck: líquido = compras − vendas + todos os custos), 4 mutações provadas. `dotnet test` verde (Domínio 1160, Arquitetura 17,
    API 1054, 7 ignorados). A propriedade `TradeNoteId` do lançamento e a fábrica da liquidação ficam para a I.8b, com a migration:
    mapear a coluna antes quebraria o banco.
  - [x] **I.8b API e migration** — `trade_notes.settlement_account_id` e `transactions.trade_note_id`; criar, editar,
    trocar conta e data, tirar o vínculo, excluir e desfazer mantêm a liquidação na mesma gravação; editar a linha
    direto é recusado; cartão recusado; campo opcional no pedido; `.http` e `gen:api`. **A migration só se aplica no
    banco com o "pode" do dono.** Testes de integração, isolamento por usuário, esquema (`export-user.sql`, exclusão de
    conta, limpeza do excluído).
    Feita em 2026-10-28: `TradeNote.SettlementAccountId`, `Transaction.TradeNoteId` e `TradeSettlement.Settle` (domínio);
    `NoteSettlement` sincroniza a liquidação no mesmo `SaveChanges` de criar, editar, trocar conta/data, tirar o vínculo,
    excluir e restaurar (a mesma linha volta; índice único `ux_transactions_trade_note` conta a excluída); check
    `ck_transactions_trade_settlement`; editar ou excluir a liquidação à mão dá 409 ("Altere ou exclua a nota de
    corretagem."); conta de cartão recusada (400), conta de outro usuário é 404, conta inativa só recusa ao trocar de
    conta; excluir uma conta com nota vinculada é recusado, e a limpeza do excluído leva a liquidação junto e não apaga conta
    que uma nota ainda aponta; o pedido só acrescenta (`settlementAccountId` opcional); `TransactionResponse.tradeNoteId`;
    `.http` e `api-types.ts` (atualizado à mão: o `gen:api` exige a API rodando e a migration não foi aplicada no banco de
    dev, refazer na I.8d). 9 testes de domínio e 22 de integração (`TradeNoteSettlementApiTests`); 10 mutações provadas (3
    sobreviveram no começo: uma trava duplicada no handler foi removida e duas redes de segurança da limpeza ganharam teste).
    **Migration `AddTradeNoteSettlement` gerada e NÃO aplicada no banco de dev nem na produção: falta o "pode" do dono.**
    O front só ganhou o valor novo do tipo (compila); a liquidação aparece no extrato sem tratamento visual até a I.8d.
    `dotnet test` verde (Domínio 1169, Arquitetura 17, API 1076, 7 ignorados); `tsc` e `oxlint` limpos.
  - [x] **I.8c Investido** (**regra substituída pela I.9**; o que segue é o histórico) — `splitBalances` soma o "Em ativos"; Contas mostra "Em ativos R$ X" na conta de
    investimento; o "Investido" do mês no Resumo não muda. Vitest da conta pura.
    Feita em 2026-10-28: `AccountAssets.Of` (domínio; liquidações já ocorridas, compras − vendas, piso em zero) e
    `assetsCents` em `GET /accounts/balances` (campo novo, só acrescenta; nulo no cartão); `splitBalances` soma o "Em ativos" de
    **qualquer** conta ao Investido (a corrente que paga a nota também: o total Disponível + Investido não cai) e devolve
    `assetsCents`; o "Investido" do Resumo ganha a nota "X em ativos" (e o bloco se divide mesmo sem conta de investimento, se
    houver ativos); Contas mostra "Em ativos R$ X" e o "Investido" da conta de investimento inclui o valor. 6 testes de
    domínio, 4 de integração, 3 de Vitest; 6 mutações provadas, todas pegas. **Falta a conferência visual** (320/390/1280, claro
    e escuro): precisa do banco de dev com a migration da I.8b aplicada, junto com a I.8d.
  - [x] **I.8d Tela da nota** — campo "Debitar da conta" (última usada, opcional), aviso de conta negativa, a linha no
    extrato com a nota como destino ("Altere a nota"), editar uma nota antiga para vincular. Conferido em 320/390/1280,
    claro e escuro, com toque real.
    Feita em 2026-10-28: seção "Debitar da conta" no painel da nota (chips "Nenhuma" e as contas que não são cartão; a de uma nota
    nova é a última escolhida, senão a de nome igual ao da corretora, senão nenhuma; "nenhuma" também é lembrada, para quem lança
    notas antigas), a frase do que a nota faz no caixa ("A compra de R$ X sai de Íon") e o aviso "A conta ficará negativa. Você pode
    salvar mesmo assim." (que desconta o que a própria nota já liquidava ali); a liquidação no extrato como "Compra/Venda de ativos"
    com o logo da conta, valor em tinta com "−" ou "+" (nunca no espectro), e tocar nela abre a nota (`?aba=notas&nota=<id>`, que
    limpa o endereço). Regras puras em `trades.ts`, 7 testes de Vitest, 5 mutações provadas. **Migration aplicada no banco de dev em
    2026-10-28 (autorizada pelo dono)**; `gen:api` rodado de verdade e **idêntico** ao arquivo editado à mão na I.8b. Conferida por mim
    no Chrome (toque real, usuário descartável removido depois): Resumo, Contas, Lançamentos e o painel da nota em 390 claro, 320
    escuro e 1280 claro, sem rolagem lateral; achou e corrigiu dois defeitos (tile tracejado, que lê como "carregando", e texto cortado
    em 320 px). Vitest 591, `tsc` e `oxlint` limpos. **Falta a conferência do dono**, em especial no celular.
  - [x] **I.8e Documentos e produção** (**absorvida pela I.9d**, feita em 2026-10-29: o "Investido" mudou de regra e a varredura foi uma só) — varredura (`CLAUDE.md`, `README.md`, `docs/`, memória), contagens de testes,
    e roteiro de vincular a nota da PETR4 de 22/10 na produção, com o "pode" do dono.

- **I.9 Patrimônio investido** — pedida pelo dono (2026-10-28): o "Investido" passa a vir dos ativos (renda fixa inteira, com ou sem
  transferência, e ações ao custo, com ou sem nota vinculada), e o caixa das corretoras vira opcional por conta ("acompanhar o caixa").
  Quem investe há anos não precisa recriar o histórico de entradas. Especificação, regras e valores esperados em `docs/patrimonio.md`.
  Substitui a regra de "Investido" da D20 e o "Em ativos" da I.8c. Uma etapa por vez, com a revisão do dono entre elas.
  - [x] **I.9a Domínio** — `NetWorthSplit` (Disponível, Investido, Caixa nas corretoras, Patrimônio); testes antes. Feita em 2026-10-28:
    `NetWorthSplit.Of(contas, investido, rendimento)` e `NetWorthAccount` (`Prisma.Domain.Accounts`); 12 testes (o caso do dono, caixa livre
    negativo preservado, nota vinculada sem contar duas vezes, conta desligada, inativa e cartão fora), 5 mutações provadas, todas pegas;
    Domínio 1187 verdes (só o domínio; a suíte completa roda no fechamento, I.9d). **`Account.TracksCash` ficou para a I.9b**, junto com a
    coluna: mapear a propriedade antes quebraria o banco dos testes (como a `TradeNoteId` na I.8).
  - [x] **I.9b API e migration** — `accounts.tracks_cash` (aditiva, falsa por padrão), `tracksCash` opcional ao criar e editar conta,
    `AccountYield.principalCents`; `.http` e `gen:api`. **Migration só com o "pode" do dono.**
    Feita em 2026-10-28: `Account.TracksCash` (criar com o campo; editar sem ele **mantém** a escolha, para a tela aberta antes do deploy;
    só a conta de investimento aceita, as outras recebem 400 "Só a conta de investimento acompanha o caixa."), coluna `accounts.tracks_cash`
    (padrão falso) e check `ck_accounts_tracks_cash`; `AccountResponse.tracksCash`; `AccountYield.principalCents` (o principal que sobra
    dos aportes financiados pela conta, depois dos resgates); `accounts.http`; `api-types.ts` editado à mão (o `gen:api` roda na I.9c).
    8 testes de domínio, 7 de integração de contas (`AccountTracksCashApiTests`) e 3 asserções novas em `FixedIncomeInvestedByAccountApiTests`;
    6 mutações provadas, todas pegas; Arquitetura 17, Domínio 1195, contas e renda fixa por conta 36 verdes (suíte completa só no
    fechamento, I.9d). **Migration `AddAccountTracksCash` gerada e NÃO aplicada no banco de dev nem na produção: falta o "pode" do dono.**
  - [x] **I.9c Tela** — `splitBalances` reescrita (Resumo e Contas), "Acompanhar o caixa" na conta de investimento, card do mês "Aportado".
    Feita em 2026-10-28: nasceu `GET /dashboard/net-worth` (`GetNetWorth`, usa o `NetWorthSplit` do domínio: a regra fica num lugar só e o
    Resumo faz uma chamada; a medição de custo da regra 10 não foi necessária, o cálculo junta o que `GET /portfolio`, `/fixed-income/balances` e
    `/accounts/balances` já calculam); `useNetWorth`, Resumo "Hoje" (Disponível, Investido com "Inclui R$ X de rendimento", Caixa nas corretoras só
    para quem acompanha), Contas (a conta de investimento sem acompanhar mostra "caixa não acompanhado" e nenhum valor; acompanhando, "Caixa livre"),
    formulário da conta com "Acompanhar o caixa desta conta" (só investimento), card do mês renomeado "Aportado"; `splitBalances`,
    `balanceInAccounts` e o "Em ativos" da tela saíram. Achado e corrigido: salvar uma nota **vinculada** não recarregava saldos nem
    lançamentos (`invalidateTrades` dizia "saldos, não"; defeito da I.8d). `gen:api` rodado de verdade: idêntico ao escrito à mão na I.9b. 8
    testes de integração (`NetWorthApiTests`, oráculo: `GET /portfolio`), 3 de domínio, 8 de Vitest (`netWorth.test.ts`); 9 mutações provadas, 1
    sobreviveu no começo e virou teste. **Migration `AddAccountTracksCash` aplicada no banco de dev em 2026-10-28 (autorizada pelo dono).**
    Conferida por mim no Chrome com toque real (usuário descartável removido): Resumo, Contas, formulário e Lançamentos em 390 claro, 320 escuro
    e 1280 claro, sem rolagem lateral; liga o caixa pelo formulário, salva e o Resumo e Contas mudam. Vitest 586, `tsc` e `oxlint` limpos.
    **Falta a conferência do dono**, em especial no celular, e a suíte completa (uma vez, na I.9d).
  - [x] **I.9d Documentos e produção** — varredura, D21, suíte completa uma vez, roteiro da produção (absorve a I.8e). Feita em 2026-10-29
    (a produção em si não foi aplicada): `CLAUDE.md` (glossário: Caixa da corretora implementada, Disponível × Investido marcada como
    substituída, Patrimônio investido, Acompanhar o caixa, Caixa livre e Aportado; árvore de `docs/`; a tela de renda fixa; nova seção "Patrimônio
    na tela"; Resumo × Análise), `README.md`, `docs/patrimonio.md` (estado, decisões da implementação e o roteiro da produção, seção 8),
    `docs/investimentos.md` (a regra 6 da I.8 marcada como substituída, o aviso de conta negativa e o defeito da I.9c), `docs/renda-fixa.md`
    (estado, 7.4c, linha de D20 e a nova **D21**), `docs/fase-2.md` (2.4), `docs/operacao.md` (a exceção da chave estrangeira da liquidação na
    limpeza), `PLAN.md` e a memória. Conferidos e **sem mudança**: a política de privacidade e os termos (nenhuma categoria nova de dado;
    versão 1.0 mantida), `ops/privacy/export-user.sql` (usa `to_jsonb` da linha: as colunas novas entram sozinhas) e `docs/landing.md`.
    **Conferência do dono (2026-10-29): roteiro de 11 passos no banco local, "deu tudo certo".** A suíte completa foi rodada pelo dono em
    2026-10-29 e passou inteira; os números parciais de cada etapa estão acima.

---

## Renda fixa (objetivos e aportes)

Pedida pelo dono (2026-10-17), fora da ordem das fases: objetivos financeiros ("caixas") com investimentos em renda fixa
cadastrados à mão, rendimento calculado pelo CDI do Banco Central. Plano completo, fórmulas, fontes e decisões
(D1 a D18) em `docs/renda-fixa.md`; o resgate (R.22 a R.31) em `docs/renda-fixa-resgate.md`; o "Disponível × Investido" (R.32 a R.34, D20) em `docs/renda-fixa.md`, 7.4c (regra de "Investido" substituída pela I.9, D21). Sem integração bancária nem movimentação real.

- [x] **R.0 Verificação** — fórmula da B3 conferida e reproduzida com dois extratos do banco do dono (diferença máxima de
  R$ 0,01); tabela de IOF lida no decreto; tabela de IR pela B3 (texto legal pendente, dispensado por ora).
- [x] **R.1 Motor de rendimento** — `FixedIncomeEngine` no domínio (`Prisma.Domain/FixedIncome`): fator da B3 em ponto fixo
  `Int128`, sem banco nem relógio. Testes com vetores independentes, os extratos do banco e propriedades (CsCheck).
- [x] **R.2 Taxas do CDI** — tabela global `reference_rates` (migration `AddReferenceRates`) e `BcbSgsRateSource`, cliente da
  SGS 12 do Banco Central: janelas de 9 anos, só aceita lista JSON (a API responde erro com 200, 406 ou HTML, e faixa sem dia
  útil com 404), tudo ou nada. Teste de contrato `[LiveFact]` com a API real.
- [x] **R.3 Sincronização** — `ReferenceRateSync` + `ReferenceRateWorker` (19h30 e 23h30 em São Paulo, e ao subir se o histórico
  estiver vazio ou atrasado): idempotente (índice único + releitura), revisão de valor com `RevisedAt` e aviso no log, falha da
  fonte sem efeito, lacuna e defasagem (`ReferenceRateCoverage`, único uso do calendário bancário no CDI, só para diagnóstico).
- [x] **R.4 Objetivos e aportes (API)** — `SavingsGoal` (a caixa) e `FixedIncomeDeposit` (o aporte, `GoalId` opcional) em
  `/savings-goals` e `/fixed-income-deposits`: criar, editar, listar, mover entre caixas, excluir e desfazer; excluir o objetivo
  exige escolher entre manter os investimentos (ficam soltos) ou levá-los junto; `clientRequestId` contra aporte duplicado; `xmin`;
  data do aporte conferida contra o início do histórico do CDI. Exclusão de conta, limpeza dos excluídos e cópia dos dados cobrem
  as tabelas novas. O `npm run gen:api` fica para a R.6, junto com a tela (exige a API rodando com o banco migrado).
- [x] **R.4b Aporte que sai de uma conta** — `funding` opcional no aporte (de que conta saiu, em qual conta de investimento entrou):
  o app cria a transferência junto, na mesma gravação, e o saldo da conta cai e o "Investido" do Resumo sobe, sem virar receita
  nem despesa. Valor e data do aporte financiado ficam travados (transferência não se edita); excluir pergunta se a transferência
  fica ou sai junto; desfazer traz as duas.
- [x] **R.5 Saldo e evolução** — `GET /fixed-income/balances?asOf=` (por aporte, por caixa, soltos e total: principal, saldo bruto,
  rendimento bruto, fator e data da última taxa usada) e `GET /fixed-income/evolution` (série para o gráfico, por caixa, soltos ou
  tudo, `step=day|week|month`). Sempre com o estado do histórico do CDI (`rates`: até que dia, defasado, completo). Sem projeção:
  data futura é recusada. Aporte sem taxa vem sem saldo e o total o conta pelo principal, avisando. Casos de referência (cálculo
  independente e extratos do banco) em `fixed-income-reference.json`.
- [x] **R.6 Interface (bruto)** — aba "Objetivos" em Investimentos: lista com saldo e meta, detalhe com gráfico de evolução e aportes, painéis de objetivo e de aporte (com origem da conta), perguntas ao excluir, avisos de taxa defasada/incompleta. Conferida em 320/390/1280, claro e escuro, com toque real.
- [x] **R.7 Tributação (líquido estimado)** — `FixedIncomeTax` (IOF até 29 dias pela tabela do Decreto 6.306 e IR regressivo 22,5/20/17,5/15% por aporte, sobre o rendimento líquido do IOF), regra versionada por data; o saldo (`/fixed-income/balances`) devolve imposto e líquido por aporte e por grupo; a tela alterna Bruto | Líquido estimado. A tabela do IR segue B3 + dono (o texto legal não foi conferido, por decisão do dono).
- [x] **R.8 Fechamento** — documentação em dia (`CLAUDE.md`: glossário, tabela global, quinta tarefa em segundo plano, calendário bancário, comandos;
  `docs/operacao.md`, seção 4.5; `docs/renda-fixa.md`; `README.md`; `PLAN.md`), aviso do que a renda fixa ainda não faz em toda tela
  que mostra o saldo, conferência da política de privacidade e dos termos (**nenhum texto foi mudado**) e verificação manual do dono na tela, com os números conferidos contra o banco (2026-10-18: tudo certo). Ficou para o dono decidir (e
  para o advogado, A.2): a seção 3.2 da política lista o que se registra em investimentos (notas, proventos, avisos, conferência de
  eventos) e não cita objetivos e aportes em renda fixa (nome e meta do objetivo; data, valor, % do CDI, vencimento e conta de origem
  do aporte); a seção 5 cita a brapi como fonte de dados de mercado e não o Banco Central (que também não recebe dado de ninguém);
  e os termos (seção 4) falam de "preço médio, resultado de vendas, rendimentos" sem a estimativa de imposto da renda fixa. São
  esclarecimentos, não nova finalidade nem novo operador, então não pedem novo aceite por si; a versão só sobe se o dono decidir. **Feito em 2026-10-20 (R.26, a pedido do dono):** os três pontos foram esclarecidos nos textos (e a observação do aporte entrou entre os textos livres), com a versão mantida em 1.0 e o esclarecimento registrado no histórico da política.
- [x] **R.9 Renda fixa na Carteira** — grupo único "Renda fixa" (todos os índices) na aba Carteira: peso pelo aportado, linhas por objetivo, rendimento ao lado, atalho para Objetivos; vale por ano. `PortfolioGroup.FixedIncome`, `FixedIncomeHolding` no `PortfolioSnapshot`, `fixedIncome` em `GET /portfolio`. Conferida em 320/390/1280, claro e escuro.

- [x] **R.10 CDI+ e pré-fixado (domínio)** — `Remuneration` (X % do CDI, CDI + taxa a.a., pré-fixado), `AnnualRate` (0,01 % a 100 % a.a., E4), `FixedPoint` e `PrefixedYield` (`Prisma.Domain.FixedIncome.Prefixed`, dias úteis pelo `BankCalendar`, quarto uso liberado em `BankCalendarUsageTests`). `FixedIncomeEngine.Balance/Series` ganham a sobrecarga com `Remuneration`; as antigas seguem iguais. Vetores independentes (Python/Decimal), oráculo em BigInteger e mutações provadas (`docs/renda-fixa.md`, 5.5 e D14). Sem banco, API nem tela.
- [x] **R.11 CDI+ e pré-fixado (persistência e API)** — `FixedIncomeIndexKind` com `CdiPlus` e `Prefixed`; uma coluna só, `RateE4` (renomeia `IndexPercentE4` sem perder dado, constraint por índice; migration `AddDepositRateAndIndexKinds`, **ainda não aplicada no banco de desenvolvimento**), validação, saldo, evolução, imposto e Carteira; o pré não depende das taxas do CDI nem da data do primeiro dia do histórico; a API só acrescenta (`rateE4` novo, `indexPercentE4` antigo aceito e devolvido no CDI).
- [x] **R.12 CDI+ e pré-fixado (tela e documentos)** — chips "% do CDI | CDI + taxa | Pré-fixado" no aporte (um campo "Taxa" que muda de rótulo e de faixa por índice, sem taxa sugerida fora do CDI), lista com "CDI + 2% a.a." e "14% a.a. (pré)", avisos e notas de rodapé que só falam do CDI quando há aporte que o usa, nota de limitações nova, `gen:api`, `.http`, Vitest (33 no arquivo) e conferência em 320/390, claro e escuro, com toque real; migration aplicada no banco de desenvolvimento. **Pendente (do dono): conferir um CDI+ e um pré com extrato real (tolerância de R$ 0,01**).
- [x] **R.13 LCI e LCA (domínio, banco e API)** — `Lci` e `Lca` em `FixedIncomeProductKind`; `FixedIncomeTax` com regra por produto (`lci-lei-11033-2005`: IR zero e IOF pela tabela; `lca-lei-11033-2005`: IR zero e IOF zero; produto sem regra é recusado); migration `AddLciAndLcaProducts` (constraint do produto; **ainda não aplicada no banco de desenvolvimento**). Esperado dos testes da lei e do decreto (pesquisa de 2026-10-18, `docs/renda-fixa.md` 6.5 e D15), com mutações provadas. A etapa juntou o banco e a API do plano (o produto já viaja no pedido): sem a constraint, a API aceitaria um produto que o banco recusa.
- [x] **R.14 LCI e LCA (tela e documentos)** — seção "Produto" (CDB | LCI | LCA) no aporte; produto nas linhas ("LCI · 90% do CDI"); "Isento de IR" (ou "IOF, isento de IR") no lugar do imposto das letras; aviso de vencimento abaixo de 6 meses (só avisa, `maturityWarning`); nota do imposto das letras no rodapé do líquido; nota de limitações com LCI e LCA; `DepositBalance.productKind` na API (só acrescenta); `gen:api` e migration `AddLciAndLcaProducts` aplicada no banco de desenvolvimento; Vitest (41 no arquivo) e conferência em 390 claro e 320 escuro, com toque real. **Pendente (do dono): conferir uma LCI e uma LCA com extrato real.**

**Admin dos dados de mercado e IPCA (planejado em 2026-10-19; R.15 e R.16 feitas, o IPCA não).** Pedido do dono: disparar à mão a sincronização do
Banco Central (CDI hoje, IPCA depois) por rota e tela de admin, **mantendo** a execução automática nos horários (19h30 e 23h30 de São
Paulo e ao subir), e depois adicionar IPCA e IPCA+. **R.15 e R.16 estão feitas; R.17 a R.21 (IPCA e IPCA+) estão em espera** (ver abaixo). Especificações: `docs/admin-mercado.md` (R.15 e R.16) e `docs/renda-fixa-ipca.md`
(R.17 a R.21: fatos verificados, decisões, cálculo e fases). **Decisões M1 a M6 (admin por lista de e-mails em `Admin:Emails`, histórico
em `market_sync_runs`, execução síncrona, horário automático mantido): M1 e M2 aprovadas pelo dono em 2026-10-19; M3 a M6 seguiram como recomendadas.**

- [x] **R.15 Admin dos dados de mercado (API)** — política `Admin` (e-mails em `Admin:Emails`, falha fechada), `GET /admin/market-data`, `POST …/{série}/sync` e `…/refresh` (janela até 10 anos), `GET …/runs`; tabela global `market_sync_runs` (migration `AddMarketSyncRuns`, sem dado pessoal, 90 dias) escrita também pela tarefa; `SourceErrorCode` (código da página de rejeição do BCB) no `BcbSgsRateSource`; `isAdmin` no `/auth/me`; limite de taxa e log por pedido; teste de arquitetura de rota `/admin`. Pronto quando os testes de `docs/admin-mercado.md`, seção 9, passam e as mutações morrem. **Feita (2026-10-19):** migration `AddMarketSyncRuns` gerada, **ainda não aplicada no banco de desenvolvimento** (pede autorização do dono); `Admin__Emails` precisa estar no Railway antes do merge se o dono quiser a tela no ar.
- [x] **R.16 Admin dos dados de mercado (tela)** — `/admin` (desde a AE.3, `/admin/mercado`; lazy, `noindex`, só para `isAdmin`), entrada no menu da conta, um cartão por série (estado, próxima execução automática, última execução, erro com código), "Sincronizar agora" e "Reprocessar período", lista das últimas execuções; `gen:api`; Vitest; 320/390/1280, claro e escuro. **Feita (2026-10-19):** `features/admin/*` (`adminMarket.ts` com 34 testes), guarda `RequireAdmin` (para quem não é admin a rota é o 404 de sempre), entrada "Dados de mercado" no menu da conta só para admin; conferida com toque real em 320/390/1280, claro e escuro, com o Banco Central de verdade (sincronização manual gravou a execução) e com um erro simulado com código. A migration `AddMarketSyncRuns` foi aplicada no banco de desenvolvimento (com o "OK" do dono).
> **EM ESPERA (decisão do dono, 2026-10-19):** o dono vai buscar dados melhores (extratos reais de CDB IPCA+ e do Tesouro IPCA+) antes de seguir; **não começar a R.17 em diante sem ele pedir**. As cinco etapas abaixo seguem desmarcadas de propósito; o que já foi levantado está em `docs/renda-fixa-ipca.md`. O IPCA e o IPCA+ continuam **recusados** pela API e pela tela até lá.

- [ ] **R.17 IPCA: verificação (sem código)** — **EM ESPERA** (o dono vai buscar dados melhores; as R.18 a R.21 dependem desta) — extrato real de um CDB IPCA+ (e do Tesouro) para decidir a convenção (F1 a F3); confirmar a SGS 1737 contra o IBGE, o IR sobre a correção (F7) e o comportamento do BCB. **Depende do dono.**
- [ ] **R.18 IPCA persistido** — tabela mensal global (variação com sinal e número-índice), fonte do BCB, sincronização irmã da do CDI no mesmo worker, cobertura, logs, série `ipca` no admin.
- [ ] **R.19 IPCA+: domínio do cálculo** — `IpcaPlus`, fator de inflação pela convenção decidida, IPCA+0, `Series`; vetores independentes, oráculo em BigInteger, mutações; o CDI inalterado.
- [ ] **R.20 IPCA+: API** — saldo, evolução, Carteira e estado do IPCA (só acrescenta); `SaveDeposit` confere o primeiro índice conhecido.
- [ ] **R.21 IPCA+: tela e documentos** — 4º chip "IPCA +", campo, avisos ("inflação até <mês>", estimativa pelo contrato), `gen:api`.

**Resgate de renda fixa (R.22 a R.26, feitas em 2026-10-19, com o "de acordo" do dono; `docs/renda-fixa-resgate.md`).** Revê a D5: parcial e total, imposto calculado com o líquido do extrato opcional, contas opcionais (principal como transferência, rendimento líquido como receita), a pessoa escolhe o aporte. Independe do IPCA (em espera).

- [x] **R.22 Resgate: domínio e motor** — `FixedIncomeRedemption` (principal e imposto congelados, líquido informado), `Balance`/`Series` com os resgates do aporte (`RedemptionStep`); oráculo em `BigInteger`, propriedades (CsCheck), bordas e mutações. Feita em 2026-10-19: 30 testes novos em `FixedIncomeRedemptionTests`, 15 mutações provadas. Decisões do dono: as quatro recomendações da seção 8 da spec (taxas ausentes recusam, aporte sem financiamento só registra, só o último resgate se exclui, categoria "Rendimentos").
- [x] **R.23 Resgate: API e migration** — tabela com dono e soft delete, `POST/GET/DELETE` e desfazer de resgates (só o último se exclui), `balances`, `evolution` e objetivos descontando (só acrescenta), contrato do aporte travado; `export-user.sql`, limpeza dos excluídos e exclusão de conta. Feita em 2026-10-19: 19 testes de integração (`FixedIncomeRedemptionApiTests`), 9 mutações provadas (3 sobreviveram no começo e viraram testes). **Migration `AddFixedIncomeRedemptions` aplicada no banco de dev em 2026-10-19 (autorizada pelo dono).** A Carteira segue sem descontar resgates (R.26); `gen:api` entra com a tela (R.25).
- [x] **R.24 Resgate: contas** — transferência do principal (conta de investimento → corrente ou carteira) e receita do rendimento líquido (categoria "Rendimentos"), na mesma gravação; só para aporte que saiu de uma conta pelo app; excluir com "manter ou excluir junto" e desfazer que traz tudo. Feita em 2026-10-19: 10 testes de domínio e 15 de integração (`FixedIncomeRedemptionFundingApiTests`), 16 mutações provadas. **Migration `AddRedemptionFunding` aplicada no banco de dev em 2026-10-19 (autorizada pelo dono).**
- [x] **R.25 Resgate: tela** — painel "Resgatar" (data a partir do último dia possível, valor bruto com "Resgatar tudo", prévia do imposto vinda da API, líquido do extrato opcional, conta onde cai), botão em cada aporte, lista dos resgates no painel do aporte (só o último se exclui; pergunta se os lançamentos ficam), contrato travado, seção "Resgatados", linha "Já resgatado", `gen:api`. Para a prévia sem refazer a regra na tela, a API ganhou `GET /fixed-income-deposits/{id}/redemptions/preview` (com `latestDate`, domínio `ReferenceRateCoverage.LatestRedemptionDate`). Feita em 2026-10-19: Vitest das contas puras, 5 testes de integração da prévia, mutações provadas, conferência visual por toque real em 390, 320 (escuro) e 1280. Nenhuma migration nova.
- [x] **R.26 Resgate: Carteira e documentos** — a Carteira já pesava pelo principal que sobra (lê o mesmo saldo da aba Objetivos, R.23): faltavam a prova por teste (`PortfolioRedemptionTests`: peso, resgate depois do fim do ano não conta, objetivo resgatado por inteiro some) e o contador de aportes da linha, que contava os resgatados por inteiro (corrigido). Documentos varridos (`renda-fixa.md`, `investimentos.md`, `CLAUDE.md`, `README`, `PLAN.md`); a política de privacidade e os termos foram **esclarecidos em 2026-10-20, a pedido do dono** (renda fixa entre os dados registrados, observação do aporte entre os textos livres, Banco Central entre as fontes públicas, linha da estimativa de imposto nos termos), com a **versão mantida em 1.0** (esclarecimento, sem novo aceite); a revisão do advogado (A.2) segue pendente. Feita em 2026-10-19.

**Resgate de renda fixa em lote (R.27 a R.31, feitas em 2026-10-21, com o "de acordo" do dono; `docs/renda-fixa-resgate.md`, seções 15 e 16).** Pedido do dono: um resgate do extrato cobre vários aportes. Decisões: divisão automática do mais antigo ao mais novo **e** ajuste à mão na prévia; entrada no modo "Selecionar" da lista de aportes; uma conta de destino para o lote; líquido do extrato repartido na proporção do imposto estimado. Cada aporte mantém o seu resgate; o lote só os cria juntos, tudo ou nada.

- [x] **R.27 Resgate: "Resgatar tudo" como modo** — corrige o achado de 2026-10-21 (o valor copiado não acompanhava a mudança de data) e sugere "tudo" quando o valor fica a até R$ 0,05 do saldo. Feita em 2026-10-21: `allGross` e `nearlyAll` em `fixedIncome.ts` (5 testes novos), `RedemptionSheet` usa o modo; Vitest 563 verdes, lint e tipos limpos. **Falta a conferência com toque real do dono.**
- [x] **R.28 Lote: domínio** — `RedemptionAllocator` (ordem, parcial no último, excedente, repartição do líquido), testes de propriedade. Feita em 2026-10-21: `RedemptionAllocator` (`Allocate`, `TotalBalance`, `DistributeNet`) em `Prisma.Domain/FixedIncome`, 20 testes (`RedemptionAllocatorTests`, com o caso do extrato do dono e 2 propriedades CsCheck), 7 mutações provadas (1 sobreviveu no começo e virou teste). `dotnet test` verde (Domínio 1147, Arquitetura 17, API 1025). Sem banco, API nem tela.
- [x] **R.29 Lote: API** — prévia e gravação em lote, tudo ou nada, contas, duplicidade, desfazer lote; `.http` e `gen:api`. Feita em 2026-10-21: `RedemptionBatches` (prévia, gravação e `undo`), `Redemptions.Handler.Fund` virou `internal static` para os dois usarem a mesma regra de contas, eventos `RedemptionBatchAdded` (4319) e `RedemptionBatchUndone` (4320), `fixed-income.http` e `api-types.ts` regenerados (só acrescenta). 22 testes de integração (`FixedIncomeRedemptionBatchApiTests`), cada pedaço conferido contra a prévia do resgate de um aporte só; 6 mutações provadas (a do `Touch` do aporte sobreviveu e virou o teste de dois lotes ao mesmo tempo). `dotnet test` verde (Domínio 1147, Arquitetura 17, API 1047). **Sem migration** (só usa `fixed_income_redemptions`).
- [x] **R.30 Lote: tela** — modo "Selecionar", painel do lote, prévia com ajuste. Feita em 2026-10-21: modo "Selecionar" na lista de aportes do objetivo (linha vira caixa de seleção, "Selecionar todos"/"Limpar", "Resgatar N aportes"), `BatchRedemptionSheet` (data, valor com "Resgatar tudo" como modo, aportes do mais antigo ao mais novo com caixa para desmarcar e o valor de cada um, totais da API, líquido do extrato, uma conta), `redemptionBatch.ts` (12 testes), consultas novas e "Desfazer" do lote no aviso. Vitest 575, tipos, lint e build limpos. Conferida por mim no Chrome com toque real e API paralela (390 claro, 320 escuro, 1280 claro; sem rolagem lateral; registrar, parcial, desmarcar e desfazer). **Falta a conferência do dono**, em especial no celular.
- [x] **R.31 Lote: documentos** — varredura de tudo o que fala de resgate: `CLAUDE.md` (glossário com a linha do lote, árvore de `docs/`, a tela), `PLAN.md`, `README.md`, `docs/renda-fixa-resgate.md` (estado e seção 16), `docs/renda-fixa.md` (estado, D18 corrigida, nova D19, itens 19 e 20, seção 13: o resgate de um aporte já está na `main` pelo PR #23, e as contagens de testes), `docs/renda-fixa-ipca.md` (o "Depois" já não lista resgates) e a memória. Conferidos e **sem mudança**: `docs/operacao.md` e `docs/investimentos.md` (nenhuma tabela nova: exclusão de conta, limpeza do excluído e `export-user.sql` já cobrem `fixed_income_redemptions`) e a política e os termos (mesmos dados do resgate de um aporte; versão 1.0 mantida). Feita em 2026-10-21.
**Disponível × Investido nas contas (R.32 a R.34, feitas em 2026-10-21; D20 em `docs/renda-fixa.md`; a regra de "Investido" foi substituída pela I.9 em 2026-10-28, D21: o que segue é o histórico).** Achado do dono: aportou R$ 10 mil da conta (Itaú → Íon) e não tem a visão do que sobrou, porque o "Em contas" soma dinheiro livre e investido e a Íon parece caixa. O saldo da conta não muda; muda a leitura. Decisões do dono: somar o rendimento ao "Investido" (o bruto, como a Carteira).

- [x] **R.32 Investido: API** — `GET /fixed-income/balances` ganha `byAccount` (rendimento bruto do que sobra, por conta de investimento, só de aporte financiado por conta com as pontas vivas); só acrescenta; teste de integração (aporte financiado, aporte só acompanhado, ponta excluída, resgate parcial, isolamento por usuário); `.http` e `gen:api`. Feita em 2026-10-21: `AccountYield`/`byAccount` em `GetBalances` (campo novo, só acrescenta), `Fundings.LiveFundings` extraído de `Respond` (mesma regra de "pontas vivas"); `FixedIncomeInvestedByAccountApiTests` (7: aporte financiado, só acompanhado, soma por conta, resgate parcial, aporte excluído, transferência excluída à parte, isolamento; oráculo = rendimento do próprio aporte; mutações da contagem e do filtro de pontas vivas pegas, e a segunda só depois de um teste novo); `.http` e `api-types.ts` (+9 linhas). Suíte: Domínio 1147, Arquitetura 17, API 1054 (7 ignorados).
- [x] **R.33 Investido: tela** — "Em contas" do Resumo em Disponível + Investido (com o rendimento), conta de investimento na lista de Contas como "Investido" com o rendimento ao lado; Vitest da conta pura; conferido em 320/390/1280, claro e escuro. Feita em 2026-10-21: `splitBalances` em `dashboard/upcoming.ts` (6 testes; 3 mutações pegas), `TodayPanel` ("Em contas" vira Disponível + Investido quando há conta de investimento; sem ela, igual a antes), `AccountsPage` (conta de investimento mostra "Investido" e "Com R$ X de rendimento"), `invalidateMoney` passa a recarregar `fixedIncomeKey`. Conferido em 320/390/1280, claro e escuro, com usuário descartável (R$ 10.000 aportados, R$ 211,96 de rendimento): dois defeitos achados e corrigidos (valor cortado no Resumo, texto sobre o valor em Contas a 320). Vitest 581, tsc, oxlint e build limpos.
- [x] **R.34 Investido: documentos** — varredura de tudo o que fala de saldo, "Em contas" e "Investido". Feita em 2026-10-21: `CLAUDE.md` (glossário, árvore de `docs/`, a tela), `README.md`, `docs/renda-fixa.md` (estado, 2.4c, nova 7.4c, §13 com as contagens 2218 de .NET e 581 do Vitest), `docs/fase-2.md` 2.4, `docs/renda-fixa-resgate.md` (o lote já está commitado na branch), `PLAN.md` e a memória. Conferidos sem mudança: `docs/investimentos.md` (a carteira de ações não mexe em saldo), `docs/operacao.md` (sem tabela nova) e os textos legais (nenhum dado novo guardado: `byAccount` é calculado).

---

## Admin: pessoas inativas e e-mail em massa (`docs/admin-email.md`)

Pedido do dono (2026-10-29): achar quem não entra há algum tempo e mandar um e-mail, vendo antes a prévia. O desenho
inteiro, as decisões da conversa e o que segue em aberto estão em `docs/admin-email.md`; **só a AE.1 está aprovada**.
O restante (contagens no admin, descadastro, segmentos, campanha com prévia, disparo) vira etapa quando o dono decidir
a base legal e a redação da política.

- [x] **AE.1 Último acesso** — `users.last_seen_at` carimbado no primeiro pedido autenticado de cada dia civil em São
  Paulo (não só no login: o cookie é persistente), por um UPDATE condicional atrás de uma memória do dia
  (`ILastSeenCache`, hoje em memória do processo; Redis descartado por ora).
  *Pronto quando:* carimba o instante do relógio no primeiro pedido; mesmo dia não muda; dia seguinte carimba; a virada é
  à meia-noite de São Paulo, não de UTC; uma pessoa nunca mexe em outra; a memória poupa o banco; reinício só perde a
  memória; falha ao carimbar não derruba o pedido e vira aviso; a migration carimba quem confirmou o e-mail e deixa vazio
  quem não confirmou.
  *Feita (2026-10-29/30):* `LastSeenStamper` e `UseLastSeen` (`Infrastructure/Auth/LastSeen.cs`), `ILastSeenCache` e
  `InMemoryLastSeenCache`, aviso `LastSeenStampFailed` (2015), migration `AddUserLastSeenAt`, `ultimoAcessoEm` em
  `ops/privacy/export-user.sql`, consultas em `docs/consultas-saude.md`. `LastSeenTests` (9) e `LastSeenMigrationTests`
  (1), provados com 4 mutações (dia em UTC, sem lembrar, sem o `catch`, sem a condição do dia), cada uma pega por um teste. Suíte completa verde (2026-10-30): Domínio 1198, Arquitetura 17, API 1106 (7 ignorados).
  **Decisão do dono (2026-10-30):** o último acesso não é citado na política de privacidade; a versão do texto fica como
  está. O assunto volta se a política for revista (A.2) ou se o dado passar a ser usado para e-mail de relacionamento.
- [x] **AE.2 Contagem de pessoas (API)** — `GET /admin/users/summary?days=N` (N de 1 a 365, padrão 30, escolhido pelo dono, sem
  janelas fixas): `accounts`, `confirmed`, `unconfirmed`, `active` (vistas há menos de N dias), `inactive` (há N dias ou mais) e
  `neverUsed`; só números, nunca quem é. Dia civil de São Paulo; a regra é `ActivityWindow` no domínio (a mesma servirá ao
  segmento do e-mail). Uma agregação em SQL, na política `Admin`.
  *Pronto quando:* `active + inactive + neverUsed = confirmed` para qualquer N; fronteira de N−1 e N dias; a virada é à meia-noite
  de São Paulo, não de UTC; N fora de 1 a 365 dá 400 `InvalidDays`; sem N vale 30; 401 e 403; a resposta só tem números.
  *Feita (2026-10-30):* `ActivityWindow` (`Prisma.Domain/Accounts`), `GetUserSummary` e `UserSummaryResponse`
  (`Features/Admin`), `.http`, `api-types.ts` regenerado (+80 linhas, nenhuma removida; API paralela na 7161 com as tarefas
  desligadas). `ActivityWindowTests` (13, com uma propriedade em CsCheck: comparar com `ActiveSince` dá o mesmo que contar
  dias civis) e `AdminUsersApiTests` (15). Provados com 6 mutações (dia em UTC, erro de um dia, ativa e inativa sem exigir
  e-mail confirmado, nunca-usou sem exigir confirmado, não confirmadas erradas); duas sobreviveram a princípio e pediram um
  caso novo (conta sem o e-mail confirmado mas com acesso gravado). **Log:** `UserSummaryRequested` (2016, `userId` e `days`,
  nunca os números; não sai em pedido recusado), com 2 testes e 3 mutações (sem o log, id errado, log antes de validar); o
  `LastSeenStampFailed` (AE.1) passou a ter o `userId` do escopo conferido por teste. `docs/operacao.md` 4.6 e `docs/admin-email.md` 2.1. Suíte completa verde (2026-10-30): Domínio 1211, Arquitetura 17, API 1123 (7 ignorados).
- [x] **AE.3 Admin como entrada + tela de Pessoas** — `/admin` vira uma página de entrada com as áreas (cartões "Dados de mercado"
  e "Pessoas"); a tela de mercado de hoje passa para `/admin/mercado` e a nova fica em `/admin/pessoas` (campo "dias sem acesso"
  com atalhos de 7, 30 e 90 que só preenchem o campo, aviso de que o filtro só é confiável N dias depois do deploy). O menu da
  conta troca "Dados de mercado" por "Administração". Rotas `lazy`, guarda `RequireAdmin`. *Pronto quando:* Vitest do que é
  texto e validação do campo, e conferência visual em 320, 390 e 1280, claro e escuro, com toque real.
  *Feita (2026-10-30):* `AdminHomePage` (entrada), `AdminPeoplePage` (campo, atalhos 7/30/90, contagem, aviso de confiabilidade),
  `AdminBack`, `adminPeople.ts` (validação e textos), `useUserSummary` (`keepPreviousData`, sem consultar com campo inválido),
  chave `adminPeopleKey`, rotas `/admin`, `/admin/mercado` e `/admin/pessoas`, menu "Administração" (`ShieldCheck`). A tela de
  mercado só ganhou o "voltar". `adminPeople.test.ts` (20), provado com 5 mutações (limite superior, sinal, plural, espaços, "hoje").
  Conferida por mim no Chrome com toque real e API paralela (7160, banco descartável, tarefas desligadas): 320, 390 e 1280, claro e
  escuro, sem rolagem lateral; o toque em "7 dias" recalcula; um defeito achado e corrigido (com o campo inválido ficavam na tela os
  números do N anterior, esmaecidos). Tipos, lint e build limpos; Vitest 607. **Falta a conferência do dono**, no celular.
- [x] **AE.4 Disparos de e-mail pelo admin (mecanismo, backend)** — pedido do dono (2026-10-31): várias formas de disparo, não só a
  confirmação. Um disparo = **um modelo + um público**: `confirm-reminder` (`ConfirmEmailReminder`, categoria **conta**: não confirmou
  há N dias, um lembrete por conta, para sempre) e `miss-you` (`MissYou`, categoria **relacionamento**: confirmou e está sem acesso há
  N dias, intervalo de 30 dias), que já existe mas **não sai para ninguém** até o descadastro. API `/admin/emails`: `templates`,
  `audience` (só a contagem e o teto), `preview` (dados de exemplo), `test` (só para o endereço do próprio admin, `[Teste]`), `send`
  e `campaigns` (histórico). Travas (`CampaignGuard`, domínio): categoria, alguém no público, teto por disparo (50), a contagem que
  o dono viu (`expectedCount`, senão 409); ninguém duas vezes (`email_campaign_recipients`); `clientRequestId` idempotente; lock do
  Postgres por modelo; disparo, destinatários e fila numa transação; e-mails escalonados na fila (5 s) para não atrasar senha nem
  estourar a cota do Resend; limite `admin-email` por pessoa. Detalhes em `docs/admin-email.md`, 3.3.
  *Pronto quando:* o público de cada modelo segue a regra de dias civis de São Paulo; recusa categoria bloqueada, público vazio, teto e
  contagem velha; nada sai duas vezes (nem em corrida); o teste vai só ao dono; o log traz quem pediu e nunca endereço.
  *Feita (2026-10-31):* domínio (`Prisma.Domain/Emails`: `CampaignAudience`, `CampaignCooldown`, `CampaignGuard`) com 27 testes e
  7 mutações pegas; API: `EmailCampaignCatalog`, `CampaignAudienceQuery`, `CampaignEmails` (textos e os montadores
  `ConfirmEmailReminderComposer` e `MissYouComposer`), seis casos de uso em `Features/Admin`, tabelas globais `email_campaigns` e
  `email_campaign_recipients` (migration `AddEmailCampaigns`), `Email:Campaigns`, política `admin-email`, eventos `EmailCampaignSent`
  (5009), `EmailCampaignRefused` (5010), `EmailTestSent` (5011) e `EmailTestFailed` (5012), `admin-email.http`, `api-types.ts` (+476
  linhas, nenhuma removida). `AdminEmailCampaignsApiTests` (27, num banco próprio por teste, porque o compartilhado tem contas não
  confirmadas de outros testes), provados com 9 mutações (sem lock, sem filtrar quem já recebeu, sem gravar destinatários, sem
  escalonar, relacionamento liberado, público sem exigir "não confirmado", montador sem pular quem confirmou, contagem esperada ignorada);
  uma sobreviveu a princípio (o lock: duas chamadas simultâneas acabam em série) e pediu um teste determinístico que segura o lock.
  Os dois e-mails entram na prévia (`EmailPreviewTests`) e foram conferidos no Chrome, claro e escuro.
  Suíte completa verde (2026-10-31): Domínio 1238, Arquitetura 17, API 1150 (7 ignorados); Vitest 607.
- [x] **AE.5 Tela dos disparos** — `/admin/emails` (cartão "E-mails" na entrada do admin): escolher o e-mail, os dias (com atalhos do
  modelo: 3, 7 e 14 para a confirmação; 30, 60 e 90 para o "sentimos sua falta"), a contagem do público com o teto, a prévia num quadro
  isolado (`sandbox` sem scripts), "enviar teste para mim", "enviar para N pessoas" com a confirmação da contagem exata e o histórico.
  Modelo de relacionamento aparece com um cadeado e "só prévia e teste por enquanto": o botão de enviar fica desabilitado e diz por quê.
  *Pronto quando:* a tela só deixa enviar o que o servidor aceitaria (modelo liberado, dias válidos, público entre 1 e o teto); a
  confirmação carrega a contagem que o dono viu e uma chave de pedido estável; uma contagem velha (409) mostra o motivo e atualiza;
  conferência visual em 320, 390 e 1280, claro e escuro, com toque real.
  *Feita (2026-10-31):* `AdminEmailsPage`, `adminEmails.ts` (regras e textos: `sendState`, `confirmQuestion`, `sendErrorMessage`,
  `campaignLine`, atalhos por modelo), consultas em `queries.ts` (o teste responde 204 e não passa pelo `unwrap`), chave
  `adminEmailsKey`, rota `/admin/emails` (lazy, 10 kB, fora do pacote principal) e o cartão em `AdminHomePage`. `adminEmails.test.ts`
  (25), provado com 7 mutações; uma sobreviveu (o ramo do 429 coincidia com a mensagem padrão do cliente) e pediu um teste que exige a
  mensagem própria da tela. Conferida por mim no Chrome com toque real e API paralela (7160, banco descartável, tarefas desligadas):
  320, 390 e 1280, claro e escuro, sem rolagem lateral; teste enviado (204, aviso na tela e o evento no log), confirmação "Enviar … para
  8 pessoas? … não dá para desfazer", envio (8 e-mails na fila escalonados em 35 s, 8 destinatários, 1 disparo), público a zero depois,
  histórico com a hora de São Paulo; modelo bloqueado com o botão desabilitado e o motivo. Tipos, lint e build limpos; Vitest 632.
  **Falta a conferência do dono**, no celular. No desenvolvimento o logo do cabeçalho do e-mail aparece quebrado na prévia porque vem de
  `App:PublicUrl` (o Vite na 5173, que não estava de pé); na produção é o domínio.
- [ ] **AE.6 Descadastro — EM ESPERA por decisão do dono (2026-10-31), não implementar sem ele pedir** — preferência por pessoa no Prisma, link assinado sem login, `List-Unsubscribe` e `List-Unsubscribe-Post`;
  depende da base legal do e-mail de relacionamento (legítimo interesse ou consentimento) e de revisar a política.
- [ ] **AE.7 Liberar o "sentimos sua falta" — EM ESPERA (depende da AE.6)** — `UnsubscribeAvailable` verdadeiro, rodapé de descadastro no texto, e o teste de que o
  e-mail de relacionamento sai só com ele.

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

- **Tela de categorias** (2026-09-28): a API já cria, renomeia, recolore e exclui categorias, mas o app
  só as lista para escolher ao lançar; ninguém cria uma categoria própria sem mexer no banco. Adiada
  pelo dono depois da 2.23. Para acrescentar uma categoria para todos, use o catálogo
  (`DefaultCategories`: suba `Version` e marque as novas; `docs/fase-2.md`, 2.11).
- **Revisão do código de categorias** (2026-09-28, propostas fora da 2.23): excluir categoria conferindo
  com `AnyAsync` em vez de contar tudo; nome único entre irmãs que ignora maiúsculas mas não acentos
  ("Agua" e "Água" convivem); validade maior da lista de categorias no front (muda pouco); categoria sem
  ícone conhecido mostra o círculo tracejado, que lê como "carregando" (trocar por um ícone neutro).

- **Premissas do produto a validar** (2026-09-27): `docs/validacao-premissas.md` lista 15 premissas
  (valor, usabilidade, viabilidade, técnica), com consultas SQL prontas, filtros de log e o roteiro das
  sessões com o pai e a noiva. As mais arriscadas: lançar na hora da compra, pagamento de fatura lançado
  como despesa e dias do cartão. A produção sem backup deixou de ser risco com a H.2b (2026-10-02); seguem
  sem homologação e com migrations direto na produção. Resultados entram na tabela do
  documento e, se mudarem o produto, viram decisão aqui.

- Envio de fatura para LLM externo é opt-in explícito do usuário? (Fase 4)
- Acompanhar rentabilidade de investimento ou só aporte? Respondida em parte pela Fase I: carteira, proventos,
  operações, preço médio e resultado das vendas (I.7). Segue em aberto o valor de hoje (cotação, I.7d).
- Hospedagem para cadastro público: o Railway basta, ou os dados devem ficar numa região no
  Brasil por causa da LGPD? Qual orçamento mensal? (antes de abrir cadastro público)
- Política de privacidade e termos de uso, por causa da LGPD. (antes de abrir cadastro público)
- Apagar a própria conta (LGPD, direito de eliminação). Não existe hoje. (antes de abrir cadastro público)
- Página de apresentação (`docs/landing.md`, 2026-10-12 e 13, branch `feat/landing-page`): feita e fora do ar. Aparece
  sozinha em `/` quando o cadastro abrir (`Registration:Open`); antes disso, o rodapé dela ganha os links de
  privacidade, termos e contato. Em aberto lá (seção 7): o nome visível do app ("Prisma" → "Prisma Money", com os
  vídeos regravados), "não vendemos seus dados" e como medir quem chega.
- Domínio `prismamoney.com.br`: no ar desde 2026-10-13 (comprado na GoDaddy, DNS na Cloudflare, site no Railway;
  `docs/operacao.md`, seção 7). Recebimento do `contato@` e envio pelo Resend (aprovado em 2026-10-13):
  `docs/operacao.md`, seção 8, e etapas H.4.
- Confirmação de e-mail no cadastro, esqueci a senha, trocar a senha e o contador de senhas erradas sem prazo:
  viraram as etapas H.4a a H.4e (2026-10-13, `docs/email.md`). Todas feitas: a H.4d (trocar a senha logado) e a H.4e
  (contador com prazo) em 2026-10-15.
- Login com Google (antiga etapa 1.4), adiado. Critério original: entrar por e-mail e depois
  por Google cai na mesma conta. Atenção: sem confirmação de e-mail, juntar contas pelo
  e-mail permite pré-sequestro de conta. Regra proposta: só juntar se o Google marcar o
  e-mail como verificado; se a conta local nunca confirmou o e-mail, remover a senha e trocar
  o `security_stamp` ao juntar. (quando o login social voltar à pauta)
- **Cartão que fecha N dias antes do vencimento** (em discussão, 2026-09-27). O cartão Itaú do dono
  vence sempre no dia 4 e fecha 7 dias antes (antes eram 10; o banco muda a regra); a compra feita no
  dia do fechamento já vai para a fatura seguinte; vencimento em dia não útil vai para o próximo dia
  útil. O modelo atual (dia fixo de fechamento, compra do dia do fechamento entrando na fatura) não
  representa isso: nenhum dia fixo acerta todos os meses. Evidência em `docs/validacao-premissas.md`,
  seção 11. Decidido: etapas 2.20 a 2.22 (seção "Faturas alinhadas ao banco").
- Mudar o dia de fechamento ou de vencimento do cartão não recalcula as faturas já criadas
  (inclusive as futuras, abertas pelas parcelas). Proposta: recalcular as não editadas e não
  pagas, e o `SettlementDate` das parcelas delas; `DatesEditedManually` já permite distinguir.
  (quando fizer falta)
- Cancelar as parcelas futuras de uma compra parcelada estornada (a 2.5 faz só o crédito único).
  (quando fizer falta)
- Cartão × conta corrente (conferido em 2026-09-26): o cartão não pertence a uma conta, e está certo;
  cada fatura escolhe de onde sai o dinheiro (qualquer conta ativa que não seja cartão; Pix, boleto,
  débito ou TED). Melhorias futuras: conta de pagamento padrão no cartão (opcional, pré-selecionada
  no "Pagar fatura") e débito automático de verdade (registrar o pagamento sozinho no vencimento,
  a partir dessa conta; exige tarefa agendada no backend e regra para fatura que muda depois do
  fechamento). (quando fizer falta)
- **Débito automático, o que ficou fora da 2.26** (2026-09-30): filtro "Débito automático" na lista de
  lançamentos e análises só dos débitos (o tipo gravado na série já permite); cadastrar um débito automático
  novo sem lançar o primeiro débito (hoje a série nasce de um lançamento, e o primeiro não vai para o sino);
  conferir o valor antes do dia do débito; feriado estadual e municipal no calendário. (quando fizer falta)
- Trocar o cartão de uma compra entre cartão e Pix ou débito (fora da 2.24, que é só cartão para
  cartão): a compra ganharia ou perderia fatura, e a parcelada teria de virar à vista. (quando alguém pedir)
- Aviso ativo de versão nova: a API devolve a versão num cabeçalho (ex.: `X-App-Version`) e a tela,
  ao ver uma diferente, oferece "Versão nova disponível · Recarregar". Hoje a aba antiga só percebe
  quando falta um arquivo de tela (`lib/crash.ts`). (se virar PWA instalado ou app nativo, ou se uma
  mudança não puder ser compatível)
- Textos antigos vistos na 2.24: a recusa "Esta despesa tem estornos; o tipo e a conta não mudam." usa
  ";" (sugestão: "Esta compra tem estorno. O tipo e a conta não mudam."); a compra sem categoria mostra
  o círculo tracejado, que lê como "carregando" (`CLAUDE.md`, 7.1). (numa revisão de textos e ícones)
- **Ícones de marca, o que ficou fora da 2.27** (2026-09-30): ícone do Itaú Personnalité (hoje mostra o
  do Itaú; falta o SVG), Inter e C6 (seguem como monograma); escolher a marca numa lista ao criar a conta
  (para nome que não diz a marca, ex.: "Cartão preto"; o banco guardaria só uma chave, e a lista poderia
  separar banco e corretora pelo tipo da conta); "caixa" casa em qualquer texto, então uma carteira
  chamada "Caixa" mostra o ícone da Caixa Econômica; nas listas, o leitor de tela lê o nome da marca duas
  vezes (ícone e texto); contorno no modo escuro para os ícones de fundo escuro (Safra, Modalmais, Íon):
  o dono preferiu sem; crédito ao autor do arquivo de ícones, se a licença pedir. (quando fizer falta)
- Editar as datas de uma fatura lê o cartão inteiro (`CardLedger`), embora o recálculo pule as compras
  com parcela em fatura paga. Medido em 2026-09-30 (Release, mediana): cartão de 5 anos (3.229
  lançamentos, 93% em faturas pagas) 9 ms na prévia e 25 ms ao salvar; caso extremo (17.747) 58 e 142 ms;
  o recálculo em si fica abaixo de 0,5% da CPU. Proposta: ler só as compras com alguma parcela em fatura
  aberta (e todas as parcelas delas), com teste de que o resultado é o mesmo. (se salvar passar de ~100 ms)
- Lista de lançamentos com milhares de linhas num mês (medido em 2026-09-29 com o usuário de carga,
  4.792 linhas em setembro, no Mac, modo de desenvolvimento): a linha memoizada levou abrir o painel de
  ~760 para ~400 ms e fechar de ~585 para ~225 ms; mês real (~90 linhas) fica em ~60 e ~15 ms. O que
  sobra é o navegador recalculando o estilo de todos os elementos quando o painel trava o fundo
  (`content-visibility: auto` testado: não ajudou). Só diminuir as linhas na página resolve:
  virtualização (`@tanstack/react-virtual`, dependência nova) ou paginação por cursor no servidor
  (`?after=`, índice data + id; sem cortar parcelas de uma compra nem pernas de uma transferência;
  revisar o filtro por categoria, que soma como o Resumo). (quando um mês real passar de centenas de
  lançamentos, como na importação da Fase 4, ou no app nativo)
