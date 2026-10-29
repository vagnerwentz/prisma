# Renda fixa (objetivos e aportes) — plano

> **Estado (2026-10-21): implementado, da R.0 à R.16 (CDI, CDI+, pré-fixado, LCI e LCA, admin dos dados de mercado), o resgate (R.22 a R.26) e o resgate em lote (R.27 a R.31), ambos em `docs/renda-fixa-resgate.md`, e o "Disponível × Investido" nas contas (R.32 a R.34, D20, cuja regra de "Investido" a I.9 substituiu em 2026-10-28: `docs/patrimonio.md`, D21); IPCA e IPCA+ (R.17 a R.21) EM ESPERA.** Este arquivo nasceu como o plano (proposta revisada com o retorno do dono em
> 2026-10-17: D1–D12 aceitos com a recomendação, exceto a D3, reaberta, e o objetivo como "caixa", seção 2.2) e agora também é o registro do que foi feito:
> o texto das seções 1 a 12 é o plano e as decisões; as partes marcadas **Implementado** dizem como ficou; a seção 13 é o resumo para retomar.
> O que ainda falta: texto legal do IR por conferir (dispensado pelo dono), IPCA e IPCA+ (em espera), cotação/projeção (seção 12, item 14). Os resgates foram feitos (D18 e D19).
>
> Legenda usada em todo o texto: **[V]** fato verificado (com a fonte), **[H]** hipótese ainda sem prova,
> **[D]** decisão que depende do dono.

---

## 1. O que existe hoje e importa para esta funcionalidade

Tudo abaixo foi lido no repositório (nada presumido).

| Assunto | Como é hoje [V] | Consequência para a renda fixa |
|---|---|---|
| Stack | .NET 10, Minimal APIs, EF Core 10 + Npgsql (snake_case), Postgres 18, React 19 + Vite + TanStack Query + Recharts, Railway | nenhuma dependência nova é necessária |
| Arquitetura | `Prisma.Domain` (zero dependência externa, teste NetArchTest) + `Prisma.Api/Features` (uma vertical slice por arquivo: request, validador, handler, endpoint) | o motor de cálculo e a tributação vão no domínio, puros e sem banco |
| Dono dos dados | toda entidade herda `Entity` (`UserId`, soft delete) e o `AppDbContext` aplica o filtro de dono; `OwnershipTests` recusa tabela sem dono que não esteja em `AppDbContext.GlobalTables` | tabela do usuário herda `Entity`; a tabela do CDI é global e entra na lista, com o motivo |
| Dinheiro | `Money(long Cents)`; regra inviolável 1: **nunca `decimal`/`double` para dinheiro** | ver 5.4: taxas e fatores em inteiro escalado, como já se faz em `TargetUnitPrice`/`CashPerUnit` (10⁻⁸ real) |
| Datas | `DateOnly`, `IClock`, fuso `America/Sao_Paulo`; teste de arquitetura proíbe ler o relógio do sistema | aporte e data de referência são `DateOnly`; "hoje" é `IClock.Today` |
| Dados de mercado | tabelas globais sem `UserId` (`Asset`, `CorporateEvent`), escritas só por tarefa; exceção registrada em `CLAUDE.md`, regra 6 | o CDI segue o mesmo molde |
| Tarefa agendada | `AssetListWorker` (`BackgroundService`, uma vez por dia às 4h de São Paulo, também ao subir se o catálogo estiver vazio, falha nunca derruba a API, escopo de DI novo a cada execução), `AssetSyncSchedule.NextRun` | o `ReferenceRateWorker` repete o molde; sem Hangfire/Quartz |
| Fornecedor externo | `HttpClient` tipado do `IHttpClientFactory` atrás de uma interface (`IAssetListSource`), sem pacote de retentativa (a execução seguinte é a retentativa), testes com `HttpMessageHandler` falso + fixtures reais, contrato real em `[LiveFact]` | `IReferenceRateSource` + `BcbSgsRateSource` |
| Logs | só `ILogger`; todo evento em `AppLog.cs` com `[LoggerMessage]`; nunca logar valor, descrição, nome | eventos novos na faixa própria; só ids, séries e datas |
| Apagar conta / cópia dos dados | `AccountEraser` (SQL por `user_id`), `ops/privacy/export-user.sql`, testes de schema (`AccountDeletionTests`) acusam tabela nova com `user_id` esquecida | tabelas novas entram nos dois lugares |
| Testes | xUnit + Shouldly + CsCheck (domínio), Testcontainers Postgres 18 (integração), NetArchTest; **sem teste E2E** (Playwright é a Fase 5) | o "ponta a ponta" do pedido é a integração por slice (API + Postgres real); Playwright fica fora, ver 9 |
| Interface | `/contas/investimentos` (`InvestmentsPage`, abas Carteira/Proventos/Notas, Radix Tabs), `HistoryChart` (Recharts), `.surface`, `BottomSheet`, `invalidateMoney`, identidade visual "espectro" (`CLAUDE.md`, 7.1) | nova aba/tela segue esses componentes |
| Conta de investimento | `AccountType.Investment` existe; aporte hoje é **transferência** entre contas (regra 5) e o Resumo mostra "Investido" | **risco de contar duas vezes**, ver D3 |
| Objetivos/metas, renda fixa | **não existem** (busca no código, `docs/` e `PLAN.md`; só a "Fase 5 — metas" em uma linha) | tudo é novo |
| Calendário | `BankCalendar` (feriados nacionais + Carnaval + Corpus Christi + Sexta-feira Santa); teste de arquitetura restringe o uso à série de débito automático e ao vencimento da fatura | só serviria para diagnosticar lacuna do CDI; exigiria ajustar esse teste (D8) |
| Limite de infraestrutura | um serviço de API no Railway; já roda 3 tarefas em segundo plano; Postgres único | uma tarefa diária a mais é irrelevante; ver 3.5 |

Convenções a seguir: código em inglês, documentação/UI/erros em pt-BR, `Result` para erro de negócio, endpoints em inglês e no plural,
atualizar o `.http` do endpoint e rodar `npm run gen:api` ao mudar a API, e **adicionar dependência fora da tabela do `CLAUDE.md`
exige perguntar** (este plano não pede nenhuma).

---

## 2. Domínio e modelo de dados

### 2.1 Conceitos (e por que não são uma entidade só)

| Conceito | Regra de negócio própria | Vira entidade? |
|---|---|---|
| **Objetivo** ("Casamento") | nome, meta opcional; organiza e soma | sim: `SavingsGoal` |
| **Aporte** (lote) | data, valor, taxa (% do CDI, CDI + taxa ou pré), produto, vencimento, liquidez; cada um tem o seu prazo de IR e o seu rendimento | sim: `FixedIncomeDeposit` |
| **Estratégia/produto** | tipo (CDB pós), índice (CDI), % contratado | **não** como tabela: são campos do aporte (cada aplicação real é um contrato; o "mesmo CDB" com 100% e outro com 110% não cabem numa estratégia única do objetivo). O objetivo guarda só o **padrão** para pré-preencher o formulário |
| **Taxa de referência** (CDI) | dado de mercado, global, imutável na prática | sim: `ReferenceRate` (global) |
| **Regra tributária** | tabela de IR/IOF por prazo, muda por lei | **não** como tabela: código versionado no domínio (ver 6.4) |
| **Resgate** | tira principal e dispara IR/IOF reais | **feito (R.22 a R.26, e em lote nas R.27 a R.31)**: ver D18, D19 e `docs/renda-fixa-resgate.md` (a D5 o deixava fora da v1) |

### 2.2 `SavingsGoal` (`savings_goals`) — herda `Entity`
- Campos: `Name` (obrigatório, até 80), `Description?`, `TargetCents?` (`long`, > 0), `DefaultIndexPercent?` e `DefaultProductKind?` (só pré-preenchimento do formulário de aporte).
- `Id`, `UserId`, `CreatedAt`, `UpdatedAt`, `DeletedAt` vêm de `Entity` (soft delete com "Desfazer" de 30 min, como o resto).
- Restrições: índice único `(user_id, lower(name))` entre não excluídos [H: usar `citext` como em outras colunas; conferir `AccountConfiguration`]; `TargetCents > 0`.
- **O objetivo é uma caixa, não um dono dos investimentos** (decisão do dono, 2026-10-17). O aporte tem `GoalId` **opcional**: pode existir sem objetivo ("Sem objetivo") e ser movido de uma caixa para outra a qualquer momento; mover não muda data, valor, rendimento nem histórico.
- **Excluir um objetivo nunca apaga investimento por baixo dos panos.** O app pergunta: **"Excluir só o objetivo (os investimentos ficam em 'Sem objetivo')"** ou **"Excluir o objetivo e os investimentos dele"**; a segunda mostra quantos investimentos e o total aportado, e pede confirmação. O "Desfazer" de 30 min restaura o que foi apagado, e só isso (o que foi só desvinculado volta para a caixa junto).
  Implementação: FK `goal_id` `ON DELETE SET NULL` não se aplica (o objetivo é soft delete); o handler zera `GoalId` dos aportes no caso "só o objetivo" e guarda no evento de exclusão a lista de ids desvinculados para o "Desfazer" religar.
- Uma caixa pode misturar produtos e índices (CDI hoje; IPCA e IPCA+ depois): o produto e o índice vivem em cada aporte, não no objetivo, então misturar não exige mudança de modelo. O motor, a série de taxas e a tributação é que crescem por índice (ver 2.7).

### 2.3 `FixedIncomeDeposit` (`fixed_income_deposits`) — herda `Entity`
- `GoalId` (FK, `Restrict`), `DepositDate` (`DateOnly`, a data da aplicação), `AmountCents` (`long` > 0),
  `ProductKind` (`Cdb` na v1; enum aberto), `IndexKind` (`Cdi`, `CdiPlus` ou `Prefixed`, R.11) e `RateE4` (a taxa contratada, inteiro escalado em 4 casas, e o que ela é depende do índice: no `Cdi`, o percentual do CDI, como a B3: 110,0000% = `1_100_000`, de 0,01% a 500% (D4); no `CdiPlus` e no `Prefixed`, a taxa ao ano, 14% = `140_000`, de 0,01% a 100%; uma constraint do banco confere a faixa de cada índice; era `IndexPercentE4` até a R.11, renomeada sem perder dado),
  `MaturityDate?`, `Liquidity` (`Daily` | `AtMaturity` | `Unknown`; só informativo na v1), `Note?`, `ClientRequestId` (`Guid`, ver 8).
- **Não** guarda rendimento, saldo, imposto nem alíquota: tudo se calcula. **Não** guarda regime tributário: deriva-se de `ProductKind` (CDB → tabela regressiva).
- Restrições: `AmountCents > 0`; `DepositDate` ≥ primeira data da série carregada e ≤ hoje (aporte futuro é recusado na v1); `MaturityDate > DepositDate`;
  índice único `(user_id, client_request_id)`; índice `(user_id, goal_id, deposit_date)`.
- Editar `DepositDate`/`AmountCents`/`RateE4` é permitido (é digitação manual, não extrato), mas **registra o evento `DepositEdited`** (id, campos mudados, sem valores) para auditoria; ver 8.

### 2.4 `ReferenceRate` (`reference_rates`) — **global**, sem `UserId`, entra em `AppDbContext.GlobalTables`
- Campos: `Series` (enum: `Cdi` = SGS 12), `ReferenceDate` (`DateOnly`), `DailyRateE8` (`long`, a taxa diária **como fração, em unidades de 10⁻⁸**: a série publica 0,050788 % ao dia = fração 0,00050788 = `50_788`; ver 5.4),
  `Source` ("bcb-sgs-12"), `FetchedAt` (UTC), `RevisedAt?` (preenchido se um valor já gravado mudou na fonte).
- Chave única `(series, reference_date)`. Sem soft delete (dado de mercado, nunca apagado).
- **Guardar só a taxa diária (SGS 12).** A anualizada (SGS 4389) é derivável (ver 3.3) e não entra no cálculo; guardá-la criaria duas "verdades". Se quisermos auditar, o teste de contrato compara as duas séries em um dia (3.3) em vez de armazenar a segunda.

