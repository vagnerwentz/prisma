# Admin dos dados de mercado (R.15 e R.16)

> **Estado (2026-10-19): R.15 (API) e R.16 (tela) implementadas.** Decisões M1 e M2 **aprovadas pelo dono em 2026-10-19** (M3 a M6
> seguem como recomendadas e foram implementadas assim). Pedido do dono: poder disparar à mão a sincronização do
> Banco Central (CDI hoje, IPCA depois) por uma rota e uma tela de admin, **sem tirar** a execução automática nos horários
> marcados. Este arquivo é a fonte da verdade da Fase 1 do plano de IPCA (`docs/renda-fixa-ipca.md`). Protocolo:
> `CLAUDE.md`, seção 9 (uma etapa por vez, teste junto, `dotnet test` verde, marcar no `PLAN.md`, parar e reportar).

Legenda: **[V]** verificado no código ou na fonte; **[H]** hipótese a confirmar; **[REC]** recomendação minha, **aguardando o
"de acordo" do dono** (decisão de produto e de segurança é dele).

---

## 1. Por que agora

- O Banco Central é imprevisível: a mesma consulta já respondeu 406, 200 com JSON de erro e 200 com HTML (`docs/renda-fixa.md`,
  3.1). Em 2026-10-18 a produção registrou `ReferenceRateSyncUnavailable` com "unexpected content type 'text/html'" na carga
  inicial, e a investigação do dia seguinte reproduziu a **página de rejeição do firewall do BCB** ("Requisição inválida! … O
  código de erro é: <número>") até para uma janela pequena do CDI, enquanto a SGS 433 (IPCA) respondia normalmente.
- Hoje, para forçar uma sincronização, só reiniciando o serviço (que roda ao subir se o histórico está atrasado) ou chamando
  `ReferenceRateSync.RefreshRangeAsync` por código. Nada mostra o resultado das execuções: só o log.
- O IPCA vai precisar do mesmo painel (fonte mensal, divulgação por volta do dia 10).

## 2. Estado atual [V]

| Peça | Onde | Observação |
|---|---|---|
| Sincronização | `Features/Rates/ReferenceRateSync.cs` | `RunAsync` (janela normal: última data − 10 dias até hoje) e `RefreshRangeAsync(from, to)` (janela livre). Idempotente, `pg_advisory_xact_lock(7_460_001)`, até 3 tentativas contra corrida, tudo ou nada. O comentário diz que só código e teste chamam; **não há endpoint** |
| Tarefa | `Features/Rates/ReferenceRateWorker.cs` + `ReferenceRateSchedule.cs` | 19h30 e 23h30 de São Paulo e ao subir se vazio ou atrasado; `MarketDataException` vira aviso, o resto vira erro; nunca derruba a API |
| Resultado | `ReferenceRateSyncResult(Fetched, Added, Revised, Unchanged, Coverage)` | Só vai para o log (`AppLog`, eventos `ReferenceRate*`) |
| Fonte | `Infrastructure/Market/Bcb/BcbSgsRateSource.cs` | `MarketDataException` com mensagem curta ("unexpected content type 'text/html'"); **não guarda o código de erro da página do firewall** |
| Autorização | `Infrastructure/Auth/AuthSetup.cs` | Só a política padrão "exige login" (`SetFallbackPolicy`). **Não há papel nem flag de admin**: `AppUser` não tem o campo, o Identity não tem tabelas de papéis, `/auth/me` devolve `id`, `email`, `mustAcceptTerms` |
| Padrão parecido | `Infrastructure/Auth/RegistrationOptions.cs` | `Registration:AllowedEmails` (lista de e-mails por configuração) |
| Estado do histórico | `Domain/FixedIncome/Coverage/ReferenceRateCoverage.cs`, `Features/FixedIncome/RateBook.cs` | Primeira e última data, lacunas e defasagem já calculadas |

## 3. Decisões

| # | Decisão | Recomendação [REC] | Alternativa descartada |
|---|---|---|---|
| **M1** | Quem é admin | **Lista de e-mails na configuração** (`Admin:Emails`, variável no Railway). Vazia = ninguém é admin (falha fechada). Exige e-mail confirmado (já obrigatório para entrar) | Papéis do Identity (duas tabelas, migration, semente: exagero para uma pessoa); token secreto em cabeçalho (sem tela, segredo a mais) |
| **M2** | Histórico das execuções | **Tabela global `market_sync_runs`**, escrita pela tarefa e pelo disparo manual, **sem dado pessoal** (quem pediu fica só no log), guarda 90 dias | Só memória (some ao reiniciar) e só log (ninguém vê na tela) |
| **M3** | Execução do disparo | **Síncrona**, com teto de 90 s e a resposta já trazendo o resultado | Fila com consulta de andamento (complexidade sem ganho: uma execução é uma requisição ao BCB) |
| **M4** | O horário automático | **Continua igual** (19h30, 23h30 e ao subir). O disparo manual **não reagenda nem atrasa** a tarefa e divide o mesmo lock | Trocar o automático pelo manual |
| **M5** | Retentativa curta dentro da mesma execução (3 tentativas, 0 s, 2 s e 5 s, só para `MarketDataException`) | **Fora do mínimo; decidir à parte** (R.15b). O BCB às vezes acerta na tentativa seguinte, mas isso muda o comportamento da tarefa | — |
| **M6** | Escopo de séries | **`cdi` agora**; o desenho recebe o código da série para o IPCA entrar na R.18 sem mudar rota nem tela | Lista de ativos da brapi e eventos da empresa: ficam fora (outro serviço, outra tela) |

> Se o dono não responder M1 e M2, a implementação **para e pergunta** antes de começar a R.15 (CLAUDE.md, seção 9).

## 4. API (contrato implementado na R.15)

Tudo sob a política `Admin` (M1): anônimo recebe **401**, quem não é admin recebe **403** (sem dizer que a rota existe para admin).
Respostas e erros em pt-BR (`ProblemDetails`). Rotas mapeadas sem o prefixo `/api`, como as outras.

- `GET /admin/market-data` → o estado de cada série:
  `code` (`cdi`), `name`, `source` (`bcb-sgs-12`), `count`, `first`, `last`, `expectedLast`, `stale`, `complete`, `missingBusinessDayCount`,
  `lastFetchedAt`, `lastRevisedAt`, `schedule` (`runTimes`, `nextRunUtc`, `enabled`: falso se a tarefa está desligada por configuração),
  `lastRun` (a última execução, qualquer origem, ou nulo) e `lastSuccessfulRun`.
- `POST /admin/market-data/{series}/sync` → roda a janela normal. Devolve a execução (abaixo).
- `POST /admin/market-data/{series}/refresh` com `{ from, to }` → reprocessa o período. Validação: `from <= to`, `to <= hoje`,
  `from >=` o início do histórico configurado (`Rates:Sync:InitialLoadFrom`) e **prazo máximo de 10 anos**.
- `GET /admin/market-data/{series}/runs?limit=` → as últimas execuções (padrão 20, máximo 100).
- **A execução (`run`):** `id`, `series`, `kind` (`Scheduled`, `Startup`, `Manual`), `startedAt`, `finishedAt`, `outcome`
  (`Succeeded`, `SourceUnavailable`, `Failed`, `TimedOut`; o `TimedOut` entrou na implementação para a lista mostrar o teto estourado), `windowFrom`, `windowTo`, `fetched`, `added`, `revised`, `unchanged`,
  `sourceErrorCode`, `errorSummary`.
- **Erros com código** (`code` no ProblemDetails, para a tela não depender do texto): `UnknownSeries` (404), `InvalidWindow` (400),
  `SourceUnavailable` (502), `SyncTimedOut` (504) e `SyncFailed` (500, erro inesperado, sem detalhe). Nos três últimos, que são execuções
  que não deram certo, o corpo do problema traz também `sourceErrorCode` (o número do BCB, quando houve) e `run` (a execução gravada). O
  texto de `detail` é fixo por desfecho (`MarketSyncRun.SummaryFor`): nada da mensagem da exceção nem do corpo do BCB chega à resposta.
- Ordem das execuções: `started_at` decrescente e, em empate, o id (Guid v7, que cresce com o tempo).
- `GET /auth/me` ganha `isAdmin` (**só acrescenta**; aba aberta antes do deploy ignora o campo, `CLAUDE.md` seção 6). Login, confirmação do
  e-mail, senha nova e aceite dos termos devolvem o mesmo `SessionUser`, então também trazem `isAdmin`.

## 5. Dados: `market_sync_runs` (tabela global)

Sem `user_id` (não herda `Entity`), então entra em `AppDbContext.GlobalTables` com o motivo (`OwnershipTests` confere): dado
operacional, o mesmo para todo mundo. Colunas: `id` (Guid v7), `series`, `kind`, `started_at`, `finished_at`, `outcome`,
`window_from`, `window_to`, `fetched`, `added`, `revised`, `unchanged`, `source_error_code` (texto até 40), `error_summary` (texto curto
fixo, até 200; nunca o corpo da resposta). Checks para `series`, `kind` e `outcome` (nomes do enum, como as outras tabelas).
Índice `(series, started_at desc)`. **Limpeza:** cada execução apaga as de mais de 90 dias da própria série. Sem dado pessoal: não entra
na cópia dos dados (`ops/privacy/export-user.sql`) nem na exclusão da conta; **quem disparou** só aparece no log
(`MarketDataSyncRequested`, com `userId`).

**Código de erro do firewall do BCB:** o `BcbSgsRateSource` passa a extrair o número de "O código de erro é: <dígitos>" da página HTML
de rejeição para `MarketDataException.SourceErrorCode` (só dígitos, no máximo 40; nada do corpo é guardado ou logado). É o número que o
BCB pede se o dono abrir chamado (central +55 61 3414-2156, que a própria página informa).

## 6. Segurança

- **Política `Admin`** por requisito de autorização (e-mail confirmado e presente em `Admin:Emails`, sem diferenciar maiúsculas). Falha fechada: lista vazia ou mal formada = nenhum admin; a API não sobe se a configuração for inválida (`ValidateOnStart`, como as outras).
- **Teste de arquitetura:** toda rota sob `/admin` carrega a política `Admin` (uma rota nova sem ela quebra o build). Mora em
  `AdminMarketDataApiTests.Every_admin_route_requires_the_admin_policy` (precisa do host montado para listar as rotas; o projeto de
  arquitetura não sobe a API) e confere também que nenhuma rota `/admin` é anônima.
- **O gancho confere no banco a cada pedido** (`AdminAuthorizationHandler`): quem sai da lista, desfaz a confirmação do e-mail ou apaga a conta perde o acesso na hora, sem esperar o cookie vencer. `Admin:SyncTimeoutSeconds` (padrão 90, de 1 a 300) é o teto do disparo manual.
- **Limite de taxa por pessoa** nos `POST` (política nova, como `AssetRequestsRateLimitPolicy`), por exemplo 6 por minuto.
- **Teto da janela** (10 anos) e série validada contra a lista conhecida: nada de URL ou valor livre indo para o BCB.
- **Log** (`AppLog`, `[LoggerMessage]`): `MarketDataSyncRequested` (série, tipo, janela, `userId`) e `MarketDataSyncFinished`. Nunca valor, nome nem e-mail; o teste de arquitetura de `AppLog` recusa parâmetro sensível.
- **O repositório é público** (`CLAUDE.md`, seção 8): aqui só consta o desenho, não há falha aberta. A lista de admins fica em variável de ambiente, nunca no repositório.
- Cookie `SameSite=Lax` e `POST` com JSON: o mesmo modelo das outras mutações. Nada de dado de usuário passa por esta superfície.

## 7. Tela (`/admin`)

- Rota `lazy`, `noindex` como as telas do app, **fora do pacote principal**. A tela só abre se `isAdmin`; senão, a página de "endereço inexistente" (não revela a tela).
- Entrada no **menu da conta**, só para admin.
- Um cartão por série: nome e fonte; estado (**em dia**, **defasado**, **incompleto**, **sem taxas**) com a última data; a **próxima execução automática**;
  a última execução (quando, como, resultado e contagens) e a última que deu certo; o **erro do BCB** com o código, quando houver.
- Ações: **"Sincronizar agora"** (janela normal) e **"Reprocessar período"** (duas datas; confirma antes). Enquanto roda, o botão fica desabilitado e diz "Sincronizando…"; no fim, o resultado em aviso (`sonner`) e o cartão se atualiza (`invalidateQueries`).
- Lista das últimas execuções (tabela simples; em tela estreita vira lista de linhas). Estados vazio, carregando e erro como nas outras telas.
- **Identidade visual:** as mesmas peças e tokens do app (`.surface`, chips, `text-spectrum` só para o que é rendimento, nada de verde e vermelho; o erro usa a cor de erro já existente). Conferir em **320, 390 e 1280, claro e escuro, com toque real**.
- O `gen:api` roda antes (a API paralela 7161, sem tocar na 5151 do dono).

## 8. O que continua como está

- `ReferenceRateWorker`: mesmos horários, mesma janela, mesma regra de "falha nunca derruba a API". Passa a **gravar a execução** em `market_sync_runs` (`Scheduled` ou `Startup`) e o `ReferenceRateNextRun` segue no log.
- O disparo manual usa o **mesmo `ReferenceRateSync`** (mesmo lock, mesma reconciliação): duas execuções ao mesmo tempo não duplicam, a segunda espera a vez (até o teto de 90 s).
- O cálculo de saldo e os avisos de "defasadas"/"incompletas" não mudam.

## 9. Testes (R.15)

Os esperados vêm desta especificação, não do código.
1. **Autorização:** anônimo 401; usuário comum 403; admin 200; `Admin:Emails` vazio → ninguém; e-mail com outra caixa de letra vale; e-mail não confirmado não vale.
2. **`/auth/me`:** `isAdmin` verdadeiro só para o admin; campos antigos intactos.
3. **`sync`:** com a fonte falsa (`FakeReferenceRateSource`), grava, devolve as contagens e registra a execução; repetir dá `added = 0`; fonte que falha dá 502 com `SourceUnavailable`, registra `SourceUnavailable` e **não grava taxa**.
4. **`refresh`:** período válido; `from > to`, `to` no futuro, antes do início do histórico e mais de 10 anos dão 400 `InvalidWindow`; série desconhecida dá 404.
5. **Corrida:** disparo manual junto com a tarefa não duplica (o índice único e o lock existentes).
6. **Código do firewall:** a fixture HTML real ("Requisição inválida", com o número) vira `SourceErrorCode` certo; HTML sem número dá nulo; nada do corpo vai ao log nem à tabela.
7. **Limpeza de 90 dias** da própria série e só dela.
8. **Teto de 90 s** (`SyncTimedOut`) com um `HttpMessageHandler` lento.
9. **Log:** `MarketDataSyncRequested` leva `userId` e a janela, e nenhum valor.
10. **Arquitetura:** rota `/admin` sem política quebra; `market_sync_runs` está em `GlobalTables`.
11. **Mutações** provadas: política removida, `isAdmin` fixo, janela sem teto, execução sem registro, lock removido.

Front (R.16, Vitest): rótulos e estado de cada situação, formatação da janela e do erro com código, guarda de `isAdmin`, texto "próxima execução", e as mutações dos textos de estado.

## 10. Etapas

- **R.15 Admin dos dados de mercado (API).** `Features/Admin/*` (políticas, rotas, handlers), requisito e opções (`Admin:Emails`) em `Infrastructure/Auth`, `MarketSyncRun` + configuração + migration `AddMarketSyncRuns` (**pede autorização para aplicar no banco de desenvolvimento**), registro das execuções na `ReferenceRateSync`/`ReferenceRateWorker`, `SourceErrorCode` no `BcbSgsRateSource`, `isAdmin` no `SessionUser`, eventos em `AppLog`, `.http`, testes da seção 9 e documentos. **Pronto quando:** os testes da seção 9 passam, as mutações morrem e o `dotnet test` está verde.
- **R.15b (opcional, decidir à parte)** retentativa curta (M5).
- **R.16 Admin dos dados de mercado (tela).** `gen:api`, `features/admin/*`, rota e entrada no menu, Vitest, conferência visual. **Pronto quando:** o dono dispara um `sync` e um `refresh` pela tela com o BCB no ar (ou vê o erro com o código com o BCB fora), nos dois temas.

## 11. Documentos a atualizar ao terminar cada etapa

`PLAN.md` (marcar), `docs/renda-fixa.md` (seção 12 e 13), `docs/operacao.md` (4.5: o roteiro "reiniciar o serviço" ganha "use a tela de admin"; eventos novos; `Admin__Emails` no Railway **antes** do merge, como as outras variáveis), `CLAUDE.md` (glossário, tarefa em segundo plano, segurança, estrutura), `README.md`, memória, `.http`.

## 12. Riscos

| Risco | Mitigação |
|---|---|
| Rota de admin aberta por engano | Política por requisito, teste de arquitetura por rota, falha fechada, testes de 401 e 403 |
| Admin disparar muita coisa | Limite por pessoa, teto de janela, lock único |
| O BCB segurar a requisição | Teto de 90 s e `SyncTimedOut` |
| Tabela crescer | Limpeza de 90 dias a cada execução |
| Variável `Admin__Emails` faltar na produção | Sem ela ninguém é admin (a API sobe, a tela some); só aplicar no Railway antes do merge se o dono quiser a tela no ar |
