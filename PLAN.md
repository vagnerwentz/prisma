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

- [ ] **H.2b Backup fora do Railway**
  O backup nativo do Railway (se o plano incluir) só restaura no mesmo projeto e some junto com o
  volume. Serviço agendado no próprio projeto: `pg_dump` diário pela rede privada, criptografado com
  `age` (chave pública no serviço, privada só com o usuário) e enviado ao Cloudflare R2. Roteiro de
  restauração em `docs/`.
  *Pronto quando:* um dump do R2 foi descriptografado, restaurado no Postgres local e conferido
  (contagens e o resumo de um mês iguais aos da produção).

- [ ] **H.2c Operação**
  Roteiro para senha esquecida (sem e-mail ainda), monitor de disponibilidade em `/api/health`,
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

- [ ] **H.3b Erros do navegador no servidor**
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
- Cartão × conta corrente (conferido em 2026-09-26): o cartão não pertence a uma conta, e está certo;
  cada fatura escolhe de onde sai o dinheiro (qualquer conta ativa que não seja cartão; Pix, boleto,
  débito ou TED). Melhorias futuras: conta de pagamento padrão no cartão (opcional, pré-selecionada
  no "Pagar fatura") e débito automático de verdade (registrar o pagamento sozinho no vencimento,
  a partir dessa conta; exige tarefa agendada no backend e regra para fatura que muda depois do
  fechamento). (quando fizer falta)