### 2.4b API feita na R.4 (resumo)
| Rota | O que faz |
|---|---|
| `GET /savings-goals` | as caixas em ordem de nome, com `depositCount` e `principalCents`, mais `unassigned` (os aportes soltos) |
| `POST /savings-goals`, `PUT /savings-goals/{id}` | cria e edita; nome repetido (sem diferenciar maiúsculas) é 409 `GoalNameTaken` |
| `DELETE /savings-goals/{id}?deposits=keep\|delete` | com aportes dentro a escolha é obrigatória (400 `DepositsChoiceRequired`); responde os ids soltos e os excluídos |
| `POST /savings-goals/{id}/restore` | desfaz; corpo opcional `{ detachedDepositIds }` religa só os que continuam soltos |
| `GET /fixed-income-deposits?goal=<id>\|none` | os aportes, do mais recente ao mais antigo |
| `POST /fixed-income-deposits` | cria; o mesmo `clientRequestId` devolve o aporte que já existe, seja qual for o corpo do reenvio |
| `PUT /fixed-income-deposits/{id}` | edita o contrato (a caixa muda abaixo) |
| `PATCH /fixed-income-deposits/{id}/goal` | muda de caixa (`goalId` nulo tira de todas); não toca no contrato |
| `DELETE` e `POST .../restore` do aporte | soft delete e desfazer; se a caixa foi excluída nesse meio-tempo, o aporte volta solto |

Data do aporte antes do primeiro dia do histórico do CDI: 400 `BeforeFirstRate` (sem nenhuma taxa carregada, não confere).
IPCA e IPCA+ são recusados (400); os produtos aceitos são CDB, LCI e LCA (R.13). Percentuais e datas em E4 e `DateOnly`; dinheiro em centavos.

**Resgates (R.23 a R.25, D18):** `POST|GET /fixed-income-deposits/{id}/redemptions`, `GET .../redemptions/preview` (a prévia, sem gravar), `DELETE .../redemptions/{redemptionId}?transfer=keep|delete` (só o último de cada aporte) e `POST .../{redemptionId}/restore`; contrato, erros e regras em `docs/renda-fixa-resgate.md` (seções 11 a 13).

**Resgate em lote (R.29, D19):** `GET /fixed-income-deposits/redemptions/batch/preview`, `POST /fixed-income-deposits/redemptions/batch` (um resgate por aporte, tudo ou nada, uma conta de destino) e `POST /fixed-income-deposits/redemptions/batch/undo`; contrato e regras em `docs/renda-fixa-resgate.md` (seções 15 e 16).

**R.11 (CDI+ e pré-fixado).** O pedido leva `indexKind` (`Cdi`, `CdiPlus`, `Prefixed`) e `rateE4`; a resposta, dos aportes e do saldo, traz `indexKind` e `rateE4`. O nome antigo `indexPercentE4` continua valendo **só no `Cdi`**: aceito no pedido quando `rateE4` não vem e devolvido nas respostas dos aportes de CDI (nulo nos outros), para a tela aberta antes do deploy (a API só acrescenta, `CLAUDE.md` seção 6). Sem taxa nenhuma, 400 "Informe a taxa do aporte.". O `BeforeFirstRate` só vale para quem usa o CDI (`Cdi`, `CdiPlus`): o pré-fixado entra com qualquer data e tem saldo mesmo sem nenhuma taxa gravada. No saldo, o pré-fixado não tem `ratesThrough` (nulo) e a data do grupo vem só dos aportes que usam o CDI; na evolução, os pontos continuam nas datas das taxas, dos aportes e do fim do período (um pré-fixado sozinho, sem histórico do CDI, tem só as pontas).

### 2.4c Aporte que sai de uma conta (D3, feito na R.4b)
Decisão do dono (2026-10-17): o aporte pode vir com `funding` (`fromAccountId`, `toAccountId`, `method`, padrão Pix). O app cria a
transferência (as duas pontas, `TransferPairId`) **na mesma gravação** do aporte e guarda o par no aporte. Resultado, provado em teste
com a história do dono: R$ 10.000 saem do Itaú, o saldo cai R$ 10.000, o "Investido" do Resumo sobe R$ 10.000, e receita, despesa e
sobra não mudam (CLAUDE.md, regra 5).
- Origem: conta corrente ou carteira. Destino: conta de investimento (`FundingNeedsPaymentAccount`, `FundingNeedsInvestmentAccount`).
  Cartão, outra conta de investimento ou conta de outra pessoa são recusados, sem criar nada.
- A conta de investimento guarda só o **principal**: o rendimento aparece nos objetivos, não no saldo da conta. Para o dinheiro não parecer "caixa", o "Investido" do Resumo e de Contas vem dos **ativos** (I.9, D21), e `byAccount` de `GET /fixed-income/balances` traz o rendimento e o principal que sobra por conta, que alimentam o caixa livre de quem acompanha o caixa da conta.
- Valor e data do aporte financiado **não mudam** (409 `FundedDepositLocked`): transferência não se edita. Para corrigir, excluir e lançar de novo.
  Percentual, vencimento, liquidez, observação e caixa mudam normalmente.
- Excluir o aporte financiado: `?transfer=keep|delete`, obrigatório enquanto a transferência existe (400 `TransferChoiceRequired`).
  `keep` deixa o dinheiro movido; `delete` leva a transferência junto e o dinheiro volta para a conta. Desfazer traz as duas juntas
  (409 se uma das contas foi excluída nesse meio-tempo). Excluir um objetivo com `?deposits=delete` **não** mexe nas transferências.
- A tela de lançamentos continua dona da transferência: excluir uma ponta lá desfaz o vínculo na resposta (`funding` nulo) e restaurar lá
  o refaz; apagadas de vez (30 min), o aporte volta a ser só acompanhado e pode ser editado.

### 2.4d Saldo e evolução (R.5)
- `GET /fixed-income/balances?asOf=AAAA-MM-DD` (padrão: hoje; data futura é 400, nada se projeta). Devolve `goals` (cada caixa, também as
  vazias, com a soma e os aportes), `unassigned` (os soltos) e `total`. Por aporte: `principalCents`, `grossBalanceCents`,
  `grossYieldCents`, `factorE8`, `ratesUsed` e `ratesThrough` (a taxa mais recente que entrou na conta). Aporte posterior à data de
  referência ainda não existe nela e fica de fora.
- `rates` acompanha todo número: `available`, `through` (última data gravada), `stale` (a última taxa é mais antiga que a do último dia
  útil antes de hoje), `complete` (nenhum dia útil faltando no meio) e `expectedLast`. A tela diz "rendimento até <data>" e avisa
  quando está defasado ou incompleto: o saldo nunca finge estar em dia.
- **Sem taxa para a data do aporte** (o histórico gravado começa depois dela): os campos de saldo vêm nulos e `unavailable` =
  `RatesUnavailable`. No grupo, o aporte entra pelo **principal** (rendimento zero) e é contado em `unavailableCount`: o total não some com
  dinheiro aportado e diz que está incompleto. Nunca zero no lugar da taxa.
- `GET /fixed-income/evolution?goal=all|none|<id>&from=&to=&step=day|week|month`: uma passada só pelas taxas por aporte
  (`FixedIncomeEngine.Series`), somadas por data. Pontos: cada dia com taxa, cada dia de aporte e as pontas do período. **O ponto de
  uma data é exatamente o saldo total da `balances` com `asOf` igual a ela** (teste cobre as duas). `week`/`month` mantêm o último ponto
  de cada semana (começa na segunda) ou mês, mais o primeiro e o último.
- Só bruto. Imposto e líquido são a R.7.

### 2.5 Fora da v1, mas já previsto no modelo (IPCA e IPCA+)
> **Atualização (2026-10-19):** a investigação do IPCA está em `docs/renda-fixa-ipca.md` (SGS 433 verificada, número-índice do IBGE, convenção do Tesouro, decisões e fases R.17 a R.21). **A implementação está em espera** até o dono trazer dados melhores (extratos reais). O texto abaixo é o da v1.

`IndexKind` ganha `Ipca` e `IpcaPlus` e o aporte ganha `FixedRateBps?` (a parte pré-fixada do IPCA+). Isso **não** entra na v1: IPCA é série **mensal**
(provavelmente a SGS 433 [H, não verificada]) e o IPCA+ real depende de regra do título (valor nominal atualizado, pro rata, marcação a mercado no Tesouro), então exige a mesma investigação que fiz para o CDI, com fonte e fórmula próprias, antes de qualquer código.
Até lá, um aporte de IPCA/IPCA+ **não deve ser aceito** com um número que pareça certo: a tela aceita só CDB-CDI.

### 2.6 O que fica fora de propósito
Sem tabela de snapshot diário (6.2), sem tabela de regra tributária (6.4), sem tabela de estratégia, sem estratégia de lote no resgate (a pessoa escolhe o aporte, D18), sem `ReferenceRateRevision`
histórico (a revisão fica no log e em `RevisedAt`; reavaliar se a fonte revisar valores na prática).

### 2.7 Propriedade e autorização
Todo acesso passa por `AppDbContext` com filtro de dono; endpoints exigem sessão como os demais; `ReferenceRate` é lida por qualquer usuário autenticado
(é pública) mas só a tarefa escreve (nenhum endpoint de escrita). Teste de arquitetura `OwnershipTests` já cobre as duas tabelas novas (dona/global).
`AccountEraser`, `export-user.sql` e `AccountDeletionTests` ganham `savings_goals` e `fixed_income_deposits`; `reference_rates` não (não é da pessoa).

---

## 3. Taxas do CDI: fonte, persistência e sincronização

### 3.1 O que foi verificado na API do BCB (2026-10-17, consultas reais) 
| Fato | Evidência |
|---|---|
| **SGS 12** = CDI diário, em **% ao dia**, 6 casas decimais, só dias úteis (sem fim de semana, feriado, Carnaval) | `últimos/5`: `08/10/2026 → "0.050788"`, `09/10`, `13/10`, `14/10`, `15/10` (10–12/10 ausentes: fim de semana + feriado) |
| **SGS 4389** = CDI anualizado, base 252, em **% ao ano**, 2 casas | mesmos dias: `"13.65"` |
| **As duas séries são consistentes, mas não "equivalentes"**: `(1,1365)^(1/252) − 1 = 0,0507880%` ≈ `0,050788` ✔; `(1,149)^(1/252) − 1 = 0,055131%` = série 12 em 30–31/12/2025 ✔ | cálculo local |
| Histórico da SGS 12 começa em **04/06/1986** | consulta de 1986 devolveu dados |
| **Janela máxima de 10 anos** em série diária: pedir mais devolve **HTTP 406** com JSON `{"error": "...no máximo, 10 anos..."}` | consulta 2015→2026 |
| **O status HTTP não prova nada.** A mesma consulta sem `dataInicial` já voltou **HTTP 406 com JSON de erro**, **HTTP 200 com o mesmo JSON de erro** (2 de 3 vezes na mesma hora) e, na primeira tentativa, **HTTP 200 com uma página HTML** ("Requisição inválida") | consultas de 2026-10-17 (a página HTML não voltou a aparecer e não foi guardada; o teste usa um HTML mínimo, não real) |
| **Faixa sem dia útil responde HTTP 404**, corpo `{"erro":{"statusCode":404,"detail":"...Value(s) not found"}}`, **não** `[]` (ex.: sábado e domingo) | consulta de 17 e 18/10/2026; é o caso normal de uma sincronização no fim de semana |
| Data inválida responde 400 (`Invalid initial date`) | consulta |
| `…/dados/ultimos/N` funciona; resposta traz `cache-control: public, max-age=900` e `etag` | cabeçalhos |
| O último dia só aparece depois da publicação: em 17/10/2026 (sábado, 00h UTC) o último valor era 15/10; 16/10 ainda não existia | consulta |
| A descrição oficial das séries (nome, unidade, periodicidade) no portal SGS não pôde ser lida aqui (a página de metadados falhou); o que acima se afirma vem do **comportamento da API**, não do cadastro oficial | limitação |
| Limite de requisições/condições de uso: **não encontrei documento oficial com cota numérica** [H: uso de 1 consulta/dia é seguro] | limitação |

Consequências de projeto: (1) o cliente **só aceita uma lista JSON** (tipo de conteúdo `application/json` e corpo em array), porque nem HTTP 200 prova sucesso, nem 404 é sempre falha: 404 com "Value(s) not found" é faixa vazia;
(2) o cliente fatia qualquer faixa em **janelas de 9 anos** (folga sobre o limite de 10); (3) `valor` vem como **string** com ponto decimal e é lido com `InvariantCulture`,
convertido direto para inteiro escalado (parse de texto, nunca `double`).

### 3.2 Fluxo de sincronização (`ReferenceRateWorker`)
1. Ao subir, se a tabela estiver vazia **ou** a última data estiver defasada, roda; depois, todo dia útil em dois horários (**[D6]**; recomendo 19h30 e 23h30 de São Paulo, porque a publicação do CDI sai à noite — a janela exata não foi verificada [H]).
2. Pede `dataInicial = (última data gravada) − 10 dias` até hoje (a sobreposição pega correções recentes).
3. Valida: JSON array, datas `dd/MM/yyyy` válidas e únicas, valores > 0 e plausíveis (0 < taxa < 1% ao dia); qualquer item inválido **recusa o lote inteiro** (nada pela metade, como a lista de ativos).
4. Grava numa transação com `INSERT … ON CONFLICT (series, reference_date) DO NOTHING`; se uma data existente chegar com **valor diferente**, **não sobrescreve em silêncio**: atualiza, preenche `RevisedAt` e emite `ReferenceRateRevised(series, date)` em `Warning`.
5. Falha da fonte (`MarketDataException`): `Warning`, nada gravado, a próxima execução tenta de novo. Exceção inesperada: `Error`. **Nunca preenche lacuna com zero nem interpola.**
6. Reprocessamento: um método administrativo `RefreshRange(from, to)` (chamado por um teste/ferramenta de operação, não por endpoint público) refaz um intervalo; idempotente pelo mesmo upsert.

### 3.3 Idempotência, lacunas e defasagem
- **Idempotência:** chave única + `DO NOTHING`/revisão explícita; rodar duas vezes ou em paralelo (duas instâncias) não duplica.
- **Lacuna:** a conta usa **somente datas gravadas** (as que o BCB publicou), então fim de semana/feriado nunca viram dia de capitalização por engano. Para *detectar* falta de um dia publicado, `ReferenceRateCoverage` (namespace `Prisma.Domain.FixedIncome.Coverage`, o único liberado a usar o `BankCalendar` no CDI; D8) compara as datas gravadas com os dias úteis esperados entre a primeira e a última; dia útil sem taxa gera o aviso `ReferenceRateGapDetected` e `IsComplete = false`. **[V]** O `BankCalendar` coincide com a série real: de 02/01/2023 a 15/10/2026 o BCB publicou 951 datas e **nenhum dia útil do calendário ficou sem taxa** (24/12 e 31/12 têm taxa; Carnaval e Corpus Christi não). A única diferença é 20/11/2023, que o calendário já trata como feriado mas só virou nacional em 2024; só causa "taxa a mais", nunca falso alarme de lacuna.
- **Defasagem:** `LastRateDate` + regra "o último dia útil esperado é no máximo ontem" define `ratesStale`. Quando está defasado, o saldo é calculado **até a última taxa conhecida** e a tela diz isso ("rendimento até 15/10"); nunca estima o dia faltante.

### 3.4 Dependências e custos
Sem pacote novo (`HttpClient` tipado, como a brapi). Sem custo. Sem token. Configuração: `Bcb:BaseUrl` (padrão `https://api.bcb.gov.br/`), `Bcb:TimeoutSeconds`, `Rates:Sync:Enabled` (desligado nos testes, igual a `Assets:Sync:Enabled`).

### 3.5 Limites da infraestrutura
A tarefa roda no mesmo processo da API (como as outras três). Uma carga inicial de ~2,7 mil linhas (2015→hoje) e ~1 linha/dia depois é desprezível. Risco real: **o Railway reinicia/dorme e a tarefa perde a janela** — mitigado por "rodar ao subir se defasado". Sem monitor externo hoje (H.2c aberta): a defasagem aparece no `ratesStale` da tela e no log; um aviso no Healthchecks.io é sugestão futura, não requisito.

---

## 4. Fontes oficiais e limitações

| Fonte | O que confirma | Estado |
|---|---|---|
| API SGS do BCB (`api.bcb.gov.br/dados/serie/bcdata.sgs.12` e `.4389`) | séries, unidade, janela de 10 anos, erro 200 com HTML | **[V]** por consulta direta |
| B3 — "Metodologia de Cálculo Acumulado de DI" e "Metodologia de apuração da taxa" (`b3.com.br`) | fórmula do fator, arredondamentos (5.1) | **[V]** lido na página da B3 |
| **Decreto 6.306/2007, art. 32 e Anexo** (texto atualizado, Câmara dos Deputados) | IOF de renda fixa: 1% ao dia, limitado ao rendimento, tabela de 30 dias | **[V]** li o artigo e a tabela completa no PDF oficial |
| **Lei 11.033/2004, art. 1º / IN RFB 1.585/2015** (tabela regressiva de IR) | 22,5/20/17,5/15% por prazo | **[V pela B3; H no texto legal]**: o glossário da B3 (Bora Investir) e o site que você consultou trazem as quatro faixas, com incidência **sobre o rendimento**. Na sua transcrição a última faixa saiu "25%"; a B3 diz **15%** (a tabela é regressiva, só desce), e o plano usa 15%. **Não consegui abrir o texto legal primário** (Planalto recusou a conexão, a página da Receita é dinâmica): conferir antes de implementar |
| MP 1.303/2025 (propunha IR fixo de 17,5% e revogava o art. 1º da Lei 11.033) | perdeu a eficácia em 08/10/2025 sem ser votada; a tabela regressiva segue em 2026 | **[V]** por várias fontes jornalísticas/jurídicas; **[H]** que não haja medida posterior — conferir na data da implementação |

Limitações: sem acesso ao texto primário do IR e à ficha oficial das séries; cota da API do BCB não documentada.
Há ainda uma advertência prática: **dois sites populares traziam a tabela do IOF errada** (ex.: 5º dia = 80%, 10º dia = 63%); o texto oficial diz **83% e 66%**.
Nunca usar tabela de blog; a tabela do código vem do Anexo.

---

## 5. Fórmulas

### 5.1 Fórmula validada
A fórmula proposta no pedido **coincide** com a convenção da B3 (que é o padrão de mercado para "% do CDI"):

```
TDI_k  = ((DI_k/100 + 1)^(1/252)) − 1          fração diária, arredondada em 8 casas  (é o que a SGS 12 publica, em %)
f_k    = 1 + (p/100) × TDI_k                    p = percentual contratado do CDI (informado com 4 casas)
C      = ∏ f_k                                  a cada passo, trunca em 16 casas; o C final é arredondado em 8 casas
S_j(t) = P_j × C_j(t)
```
Unidades: `r_d` do pedido = valor da SGS 12 em **% ao dia** ⇒ `TDI_k = r_d/100`. Por isso `(1 + r_d/100 · p/100)` equivale a `f_k`. Verificado: a própria série 12 já é `TDI_k` com 8 casas
(ver o teste de 4389 na seção 3.1). Isso **descarta** usar a série anualizada como taxa diária, usar dias corridos/365 ou aplicar `p` ao fator anual.

O que **difere** do pedido, e vira regra explícita:
1. **Truncamento a cada multiplicação (16 casas) e arredondamento do fator final (8 casas)** — a B3 faz assim; a versão "simplificada" (cálculo em ponto flutuante sem corte) diverge na 8ª–10ª casa do fator (centavos em valores altos/prazos longos).
2. **Quais taxas entram em `D_j(t)`**: [H] taxas com data `d` tal que `DepositDate ≤ d < t` (a taxa do dia `d` remunera de `d` até o dia útil seguinte; a do próprio dia da aplicação entra, a do dia da referência não). **Falta validar contra um extrato real** (Itaú/Íon) — é a principal incerteza do motor.
3. **Aporte em dia não útil ou depois do horário de corte**: [H] a capitalização começa na primeira taxa publicada em data ≥ `DepositDate` (consequência da regra 2, sem tratamento extra). Bancos podem aplicar corte de horário; o app **não sabe** a hora: documentar como limitação (D2).
4. **Como o banco aplica `p`**: a convenção da B3 aplica `p` sobre a taxa **diária**; produtos específicos podem aplicar sobre a taxa anual (`(1+i)^(p)`); o contrato do CDB prevalece. A v1 assume a B3 e **mostra a convenção na tela**.
5. **Arredondamento do saldo em reais**: o fator é calculado sem dinheiro; o saldo do aporte é `round(P × C)` em centavos com **meio para cima** [D1]; o total do grupo é a **soma dos saldos já arredondados dos aportes** (nunca arredondar o total), para o total bater com a soma que a pessoa vê.

### 5.2 Exemplo de referência (calculado fora do app, com taxas reais do BCB)
Ilustrativo, CDI = 100%, até 16/10/2026 (última taxa publicada: 15/10), arredondando como 5.1:

| Aporte | Valor | Taxas usadas | Fator C (8 casas) | Saldo bruto | Rendimento bruto | Prazo | IR (22,5%) |
|---|---:|---:|---:|---:|---:|---:|---:|
| 21/07/2026 | R$ 4.000,00 | 61 | 1,03193390 | R$ 4.127,74 | R$ 127,74 | 87 dias | R$ 28,74 |
| 22/07/2026 | R$ 1.000,00 | 60 | 1,03139210 | R$ 1.031,39 | R$ 31,39 | 86 dias | R$ 7,06 |
| 05/08/2026 | R$ 500,00 | 50 | 1,02598971 | R$ 512,99 | R$ 12,99 | 72 dias | R$ 2,92 |
| **Grupo** | **R$ 5.500,00** | | | **R$ 5.672,12** | **R$ 172,12** | | R$ 38,72 |

#### Simulação pedida: R$ 6.097,31 em 21/07/2026, 100% do CDI
Mesmo método (taxas reais do BCB; última publicada: 15/10/2026; cálculo até 16/10/2026, 87 dias corridos, 61 taxas):

| Em | Taxas | Fator C | Saldo bruto | Rendimento bruto |
|---|--:|---:|---:|---:|
| 31/07/2026 | 8 | 1,00421021 | R$ 6.122,98 | R$ 25,67 |
| 31/08/2026 | 29 | 1,01519630 | R$ 6.189,97 | R$ 92,66 |
| 30/09/2026 | 50 | 1,02618635 | R$ 6.256,98 | R$ 159,67 |
| **16/10/2026** | **61** | **1,03193390** | **R$ 6.292,02** | **R$ 194,71** |

Se resgatasse em 16/10/2026: prazo de 87 dias ⇒ faixa de 22,5% (IOF não se aplica, são mais de 30 dias).
IR estimado R$ 43,81 (22,5% × 194,71) ⇒ rendimento líquido **R$ 150,90** ⇒ saldo líquido estimado **R$ 6.248,21**.
A faixa muda para 20% no 181º dia, em **18/01/2027** (21/07/2026 + 181 dias), e o IR cairia sozinho.

Sensibilidade da convenção D2 (início da capitalização): sem a taxa do dia da aplicação (capitalizando só a partir de 22/07) o saldo bruto seria R$ 6.288,72, ou seja, **R$ 3,30 a menos**.
É essa diferença que o seu extrato do Itaú/Íon vai resolver. A tolerância de um centavo por aporte só faz sentido depois dessa calibração. Sem truncar a 16 casas o resultado é o mesmo em centavos neste caso (a diferença do truncamento só aparece em valores e prazos bem maiores).
Compare com o que o seu app do banco mostra para essa aplicação: se bater em ± R$ 0,01 a convenção está certa.

#### Calibração com o banco (extrato do dono, 2026-10-17) — D2 confirmada
Referência do banco: **16/10/2026** (taxas até 15/10). Dois casos reais, 100% do CDI, IOF 0:

| Caso | Aportes | Banco: valor guardado / bruto / IR / líquido | Motor (5.1, arredondando por aporte) | Diferença |
|---|---|---|---|---|
| A | 21/07: R$ 4.797,31 + R$ 1.300,00 | R$ 6.248,21 / 194,70 / 43,80 / 150,90 | saldo líquido R$ 6.248,21 / 194,71 / 43,81 / 150,90 | bruto e IR **+R$ 0,01**; líquido e saldo **iguais** |
| B | 22/07: R$ 10.474,08 + 10/08: R$ 9.000,00 | R$ 19.899,03 / 548,32 / 123,37 / 424,95 | saldo líquido R$ 19.899,04 / 548,33 / 123,37 / 424,96 | bruto, líquido e saldo **+R$ 0,01**; IR igual |

Conclusões **[V]**:
1. **A taxa do dia da aplicação entra e a do dia de referência não** (a variante "só a partir do dia seguinte" erra em R$ 3 a R$ 10, muito além do centavo). A hipótese 2 de 5.1 está confirmada.
2. A convenção (percentual aplicado sobre a taxa diária da B3, fator acumulado) reproduz o banco com **diferença máxima de R$ 0,01**, mesmo com dois aportes e prazos diferentes (72 e 87 dias, mesma faixa de 22,5%).
3. A diferença de 1 centavo vem do **arredondamento interno do banco**, que o extrato não revela. Truncar o saldo em centavos (em vez de arredondar) e truncar o IR do total explicam o caso A, mas **nenhuma regra simples que testei explica os dois casos ao mesmo tempo**; uma variante (saldo diário truncado em 4 casas + fim truncado) fecha ambos, mas com duas amostras isso é coincidência provável, não regra. Não vou imitar o banco com base em dois pontos.
4. **Decisão para o D1:** manter a regra do plano (centavos por aporte, total = soma dos saldos já arredondados) e **declarar a tolerância de R$ 0,01 por aporte** contra extrato. Os dois casos entram no `fixed-income-reference.json` como casos de regressão, com a nota "diferença de 1 centavo por arredondamento do banco". Reavaliar truncamento se novos extratos mostrarem um padrão.

Esses números são **vetores de teste candidatos**: precisam ser conferidos com uma calculadora/extrato de terceiros antes de virarem "verdade" (seção 9).
Vetor mínimo sem rede: 3 dias com `DI = 0,050788` a 110% ⇒ `C = 1,0016769405041684` (16 casas) ⇒ R$ 1.000,00 → R$ 1.001,68.

### 5.3 Saldo do grupo, rendimento e resgates
`S_grupo(t) = Σ S_j(t)`; `R_bruto = S_grupo − Σ P_j`. Sem resgate, "principal" é a soma dos aportes. **Com o resgate (R.22 a R.26, D18)** o principal passa a ser *aportes − principal resgatado*, por aporte: cada resgate reduz o aporte que a pessoa escolheu (não há FIFO) e congela o que saiu, sem reescrever o histórico (`docs/renda-fixa-resgate.md`, seção 3).

### 5.4 Representação numérica (decisão a aprovar, D1)
- Dinheiro: `Money(long Cents)`, sem exceção.
- Taxa diária e `p`: inteiros escalados (`long`): TDI em 10⁻⁸, `p` em 10⁻⁴ (%). 
- Fator: ponto fixo em **`Int128` com 16 casas (10¹⁶)**: produto de dois fatores (10³²) cabe com folga em `Int128`, e **truncar a 16 casas é uma divisão inteira**, exatamente a regra da B3, sem `decimal`.
  Alternativa: `decimal` (28 dígitos) só dentro do motor — mais simples de ler, mas abre uma exceção à regra 1 do `CLAUDE.md` e a conta de truncamento é mais fácil de errar. **Recomendo `Int128`** (consistente com `CashPerUnit`).
- No Postgres: `bigint` para a taxa e para o percentual; `bigint` para centavos. Nenhum `double`, `float` ou `numeric` calculado em SQL.

### 5.5 CDI+ e pré-fixado (R.10, decisão do dono em 2026-10-18)
Mesma convenção de datas do CDI: entra o dia do aporte, não entra o dia da referência. Tudo em `Int128`, 16 casas por passo e 8 no fator final, saldo com meio centavo para cima (`FixedPoint`). A remuneração do aporte é uma só (`Remuneration`): X % do CDI, CDI + taxa ao ano, ou taxa ao ano (pré). A taxa ao ano (`AnnualRate`) vai em E4 como o percentual (14 % = 140.000), de 0,01 % a 100 %.

- **CDI+** (CDI + s a.a.): `fator = C × (1 + s)^(n/252)`, onde `C` é o fator do CDI a 100 % (o mesmo da 5.1) e `n` o número de taxas usadas. A taxa fixa vira um fator por dia, `(1 + s)^(1/252)` (`AnnualRate.DailyFactor`: raiz 252 inteira por busca binária, sem `double`), acumulado ao lado de `C`; o produto é arredondado **uma vez só**, em 8 casas. Usa só as datas que o Banco Central publicou (nunca o calendário): o `n` é o das taxas usadas. Não é o mesmo produto que "X % do CDI": CDI + 2 % rende mais que 110 % do CDI com o CDI a ~14 % e menos com o CDI acima de 20 %.
- **Pré-fixado** (r a.a., base 252 dias úteis): `fator = (1 + r)^(du/252)`, com `du` os dias úteis de `[aporte, referência)`. **Não lê a série do CDI**: o saldo vale até a data pedida, mesmo com o Banco Central fora do ar. Os dias úteis vêm do `BankCalendar` (feriados nacionais, Carnaval, Sexta-feira Santa e Corpus Christi), por isso o código mora em `Prisma.Domain.FixedIncome.Prefixed`, quarto uso do calendário liberado em `BankCalendarUsageTests`. Aporte em fim de semana ou feriado só passa a render no dia útil seguinte. Prazo acima de 50 anos é recusado.
- Um ano de calendário não é 252 dias úteis: de 01/10/2026 a 01/10/2027 são 250. Por isso R$ 5.000 a 14 % a.a. dão R$ 5.694,08, e R$ 5.700,00 só com 252 dias úteis exatos (a conta de calculadora). O imposto não muda (`FixedIncomeTax` só olha o rendimento e o prazo).
- **A conferir com extrato real** (como foi com o CDI): como o banco arredonda o fator da taxa fixa. A convenção acima (acumular `C` e `(1 + s)^(1/252)` em 16 casas e arredondar o produto em 8) é a hipótese; se o extrato divergir mais que R$ 0,01, é uma linha em `FixedIncomeEngine`/`PrefixedYield`.
- Testes: vetores independentes (Python, `Decimal` de 60 dígitos, feriados escritos à parte) em `PrefixedYieldTests` e `CdiPlusTests`, e propriedades contra um oráculo em `BigInteger` (raiz 252 exata). Provados com mutação (dia da referência contando, base 365, feriados ignorados, taxa fixa descartada, percentual no lugar de 100 %, fator final truncado).

---

## 6. Cálculo, evolução e tributação

### 6.1 API de cálculo (puro, no domínio)
`FixedIncomeEngine.Balance(deposit, rates, asOf) → { PrincipalCents, GrossBalanceCents, GrossYieldCents, RatesThrough, RatesUsed }`
Recebe as taxas já carregadas (lista ordenada por data); não consulta banco, relógio nem rede. Isso torna os resultados **reprodutíveis**: mesma entrada, mesma saída, e a resposta devolve `ratesThrough` e `asOf`.

### 6.2 Evolução histórica: sob demanda, sem snapshot
Compare:
| | Sob demanda (recomendado) | Snapshot diário |
|---|---|---|
| Custo | para N aportes e D dias: O(N·D) multiplicações de inteiro; 20 aportes × 3 anos ≈ 15 mil passos, microssegundos | tabela com 1 linha/aporte/dia, job para manter |
| Consistência | sempre reflete as taxas atuais e as edições | precisa invalidar a cada edição de aporte ou revisão de taxa — fonte clássica de erro plausível |
| Auditoria | determinística a partir das taxas | duplica o que é derivável |

Gráfico: série diária (ou semanal para períodos longos) calculada **num único passe** por aporte (acumulando o fator dia a dia), somando os saldos por data. Pontos antes do aporte não existem; o dia do aporte começa em `P`.
Reavaliar snapshot só se alguma pessoa passar de ~milhares de aportes (hoje impensável).

### 6.3 O que a tela e a API devem separar (nomes dos campos)
`principalCents` (aportado) · `grossYieldCents` · `estimatedTaxCents` (IR + IOF) · `netYieldCents` · `grossBalanceCents` · `netBalanceCents` · `asOf` · `ratesThrough` · `ratesStale`/`ratesComplete`.
Líquido é sempre **"estimado se resgatar em `asOf`"**, nunca "realizado". Projeção futura **não existe na v1**; se vier, é um bloco separado, rotulado com a taxa suposta.

### 6.4 IR e IOF (módulo separado do motor de rendimento)
`FixedIncomeTax.Estimate(productKind, depositDate, redemptionDate, grossYieldCents) → { IofCents, IrCents, IrRate, HoldingDays, RuleVersion }`.

**Confirmado [V]:** IOF (Decreto 6.306/2007, art. 32 e Anexo): 1% ao dia sobre o valor do resgate, **limitado ao rendimento**, pela tabela abaixo, para resgate em **menos de 30 dias**; aplica-se a renda fixa e a fundos; alíquota zero para LCA/CRA/CDCA, debêntures/CRI/LF, e outras.

| Dia | % do rendimento | Dia | % | Dia | % |
|--:|--:|--:|--:|--:|--:|
| 1 | 96 | 11 | 63 | 21 | 30 |
| 2 | 93 | 12 | 60 | 22 | 26 |
| 3 | 90 | 13 | 56 | 23 | 23 |
| 4 | 86 | 14 | 53 | 24 | 20 |
| 5 | 83 | 15 | 50 | 25 | 16 |
| 6 | 80 | 16 | 46 | 26 | 13 |
| 7 | 76 | 17 | 43 | 27 | 10 |
| 8 | 73 | 18 | 40 | 28 | 6 |
| 9 | 70 | 19 | 36 | 29 | 3 |
| 10 | 66 | 20 | 33 | 30 | 0 |

**Confirmação por fonte secundária, a conferir no texto oficial antes de codar [H forte]:** IR regressivo de renda fixa de pessoa física: até 180 dias 22,5%; 181–360 dias 20%; 361–720 dias 17,5%; acima de 720 dias 15%; incide sobre o **rendimento**, não sobre o principal, e sobre o rendimento **já líquido de IOF**.

**Hipóteses a fechar [H]:**
- Contagem do prazo: dias corridos da data da aplicação à do resgate; limites exatos (180 é 22,5%; 181 é 20%; 360 é 20%; 361 é 17,5%; 720 é 17,5%; 721 é 15%) e se o dia da aplicação conta. Fixar com o texto da IN RFB 1.585 e testar as bordas.
- IR é retido na fonte **no resgate/vencimento** (não há "come-cotas" em CDB): portanto o valor mostrado é **estimativa de resgate em `asOf`**, não imposto devido hoje.
- IOF e IR por aporte: a faixa de IR de cada lote é independente, como pedido.
- Regime por produto: a v1 só implementa **CDB** (`TaxRuleSet` para CDB). LCI/LCA (isentas de IR e IOF para pessoa física hoje) **não** reutilizam as regras do CDB e entrarão como `ProductKind` próprio com regra própria — a MP 1.303 mostrou que a regra pode mudar por lei, então ela é **versionada por data** no código (`TaxRuleSet` com `EffectiveFrom`; o cálculo usa a regra vigente na data do resgate estimado e devolve `RuleVersion`).
- Arredondamento: IOF e IR em centavos com meio para cima, **sobre o rendimento do lote**; diferença de poucos centavos para o extrato é esperada e documentada (D1).

**Implementado na R.7 (2026-10-17).** `FixedIncomeTax.Estimate` (domínio, `Prisma.Domain.FixedIncome`): prazo = diferença em dias corridos entre resgate e aporte (180 dias ainda é 22,5%; 181 é 20%; 360 é 20%; 361 é 17,5%; 720 é 17,5%; 721 é 15%); IOF só abaixo de 30 dias, % do rendimento pela tabela acima; IR sobre o rendimento menos o IOF; ambos em centavos, meio centavo para cima. A regra é uma lista versionada por data de início (`2005-01-01`, `cdb-lei-11033-2005`); resgate anterior a ela é recusado. Rendimento zero ou negativo não gera imposto. O endpoint de saldos devolve, por aporte, `tax` (IOF, IR, total, alíquota em pontos-base, prazo, versão da regra, rendimento e saldo líquidos) e, por grupo, `estimatedTaxCents`, `netYieldCents` e `netBalanceCents` (soma dos impostos de cada aporte, nunca uma alíquota sobre o total). Aporte sem taxa não tem imposto (`tax` nulo) e entra no líquido pelo principal. **Ainda vale a ressalva:** o texto legal (Lei 11.033, art. 1º, e IN RFB 1.585) não foi conferido; a tabela do IR é a da B3 confirmada pelo dono, que dispensou a leitura do texto. A evolução (gráfico) continua em valores brutos.

**Decisão de escopo (recomendação):** **bruto primeiro (etapas 1–5), líquido na etapa 6**, porque o IR só deve ir ao ar depois de a tabela ser conferida no texto oficial. O domínio já nasce com a interface de imposto separada para não reescrever o motor.


### 6.5 LCI e LCA (R.13, pesquisa de 2026-10-18)
- **Remuneração:** a mesma do CDB: % do CDI, CDI + taxa ou pré-fixado (e, depois, IPCA+). O motor de rendimento não muda; o produto (`FixedIncomeProductKind`: `Cdb`, `Lci`, `Lca`) só escolhe a regra de imposto. Papéis de IPCA seguem fora.
- **IR:** pessoa física é **isenta**, em qualquer prazo (Lei 11.033/2004, art. 3º, II para a LCI, letras hipotecárias e CRI; IV para a LCA, CDCA, CRA, CDA e WA; texto lido na cópia compilada da Câmara dos Deputados). Pessoa jurídica paga, mas o app é de pessoa física. A MP 1.303/2025, que cobraria 5% sobre emissões de 2026, caducou em outubro de 2025 sem virar lei; se vier lei nova, entra como regra nova por data, sem mexer na antiga.
- **IOF:** a **LCA** tem alíquota zero (Decreto 6.306/2007, art. 32, § 2º, V). A **LCI** não é citada nesse inciso, então vale a tabela de menos de 30 dias do CDB (decisão do dono: sugestão aceita em 2026-10-18); como o prazo mínimo da letra é de meses, na prática quase nunca aparece. O decreto foi lido numa cópia de terceiros (normaslegais.com.br), porque o Planalto não respondeu ao WebFetch nem ao `curl`.
- **Regras (versões):** `lci-lei-11033-2005` (IR zero, IOF pela tabela) e `lca-lei-11033-2005` (IR zero, IOF zero), ambas desde 01/01/2005; o CDB segue com `cdb-lei-11033-2005`. Produto sem regra é recusado, nunca tributado como CDB. O líquido de uma letra com mais de 30 dias é igual ao bruto.
- **Prazo mínimo (não validado, só informativo):** a Resolução CMN 5.215 (22/05/2025) baixou de 9 para 6 meses o prazo mínimo das letras sem atualização por índice de preços; para as com índice de preços as fontes divergem (90 dias × 12 meses) e não achei o texto da resolução. A mesma resolução proíbe o emissor de recomprar a própria letra, salvo intermediação: o resgate costuma ser só no vencimento. A tela (R.14) só **avisa** quando o vencimento é menor que 6 meses (decisão do dono), porque letras antigas tinham 90 dias e a pessoa pode cadastrar histórico.
- **O que a lei confirma no CDB:** o art. 1º da Lei 11.033 (tabela de 22,5/20/17,5/15% por 180/360/720 dias) bate com a tabela do app (mesma cópia da Câmara). A IN RFB 1.585 não foi conferida.
- **Equivalência com o CDB** (para a tela, se um dia comparar): uma letra a X% do CDI equivale a um CDB bruto de `X ÷ (1 − IR)`: 90% do CDI = 116,1% (até 180 dias), 112,5% (181 a 360), 109,1% (361 a 720) e 105,9% (acima de 720).
- **Fora do cálculo:** o FGC cobre até R$ 250 mil por CPF e instituição (teto global de R$ 1 milhão a cada 4 anos); não entra no app.

---

## 7. Interface e fluxo

### 7.1 Onde fica
Nova aba **"Objetivos"** em `/contas/investimentos` (ao lado de Carteira/Proventos/Notas; hoje essa página é de ações/FIIs, e renda fixa é carteira diferente) **ou** rota própria `/contas/objetivos`. Recomendo a **aba**, por reaproveitar `InvestmentsPage`, o seletor de ano e a navegação já testada. [D7]

### 7.2 Telas
1. **Lista de objetivos**: cartão `.surface` por objetivo — nome, total aportado, saldo bruto estimado, rendimento, barra de progresso até a meta (`.meter-fill`, só se houver meta), data de referência.
2. **Novo/editar objetivo** (`BottomSheet`): nome, descrição, meta, produto padrão (CDB pós), % do CDI.
3. **Detalhe do objetivo**: números em destaque (`font-display`), alternância **Bruto | Líquido estimado** (a partir da etapa 6), gráfico de evolução (Recharts, receita/rendimento em `text-spectrum`, principal em tinta, **sem verde/vermelho**), lista de aportes (data, valor, % do CDI, prazo, rendimento do lote).
4. **Novo aporte** (`BottomSheet`): data (padrão hoje), valor, % do CDI (pré-preenchido), vencimento opcional. Pré-visualiza "rende até 15/10" se houver taxas.
5. **Edição/exclusão** de aporte: igual ao resto do app (soft delete + "Desfazer" por 30 min); editar mostra aviso de que altera o histórico.

**Implementado (R.6, R.7, R.9, R.12 e R.14).** Na seção "Produto" do painel do aporte, os chips **CDB | LCI | LCA** (antes de "Rende"); as linhas leem "LCI · 90% do CDI"; no líquido, as letras mostram "Isento de IR · 87 dias" (ou "IOF, isento de IR" na LCI com menos de 30 dias) e o rodapé ganha a nota das letras (Lei 11.033, art. 3º) além da do CDB, cada uma só se há aporte do produto. Vencimento menor que 6 meses numa LCI ou LCA mostra "LCI e LCA têm prazo mínimo de 6 meses. Confira o vencimento." e **não bloqueia**. O saldo por aporte (`GET /fixed-income/balances`) devolve `productKind` para a lista de todos os objetivos escolher a nota. No painel do aporte, a seção "Rende" tem três chips, **% do CDI | CDI + taxa | Pré-fixado**, e um campo só, que muda de rótulo e de faixa com o índice ("% do CDI", até 500%; "% ao ano, mais o CDI" e "% ao ano", até 100%), com uma dica de uma linha fora do CDI e sem taxa sugerida (só o CDI vem com o percentual padrão do objetivo); trocar de chip limpa o campo e o erro. Nas listas, "110% do CDI", "CDI + 2% a.a." e "14% a.a. (pré)". Os avisos de taxas do CDI (ausentes, defasadas, incompletas) e a nota de rodapé só falam do CDI quando há aporte que o usa: uma lista só de pré-fixado diz que o cálculo é pela taxa contratada, em dias úteis, até hoje; uma mista diz as duas coisas (`ratesScope`). A aba "Objetivos" (`?aba=objetivos&objetivo=<id>|sem`) tem a lista (total, cartão por objetivo com a barra da meta, "Sem objetivo" só se houver aportes soltos), o detalhe (números grandes, gráfico de evolução em Recharts com 1M/3M/6M/Tudo, aportes com o rendimento de cada um e a origem da conta) e os painéis (objetivo; aporte, com "Saiu de uma conta?"). A chave **Bruto | Líquido estimado** vale para a lista e o detalhe; o gráfico é sempre bruto e diz isso. Excluir pergunta (objetivo: manter ou levar os investimentos; aporte financiado: manter ou levar a transferência) e o toast tem "Desfazer". Avisos: taxas "sem taxa", "incompletas" e "defasadas" têm texto próprio; a nota "Estimativa; o extrato manda" traz a data da última taxa; mais o que a renda fixa ainda não faz (só CDB, LCI e LCA, em % do CDI, CDI + taxa ou pré-fixado, sem IPCA nem projeção; os resgates têm tela, R.25) e, no líquido, a explicação do imposto. Código: `src/prisma-web/src/features/investments/` (`GoalsTab`, `GoalSheet`, `DepositSheet`, `EvolutionChart`, `fixedIncome.ts` com as contas puras e o Vitest, `fixedIncomeQueries.ts`); chaves de cache em `fixedIncomeKey`.

### 7.3 Regras de texto e honestidade
- Rótulos fixos: "Aportado", "Rendimento bruto", "Imposto estimado (se resgatar hoje)", "Saldo líquido estimado". Nunca "lucro" nem "você ganhou" para o líquido.
- Nota sempre visível: "Cálculo pelo CDI publicado pelo Banco Central até **15/10/2026**. Estimativa; o extrato do banco manda."
- Estado "taxas desatualizadas" e "taxas incompletas" têm texto próprio.
- Toda tela nova se confere em 320/390/1280, claro e escuro (regra do dono).

### 7.4b Renda fixa na Carteira (R.9)
**Implementado.** `GET /portfolio` devolve `fixedIncome` (uma linha por objetivo: id, nome, quantos aportes, aportado, rendimento bruto e peso) e `fixedIncomeRatesThrough`; o grupo `FixedIncome` entra em `groups` com o aportado como custo. O `PortfolioSnapshot.Build` recebe `FixedIncomeHolding` e calcula os pesos sobre o total com a renda fixa (maior resto, soma 100%). O aportado e o rendimento são os de `GetBalances` até a data da carteira (31/12 do ano, ou hoje); `years` começa no primeiro aporte. Na tela: nova faixa do espectro (cinza-ardósia `#64748b`), linha "N objetivos" na legenda, a seção "Renda fixa" com uma linha por objetivo (toque abre o objetivo na aba Objetivos) e, no topo, o rendimento da renda fixa até a data da última taxa. O "Investido" do Resumo não muda.

### 7.4c Disponível × Investido (R.32 a R.34, D20)
**Implementado.** O saldo de uma conta de investimento é o principal (a transferência do aporte), e continua sendo, na tela da conta, no extrato e em `GET /accounts/balances`: mexer nele deixaria de bater com o banco. O que muda é a leitura. `GET /fixed-income/balances` ganhou `byAccount` (`AccountYield`: conta, quantos aportes, rendimento bruto do que sobra), só dos aportes que **saíram de uma conta pelo app e cuja transferência está viva** (`Fundings.LiveFundings`, a mesma regra que dá o `funding` do aporte); aporte só acompanhado e aporte cuja transferência foi excluída ficam de fora. Na tela, `splitBalances` (`features/dashboard/upcoming.ts`) separa **Disponível** (contas ativas correntes e carteiras) de **Investido** (contas de investimento ativas, saldo mais o rendimento delas): o "Em contas" do Resumo mostra as duas linhas quando há conta de investimento (sem ela, é o saldo de sempre), e a lista de Contas mostra a conta de investimento como "Investido", com "Com R$ X de rendimento". O rendimento é o **bruto** (o imposto só vale no resgate; a Carteira também usa o bruto). O card "Investido" do mês no Resumo é outra coisa (fluxo do mês: aportes menos resgates) e não muda. `invalidateMoney` recarrega a renda fixa, porque excluir a transferência de um aporte muda o `byAccount`.

> **Substituído pela I.9 (2026-10-28, `docs/patrimonio.md`, D21):** o "Investido" deixou de ser "saldo das contas de investimento + rendimento dos aportes com transferência viva" e passou a vir dos ativos (a Carteira de hoje mais o rendimento de **toda** a renda fixa), com ou sem histórico de caixa; `splitBalances` saiu da tela. Ficaram `byAccount`, que agora traz também `PrincipalCents` (o principal que sobra dos aportes financiados pela conta), e a regra de que **o saldo da conta não muda**. O texto acima é o histórico do que a R.32 a R.34 entregou.

### 7.4 Cache
Mutação invalida as chaves novas (`invalidateMoney` ganha as chaves de objetivos); respostas valem 60 s como as demais.

---

## 8. Segurança e integridade

| Risco | Tratamento |
|---|---|
| Acesso a dado de outro usuário | filtro de dono em `SavingsGoal` e `FixedIncomeDeposit`; todo handler carrega por id **pelo `DbContext`** (404, nunca 403, para não revelar existência); testes de isolamento (seção 9) |
| Confiar só na interface | validadores FluentValidation no servidor: valor > 0, `p` na faixa, datas, vencimento > aplicação, comprimento de texto |
| Aporte duplicado por repetição de requisição | `ClientRequestId` (gerado pela tela ao abrir o formulário) + índice único `(user_id, client_request_id)`; o segundo pedido devolve o primeiro (mesmo molde de `AddHolding`) |
| Edição/exclusão concorrente | `xmin` como token de concorrência em `SavingsGoal` e `FixedIncomeDeposit` (`CLAUDE.md`, seção 6); 409 em conflito |
| Log vaza dinheiro | eventos só com ids e tipos (`GoalCreated`, `DepositAdded`, `DepositEdited(fields)`); o teste de arquitetura de `AppLog` recusa parâmetros de nome sensível |
| Alteração silenciosa de taxas | `RevisedAt` + `Warning`; cálculo devolve `ratesThrough`; teste de que revisão não passa despercebida |
| Falha na importação | nada gravado pela metade, próxima execução tenta; nenhum valor padrão |
| Valores absurdos | teto de aporte (ex.: R$ 100 milhões em centavos cabe em `long`) e `Int128` nos produtos intermediários |
| LGPD | tabelas novas com `user_id` entram na exclusão de conta, na cópia dos dados e no roteiro de titulares; **não** exigem revisão da política (dado financeiro que a pessoa já informa), mas confirmar se a política de privacidade lista "objetivos e aportes" |

Sem movimentação real, sem integração bancária, sem ordens: o app não movimenta dinheiro. O resgate é um registro do que a pessoa fez no banco (e, se ela quiser, lança a transferência do principal e a receita do rendimento nas contas dela).

---

## 9. Testes e critérios de aceitação

Domínio (`Prisma.Domain.Tests`, sem banco): 
- Motor: um aporte; vários aportes em datas diferentes; mesmo grupo com `p` diferentes; fim de semana e feriado (sem taxa publicada = sem dia de capitalização); aporte em dia sem taxa; **data anterior à primeira taxa** (erro `RatesUnavailable`, nunca zero); **taxas ausentes no meio** (resultado "incompleto" explícito); taxa corrigida (resultado muda e reprodutível); `asOf` = dia do aporte (saldo = principal); `asOf` antes do aporte (erro).
- **Valor esperado vem de fora do código**: vetores calculados independentemente (script Python com `Decimal`, como na seção 5.2, e **uma calculadora pública ou extrato real** conferidos pelo dono), com tolerância **declarada** (ex.: 0 centavo no fator de 8 casas; ≤ 1 centavo por aporte contra extrato, documentado por convenção).
- Propriedades (CsCheck): fator ≥ 1 e monótono não decrescente com taxas ≥ 0; fator de `p=0` é exatamente 1; `Balance` do grupo = soma dos aportes; adicionar um dia nunca diminui o saldo; reordenar aportes não muda o total; duas execuções dão o mesmo resultado.
- Arredondamento: casos de meio centavo, saldo do grupo = soma dos saldos arredondados.
- IR/IOF: **todas as bordas** (dia 29/30 do IOF; 180/181, 360/361, 720/721 do IR); IR só sobre rendimento positivo; rendimento zero ou negativo ⇒ imposto zero; IOF limitado ao rendimento; IR sobre rendimento líquido de IOF; aportes de faixas diferentes no mesmo grupo (soma por lote, nunca uma alíquota para o grupo); `RuleVersion` registrada.
- Validadores: `p` ≤ 0 ou fora da faixa, valor ≤ 0 ou negativo, data futura, vencimento ≤ aplicação.

Integração (`Prisma.Api.Tests`, Postgres 18 via Testcontainers): 
- Sincronização do CDI com `HttpMessageHandler` falso e fixtures reais do BCB (incluindo a **resposta HTTP 200 com HTML** e a **406 de janela >10 anos**): idempotência (rodar 2x), carga em janelas, item inválido recusa o lote, falha e repetição, correção na fonte gera `RevisedAt` e log, lacuna e defasagem detectadas.
- CRUD de objetivos e aportes; **idempotência por `ClientRequestId`**; conflito de concorrência 409; soft delete + restauração; evolução histórica e saldo batendo com o vetor de referência.
- **Isolamento entre usuários**: B não vê, edita, exclui nem restaura objetivo/aporte de A (como `IsolationTests`); `AccountDeletionTests` e o teste de schema cobrem as tabelas novas.
- Contrato real: `[LiveFact]` (`Category=Live`) que consulta a SGS 12 e 4389 e confere `(1+a/100)^(1/252)−1 ≈ d` num dia, e que a janela de 10 anos falha como esperado.
- Arquitetura: tabela nova com dono ou em `GlobalTables`; `reference_rates` sem `UserId`; motor sem referência a infraestrutura; (se D8 aprovado) `BankCalendar` autorizado para o diagnóstico de lacuna.

Frontend (Vitest): formatação, alternância bruto/líquido, estados "defasado/incompleto", parsing do percentual. **Ponta a ponta**: o projeto não tem Playwright (Fase 5); proponho **não** introduzi-lo agora (dependência nova, pede aprovação) e tratar a integração por slice + verificação manual em 320/390/1280, claro/escuro, como nas telas anteriores.

Regressão contra referência: arquivo `fixed-income-reference.json` versionado com casos (entrada, taxas usadas, saída esperada e **origem do número**: calculadora/extrato e data) e teste que lê o arquivo; diferenças aceitas (convenção, arredondamento, corte de horário) ficam anotadas no próprio caso.

**Critério de aceitação da v1 (bruto):** (1) o exemplo da seção 5.2 reproduz-se ao centavo com as taxas fixtures; (2) pelo menos **um extrato real do dono** (Itaú ou Íon) é reproduzido com diferença ≤ R$ 0,01 por aporte, ou a diferença é explicada e documentada; (3) `dotnet test` verde, `npm run build`/`lint`/`test` verdes; (4) nenhum teste só confere "maior que o principal".

---

## 10. Riscos

| Risco | Tipo | Mitigação |
|---|---|---|
| Convenção de cálculo do banco ≠ B3 (corte de horário, início da capitalização, % sobre taxa anual) | financeiro | validar com extrato real **antes** de liberar; mostrar a convenção; tolerância documentada |
| IR/IOF mudarem por lei (já houve a MP 1.303) | financeiro/manutenção | regras versionadas por data, texto oficial conferido na implementação, aviso "estimativa" |
| Mostrar líquido como se fosse definitivo | produto | rótulos fixos, nunca "ganhou" |
| API do BCB devolver 200 com HTML, fora do ar ou mudar o limite | técnico | validação de conteúdo, lote recusado, `ratesStale`, `[LiveFact]` |
| Série revisada na fonte e histórico mudar sem ninguém ver | integridade | `RevisedAt` + log |
| Dupla contagem com "Investido" do Resumo, ou aporte e transferência saírem de sincronia | produto | D3 reaberta: criação atômica e ligação explícita; teste de que receita/despesa não mudam |
| Tarefa perder a janela por reinício | operação | roda ao subir se defasado, dois horários |
| Estouro numérico no fator | técnico | `Int128` com teto de prazo/valor e teste de propriedade |
| Escopo crescer (resgate, outros produtos) | manutenção | domínio com `ProductKind`/`TaxRuleSet` extensíveis, mas só CDB entregue |
| Atrito do cadastro manual (digitar vários aportes) | produto | pré-preenchimento, e a pergunta sobre importação fica para a Fase 4 |

---

## 11. Decisões

**Aprovadas pelo dono em 2026-10-17, com a recomendação:** D1, D2, D4, D5, D6, D7, D8, D9, D10, D11, D12. Todas podem ser revistas durante a implementação, conforme a visualização e a prática mostrarem.
**Reaberta:** D3 (abaixo). **Nome na tela:** "Cofrinho" é só um nome de trabalho de quem inspirou a ideia e **não aparece na interface**: a tela diz "objetivo" e "investimento"/"aporte".


| # | Decisão | Recomendação | Alternativas |
|---|---|---|---|
| **D1** | Representação numérica e arredondamento | `Int128` ponto fixo 16 casas (trunca como a B3), centavos por `round` meio-para-cima por **aporte**, total = soma | `decimal` dentro do motor (exceção à regra 1); arredondar só o total |
| **D2 (confirmada pelo extrato)** | Início da capitalização: a taxa do dia da aplicação entra | confirmada com 2 casos reais do banco, diferença ≤ R$ 0,01 (5.2) | capitalizar só a partir do dia útil seguinte |
| **D3 (reaberta)** | Relação com contas/transferências e com o "Investido" do Resumo | **o aporte pode sair de uma conta**: no formulário, "Sai de qual conta?" (lista das contas correntes/dinheiro) + "Entra em qual conta de investimento?"; ao salvar, o app cria **a transferência** (par ligado por `TransferPairId`, regra 5) **e** o aporte, na mesma gravação, e o aporte guarda a ligação (`TransferPairId?`). Resultado: o saldo do Itaú cai R$ 10.000, o card "Investido" do Resumo sobe R$ 10.000 e nada é contado duas vezes. "Não sai de nenhuma conta" continua existindo (investimento antigo que você só quer acompanhar) | independente (como estava); só vincular a uma transferência **já existente** |
| **D4** | Faixa permitida do `% do CDI` | de 0,01% a 500%, 4 casas | só 1–200%; só múltiplos de 0,5 |
| **D5** | Resgates na v1 | **fora**; objetivo/aporte podem ser removidos ("Desfazer"), sem retirada parcial; **revista pela D18 (2026-10-19): o resgate parcial e total entrou, por aporte** | resgate por lote (FIFO) já na v1, o que traz IR/IOF reais ao motor |
| **D6** | Horário(s) da sincronização | 19h30 e 23h30 (SP), mais ao subir se defasado | 1x ao dia às 4h, como os ativos (o CDI do dia só entraria no dia seguinte) |
| **D7** | Lugar na interface | aba "Objetivos" em `/contas/investimentos` | rota própria; item novo no menu |
| **D8** | Autorizar `BankCalendar` para diagnosticar lacuna do CDI (ajusta `BankCalendarUsageTests`) | sim, só para diagnóstico, nunca para calcular | diagnosticar sem calendário (só "defasagem") |
| **D9** | Carga inicial do histórico | desde 2015-01-01 em janelas de 10 anos | desde 1986 inteiro; só desde a data do aporte mais antigo |
| **D10** | Tributação na v1 | **bruto nas etapas 1–5; líquido na 6**, depois de conferir o texto oficial do IR | líquido já na v1 |
| **D11** | Nomes (código em inglês, UI em pt-BR) e glossário no `CLAUDE.md` | `SavingsGoal`/"objetivo", `FixedIncomeDeposit`/"aporte", `ReferenceRate`/"taxa de referência" | nome comercial de produto na tela (descartado: "cofrinho" não aparece) |
| **D12** | Teste ponta a ponta com Playwright | não agora (Fase 5); integração por slice + verificação manual | introduzir Playwright já |
| **D13 (2026-10-17)** | A renda fixa na aba Carteira (R.9) | **um grupo só, "Renda fixa", para qualquer índice**; peso pelo **aportado** (o custo da renda fixa, a mesma régua da bolsa); **linhas por objetivo**; rendimento bruto ao lado; vale por ano (aportado até o fim dele) | pesar pelo saldo com rendimento (mistura custo com valor atual); linhas por aporte ou por índice (lista longa) |
| **D18 (2026-10-19)** | Resgate de renda fixa (revê a D5; R.22 a R.26) | **parcial e total**, por aporte; principal e rendimento saem na proporção, faixa de IR do aporte; imposto calculado, com o **líquido do extrato opcional** (vale o do extrato); contas **opcionais** (principal como transferência, rendimento líquido como receita); só o último resgate de um aporte se exclui (`docs/renda-fixa-resgate.md`; **decisões da seção 1 e as quatro recomendações da seção 8 aprovadas pelo dono**) | só total; FIFO por objetivo (entrou depois, no lote: D19); transferir só o líquido (deixaria a conta de investimento negativa) |
| **D19 (2026-10-21)** | Resgate em lote (R.27 a R.31; estende a D18) | um resgate do extrato que cobre **vários aportes**: o valor sai do aporte **mais antigo ao mais novo**, com ajuste à mão na prévia (desmarcar um aporte redistribui o valor); entrada no modo "Selecionar" da lista de aportes; **uma conta de destino** para o lote (aporte sem origem em conta entra só como registro); líquido do extrato **um só**, repartido entre os aportes na proporção do imposto estimado; **cada aporte mantém o seu resgate**, o lote os cria juntos, tudo ou nada, e se desfaz junto (`docs/renda-fixa-resgate.md`, seções 15 e 16) | só um botão por aporte (o extrato mostra um resgate só); marcar à mão sem divisão automática; cada aporte na sua conta; resgate por objetivo como entidade própria |
| **D21 (2026-10-28)** | "Investido" vem dos ativos, não das contas (I.9) | achado do dono: dos 16 aportes de renda fixa do banco local, só 1 entrava no "Investido" (o único com transferência viva), e as ações só entravam se a nota fosse vinculada; para o app enxergar o dinheiro de anos atrás, a pessoa teria de recriar todo o histórico de entradas, senão as contas ficariam negativas. **O "Investido" passa a ser a Carteira de hoje** (principal que sobra de **todos** os aportes, depois dos resgates, mais as ações ao custo) **mais o rendimento bruto de toda a renda fixa**, calculado pelo domínio (`NetWorthSplit`) em `GET /dashboard/net-worth`. A conta de investimento só entra se a pessoa liga **"acompanhar o caixa"** (`Account.TracksCash`, desligado por padrão): o **caixa livre** dela é o saldo menos o principal que sobra dos aportes que ela financiou (que já está no Investido), pode ser negativo e nunca é escondido. O saldo da conta **não muda**. O D20 fica como histórico. O card do mês, que é fluxo, passa a se chamar "Aportado" | manter a regra por trilha de caixa e pedir o histórico de entradas (insustentável para quem investe há anos); somar o rendimento só dos aportes com conta; esconder o caixa livre negativo; ligar o acompanhamento por padrão (o histórico antigo faria o caixa livre aparecer negativo na primeira tela) |
| **D20 (2026-10-21)** | "Disponível" × "Investido" nas contas (R.32 a R.34) | achado do dono: depois de aportar R$ 10 mil, o Itaú mostra o que sobrou, mas o "Em contas" soma o dinheiro livre com o investido e a conta de investimento parece caixa. **O saldo da conta não muda** (continua o principal, e bate com o extrato). O que muda é a leitura: o "Em contas" do Resumo vira **Disponível** (corrente e carteira) + **Investido** (contas de investimento **mais o rendimento bruto** da renda fixa que entrou por elas); a lista de Contas mostra a conta de investimento como "Investido", com o rendimento ao lado. A API acrescenta `byAccount` em `GET /fixed-income/balances` (rendimento bruto do que sobra, por conta de investimento, só dos aportes **financiados por conta**: aporte só acompanhado não tem conta e fica de fora, porque o dinheiro dele nunca saiu do saldo de ninguém). Só acrescenta (compatível com a tela aberta) | mexer no saldo da conta (deixaria de bater com o banco e com a transferência); somar o líquido estimado (imposto não é devido antes do resgate; a Carteira já usa o bruto); card novo no Resumo |
| **D17 (2026-10-19)** | IPCA e IPCA+ (R.17 a R.21) | **estender o modelo atual** com uma tabela mensal própria (a `reference_rates` não comporta valor negativo nem frequência mensal), número-índice e variação, cálculo pela razão dos índices; convenção de apropriação (F1 a F3) **decidida só com extrato real** (R.17); produto da primeira entrega: CDB, LCI e LCA (`docs/renda-fixa-ipca.md`) | substituir a tabela do CDI; estratégias genéricas por modalidade; repetir o valor mensal em todos os dias |
| **D16 (2026-10-19)** | Admin dos dados de mercado (R.15, R.16) | rota e tela de admin para disparar a sincronização do BCB à mão (CDI, depois IPCA), **sem tirar o horário automático**; admin por lista de e-mails na configuração; histórico das execuções em tabela; execução síncrona com teto de 90 s; código do erro do firewall do BCB guardado (`docs/admin-mercado.md`, M1 a M6; **M1 e M2 aprovadas pelo dono em 2026-10-19, R.15 e R.16 feitas**) | papéis do Identity; token em cabeçalho; só log; fila com andamento |
| **D15 (2026-10-18)** | LCI e LCA (R.13, R.14) | produto no aporte (`Cdb`, `Lci`, `Lca`), mesma remuneração; **IR zero** nas duas, **IOF zero na LCA e tabela do CDB na LCI**; vencimento abaixo de 6 meses só **avisa** na tela (R.14); IPCA segue fora | tratar a LCI também como isenta de IOF; recusar vencimento abaixo de 6 meses |
| **D14 (2026-10-18)** | CDI+ e pré-fixado (R.10 a R.12) | contrato `Remuneration` (X % do CDI, CDI + taxa a.a., pré) e **uma coluna só, `RateE4`** (o índice diz o que ela é; sugestão do dono), taxa em E4 de 0,01 % a 100 % a.a., **só base 252 dias úteis**; o pré usa o `BankCalendar` (quarto uso liberado, namespace `Prefixed`); validar com extrato real | base 365 dias corridos; contar dias úteis só pelas datas do CDI (quebra com o Banco Central fora do ar) |

---

## 12. Etapas de implementação (pequenas, em ordem de dependência)

**Estado (2026-10-18): feitas R.0 a R.14 e R.4b; falta a conferência com extrato real (CDI+, pré-fixado, LCI e LCA) e os itens 16 a 18 (admin, IPCA e "Depois").** A R.9 (renda fixa na Carteira, D13) e a R.8 (fechamento) não estavam na lista original.

Cada etapa: teste junto com a implementação, `dotnet test` verde, checkbox no `PLAN.md`, parar e reportar (protocolo da seção 9 do `CLAUDE.md`).

1. **R.0 Verificação oficial (sem código) — extrato conferido em 2026-10-17; falta só o texto legal do IR (dispensado pelo dono por ora, a tabela segue como B3 + dono):** abrir o texto de Lei 11.033 art. 1º / IN RFB 1.585 e o status de medidas posteriores; conferir o 5.2 com calculadora e **com um extrato seu**; fechar D1–D4, D10. *Pronto quando:* a tabela de IR está transcrita com fonte primária neste arquivo e o vetor de referência está versionado.
2. **R.1 Domínio do motor:** `FixedIncomeEngine`, ponto fixo `Int128`, vetores e propriedades. Sem banco. *Pronto quando:* os casos de 9 (domínio) passam.
3. **R.2 `ReferenceRate` e cliente do BCB:** entidade global, migration, `IReferenceRateSource`/`BcbSgsRateSource`, fixtures reais (inclui 200-com-HTML e 406), `[LiveFact]`, eventos de log. *Pronto quando:* testes de cliente e de schema passam.
4. **R.3 Sincronização e tarefa:** `ReferenceRateSync`, `ReferenceRateWorker`, upsert idempotente, revisão, lacuna/defasagem, `Rates:Sync:Enabled`. *Pronto quando:* idempotência, falha/repetição e correção na fonte provadas com o código quebrado de propósito (padrão do projeto).
5. **R.4 Objetivos e aportes (API):** objetivo como caixa (`GoalId` opcional, mover entre caixas, exclusão que pergunta entre "só o objetivo" e "objetivo e investimentos"),  entidades, configurações, migration, slices (criar/editar/excluir/restaurar/listar), `ClientRequestId`, `xmin`, `AccountEraser`/`export-user.sql`, `.http`, isolamento. *Pronto quando:* testes de integração e `AccountDeletionTests` passam.
6. **R.5 Consulta de saldo e evolução:** endpoint de resumo e de série histórica, bruto, com `asOf`, `ratesThrough`, `ratesStale`. `gen:api`. *Pronto quando:* bate com o vetor de referência e com o extrato.
7. **R.6 Interface (bruto):** aba "Objetivos", telas de 7.2, gráfico, estados de taxa defasada, 320/390/1280 claro e escuro, Vitest. *Pronto quando:* verificação visual feita pelo dono.
8. **R.7 Tributação (líquido estimado):** `FixedIncomeTax` versionado, IOF por tabela, IR por lote, endpoint e alternância na tela. *Pronto quando:* todas as bordas de 9 passam e o texto oficial está citado no código.
9. **R.8 Fechamento:** `CLAUDE.md` (glossário, tabela global, tarefa em segundo plano), `PLAN.md`, política de privacidade se necessário, mensagem de limitações na tela.
10. **R.4b Aporte que sai de uma conta (D3):** formulário com conta de origem e de investimento; criação atômica da transferência + aporte; editar/excluir o aporte pergunta o que fazer com a transferência ligada (e vice-versa: excluir a transferência pela tela de lançamentos avisa que há aporte ligado); ajuste do `AccountEraser`/cópia de dados; teste de que o "Investido" do mês sobe e de que **receita e despesa não mudam** (regra 5). *Antes de codar:* ler o módulo de transferências (`Features/Transfers`) e o cálculo do "Investido" (`docs/fase-2.md`, 2.1: transferências recebidas por contas `Investment` menos as enviadas) para decidir como ficam o saldo e o rendimento da conta de investimento (hoje o saldo dela só vê principal; o rendimento aparece só nos objetivos).
11. **R.10 CDI+ e pré-fixado, domínio (feita):** `Remuneration`, `AnnualRate`, `FixedPoint`, `Prefixed/PrefixedYield` e as duas fórmulas nos dois motores (5.5). Sem banco, API nem tela: o `FixedIncomeEngine` ganha as sobrecargas com `Remuneration`, e as antigas (`IndexPercent`) seguem iguais.
12. **R.11 CDI+ e pré-fixado, persistência e API (feita):** `FixedIncomeIndexKind` ganha `CdiPlus` e `Prefixed`; coluna única `RateE4` (renomeia `IndexPercentE4`; constraint por índice, `ck_fixed_income_deposits_rate`; migration `AddDepositRateAndIndexKinds`); validação; saldo, evolução, imposto e Carteira usam o contrato; o pré não depende das taxas do CDI (nem do aviso de taxa defasada). A API só acrescenta (aba aberta com o código antigo segue funcionando).
13. **R.12 CDI+ e pré-fixado, tela e documentos (feita; falta só o extrato real, abaixo):** chips "% do CDI | CDI + taxa | Pré-fixado" no aporte, rótulos, nota de limitações, `.http`, `gen:api`, todos os documentos.
14. **R.13 LCI e LCA, domínio, banco e API (feita):** `Lci` e `Lca` em `FixedIncomeProductKind`, regra de imposto por produto em `FixedIncomeTax` (6.5), migration `AddLciAndLcaProducts` (constraint do produto). A API já aceita o produto no pedido que tinha o campo; nada de tela.
15. **R.14 LCI e LCA, tela e documentos (feita; falta só o extrato real):** seletor de produto (CDB | LCI | LCA) no aporte, produto nas linhas, "isento de IR" no lugar do imposto das letras, aviso de vencimento abaixo de 6 meses, nota de limitações, `gen:api`, `.http`, todos os documentos.
16. **R.15 Admin dos dados de mercado, API (feita, `docs/admin-mercado.md`):** política `Admin` (e-mails em `Admin:Emails`), rotas de estado, `sync`, `refresh` e execuções, `market_sync_runs` (migration `AddMarketSyncRuns`), código do erro do BCB, `isAdmin` em `/auth/me`. **R.16 Tela `/admin` (feita).** O horário automático da `ReferenceRateWorker` **não muda**: o disparo manual usa a mesma sincronização e o mesmo lock.
17. **R.17 a R.21 IPCA e IPCA+ (planejadas e EM ESPERA por decisão do dono, 2026-10-19; `docs/renda-fixa-ipca.md`):** verificação com extrato real (sem código), IPCA persistido, domínio do cálculo, API e tela.
18. **(Depois)** IPCA e IPCA+ (cada um com fonte e fórmula próprias, ver 2.5), projeção rotulada. (Resgates e LCI/LCA: feitos, itens 15 e 19.)
19. **R.22 a R.26 Resgate (feitas, `docs/renda-fixa-resgate.md`, D18):** `FixedIncomeRedemption` (parcial e total, por aporte; principal, rendimento e imposto congelados; o líquido do extrato vale se a pessoa o informar), motor com os resgates por trechos, tabela `fixed_income_redemptions`, rotas `/fixed-income-deposits/{id}/redemptions` (criar, prévia, listar, excluir só o último, desfazer), o resgate que cai numa conta (transferência do principal e receita do rendimento líquido, só para aporte financiado), tela ("Resgatar", "Resgatados", prévia do imposto vinda da API) e a Carteira pesando pelo principal que sobra.
20. **R.27 a R.31 Resgate em lote (feitas em 2026-10-21, `docs/renda-fixa-resgate.md`, seções 15 e 16, D19):** "Resgatar tudo" como modo (corrige o valor que ficava velho ao trocar a data), `RedemptionAllocator` (a divisão do valor e a repartição do líquido do extrato, no domínio), rotas `…/redemptions/batch` (prévia, gravação tudo ou nada e desfazer), a tela (modo "Selecionar" na lista de aportes e o painel do lote) e os documentos. Sem migration.

---

## 13. Retomada (estado em 2026-10-21: R.0 a R.16 e R.22 a R.31 feitas; R.17 a R.21 (IPCA e IPCA+) em espera; falta o extrato real de um CDI+, de um pré e de uma LCI/LCA)

**Protocolo.** Uma etapa por vez; teste junto com a implementação; `dotnet test` verde; marcar no `PLAN.md`; parar e reportar. Não commitar sem o dono pedir (a renda fixa foi para a `main` em 2026-10-18, PR #20, em 8 commits por etapa). Provar os testes quebrando o código de propósito e restaurar; nunca ajustar teste para passar. Código em inglês; documentação, interface e erros em pt-BR; dinheiro em centavos. **Nunca "cofrinho" na tela**: usar "objetivo" e "aporte/investimento".

**Entrega (2026-10-18):** mesclada na `main` pelo PR #20 (`feat/renda-fixa`, 8 commits: R.0–R.1, R.2–R.3, R.4–R.4b, R.5, R.6, R.7, R.9 e R.8). O CDI+ e o pré-fixado entraram na R.10 a R.12; IPCA e IPCA+ estão em espera (ver abaixo e `docs/renda-fixa-ipca.md`).

**Verificação do dono (2026-10-18):** testou a tela (objetivos, aportes, bruto e líquido, aporte financiado, gráfico, exclusão com desfazer, Carteira) e confirmou que está tudo certo.

**Feito:** R.0 a R.9 e R.4b na `main` (PR #20); R.10 a R.16 (CDI+, pré-fixado, LCI e LCA, admin dos dados de mercado) na `main` pelo PR #22 (`5ad48b5`); o **resgate (R.22 a R.26)** na `main` pelo PR #23 (`d7373d4`); o **resgate em lote (R.27 a R.31)** está commitado e enviado na branch `feat/renda-fixa-resgate-lote` (`b32faba` na hora de escrever; **sem PR ainda**: o ambiente não tem `gh`, o dono abre pelo GitHub); o **"Disponível × Investido" (R.32 a R.34)** vem na mesma branch, ainda sem commit. Última suíte completa verde (2026-10-21): 2218 testes de .NET (1147 de domínio, 17 de arquitetura, 1054 de integração; 7 ignorados; os `[Live]` ficam de fora) e 581 do Vitest. O líquido (imposto estimado) e a renda fixa na Carteira estão prontos.

**Em espera (decisão do dono, 2026-10-19):** IPCA e IPCA+ (R.17 a R.21). O dono vai buscar dados melhores (extrato real de um CDB IPCA+ e do Tesouro IPCA+, com datas, valor aplicado e saldos em duas ou mais datas) para a R.17, que decide a convenção de apropriação (F1 a F3) e **não tem código**. Até lá a API e a tela recusam IPCA e IPCA+, e nada se implementa. Retomar por `docs/renda-fixa-ipca.md`, seção 5.

**Variável a pôr no Railway:** `Admin__Emails__0` (o e-mail de quem pode abrir a tela de admin; sem ela ninguém é admin; o merge do PR #22 já ocorreu, então vale conferir). As migrations `AddMarketSyncRuns` e, com o resgate, `AddFixedIncomeRedemptions` e `AddRedemptionFunding` já estão na `main` e rodam sozinhas no deploy (só acrescentam tabela e colunas); o resgate em lote não tem migration.

**Decisões do dono que valem na tela**
- O objetivo é uma **caixa**. Excluir um objetivo com aportes **pergunta**: manter os investimentos (ficam em "Sem objetivo") ou excluir junto. Desfazer vale 30 minutos.
- Aporte financiado (saiu de uma conta): valor e data ficam travados; excluir pergunta se a transferência fica ou sai junto.
- Sem projeção futura. Mostrar sempre "rendimento até <data>" e avisar taxa defasada ou incompleta. Aporte sem taxa não some: entra pelo principal e avisa.
- CDB, LCI e LCA (R.13 e R.14), com três jeitos de render: % do CDI, CDI + taxa ao ano e pré-fixado (R.10 a R.12, 5.5). IPCA e IPCA+ são recusados.

**API para a tela:** formatos em 2.4b, 2.4c e 2.4d deste arquivo; exemplos em `src/Prisma.Api/Http/fixed-income.http`.
- Objetivos: `GET/POST /savings-goals`, `PUT /{id}`, `DELETE /{id}?deposits=keep|delete` (a resposta traz `detachedDepositIds` e `deletedDepositIds`), `POST /{id}/restore` (corpo opcional `{ detachedDepositIds }`).
- Aportes: `GET /fixed-income-deposits?goal=<id>|none`, `POST` (`clientRequestId` obrigatório, `funding` opcional), `PUT /{id}`, `PATCH /{id}/goal`, `DELETE /{id}?transfer=keep|delete`, `POST /{id}/restore`.
- Saldo: `GET /fixed-income/balances?asOf=` e `GET /fixed-income/evolution?goal=all|none|<id>&from&to&step=day|week|month`.
- Taxa em E4: no `Cdi`, o percentual do CDI (100% = 1000000); no `CdiPlus` e no `Prefixed`, a taxa ao ano (14% = 140000). Erros com `code`: `GoalNameTaken`, `DepositsChoiceRequired`, `TransferChoiceRequired`, `BeforeFirstRate`, `FundedDepositLocked`, `FundingNeedsInvestmentAccount`, `FundingNeedsPaymentAccount`, `RatesUnavailable`.

**Frontend (R.6, R.7, R.9).** React 19, TypeScript, Vite, React Router, TanStack Query, React Hook Form + Zod, Recharts, shadcn e sonner. Já existe `src/prisma-web/src/features/investments` (`InvestmentsPage` em `/contas/investimentos`, abas Carteira/Proventos/Notas com Radix Tabs): a aba "Objetivos" entra ali. Tipos vêm do `npm run gen:api` (versionar `api-types.ts`). Toda mutação invalida as chaves afetadas (`invalidateMoney`, `lib/queryKeys.ts`). Identidade visual no `CLAUDE.md`, 7.1 (sem verde/vermelho; rendimento em `text-spectrum`; `.surface`; Instrument Serif nos valores grandes; "Desfazer" em toast); conferir 320/390/1280, claro e escuro, e com toque real. Plano das telas na seção 7.

**Pendente** (2026-10-18)
- **Extrato real (do dono):** conferir um CDI+, um pré-fixado, uma LCI e uma LCA do banco contra o app (tolerância de R$ 0,01), como foi com o CDI (R.0). Se divergir, o ajuste é de uma linha: como o fator da taxa fixa é arredondado (5.5). A tela e a API já estão prontas para receber esses aportes.
- Nada obrigatório da renda fixa. **Texto legal (2026-10-20, a pedido do dono):** na R.8 a política e os termos não foram mudados (a seção 3.2 da política não citava objetivos e aportes em renda fixa, a seção 5 não citava o Banco Central e os termos não citavam a estimativa de imposto). Agora foram esclarecidos: a renda fixa (objetivos, aportes e resgates) entra entre os dados registrados, a observação do aporte entre os textos livres, o Banco Central entre as fontes públicas e os termos ganharam a linha da estimativa de rendimento e imposto. **A versão continua 1.0** (`LegalTerms` e os dois `.md` iguais): é esclarecimento, sem finalidade, base legal, retenção ou compartilhamento novos, e a regra do repositório é subir a versão só em mudança que pede aceite. O histórico da política registra o esclarecimento de 20/10/2026. A revisão do advogado (A.2) segue pendente, e ele pode decidir que a mudança pede novo aceite.
- Texto legal do IR: o art. 1º da Lei 11.033 foi conferido em 2026-10-18 (cópia da Câmara: 22,5/20/17,5/15%, igual à tabela do app); a IN RFB 1.585 segue por conferir. A tabela vinha da B3 confirmada pelo dono. A regra é versionada por data: lei nova entra como regra nova em `FixedIncomeTax`.
- Depois (seção 12, item 14): IPCA e IPCA+ (em espera), projeção rotulada, gráfico líquido, cotação.
- Operação: roteiro em `docs/operacao.md`, seção 4.5 (eventos de log, avisos de taxa defasada/incompleta, o que fazer).

**Lições do ambiente**
- O macOS não tem `timeout`. Rodar uma suíte por vez; a completa leva cerca de 5 minutos; usar `run_in_background` e esperar com um laço `until`.
- Os testes de API desligam o reload de configuração em `PrismaApiFactory` (travamento do `FileSystemWatcher` no macOS).
- `reference_rates` é global: teste que depende dela limpa ou semeia a tabela no início.
- Um `dotnet test` de cada vez: duas suítes ao mesmo tempo disputam CPU e o Docker, e deixam tudo lento.
- **Antes de subir uma API local, conferir a porta** (`lsof -tiTCP:5151 -sTCP:LISTEN`; a do dono costuma estar na 5151 e na 7153) e matar só o PID que eu subi; para gerar os tipos sem derrubar a dela, usar uma API paralela (`ASPNETCORE_URLS=http://localhost:7161 ... --no-launch-profile`) e `npx openapi-typescript http://localhost:7161/api/openapi/v1.json -o src/lib/api-types.ts`.
- O banco de desenvolvimento do dono pode ter migration de outra branch (ex.: `AddUserCreatedAt`, coluna `users.created_at` sem valor padrão): o cadastro de um usuário de teste dá 500. Contorno: um `DEFAULT now()` temporário na coluna, removido no fim.
- Teste que ordena tupla com `.Order()` falha de vez em quando se algum elemento da tupla não for comparável (a ordenação compara um elemento com ele mesmo): ordenar por uma chave só (`OrderBy(r => r.Id)`).
