# Operação da produção (Railway)

Roteiros para a produção de uso próprio (etapas H.2a a H.2c do `PLAN.md`). Nenhum segredo neste
arquivo: senhas ficam no gerenciador de senhas do dono (o app Senhas do Mac) e nas variáveis do Railway.

## 1. Usuário da API no banco e troca de senhas (H.2a)

A API entra no banco como `prisma_app`, dono do banco do app e sem superusuário. O `postgres` fica
só para administração. O script `ops/postgres/app-user.sql` foi testado numa cópia do banco local:
a API subiu como `prisma_app`, aplicou migration pendente, cadastrou, lançou e calculou o resumo.

**Senhas:** gere no seu terminal, fora de qualquer conversa com IA, e guarde no gerenciador de
senhas. Uma para o `prisma_app` e outra para o `postgres`:

```bash
openssl rand -hex 24
```

**Ordem** (a API só fica fora do ar no redeploy do passo 3, se ficar):

1. **Conectar como `postgres`**, com o TCP Proxy ainda ligado. Host, porta e banco estão na aba
   *Connect* do serviço Postgres, em "Public Network":
   ```bash
   psql "postgresql://postgres@<host do proxy>:<porta>/<banco>"
   ```
   A senha atual é pedida na tela e não fica no histórico.
2. **Criar o `prisma_app` e passar as tabelas para ele**, no mesmo psql:
   ```
   \i ops/postgres/app-user.sql
   \password prisma_app
   ```
   A conferência no fim deve mostrar todas as tabelas com o dono `prisma_app`. A API continua
   funcionando: ela ainda entra como `postgres`.
3. **Apontar a API para o `prisma_app`.** No serviço da API, variável `ConnectionStrings__Default`:
   trocar `Username` para `prisma_app` e `Password` para a senha nova (texto, não mais a referência
   `${{Postgres.PGPASSWORD}}`); host, porta e banco continuam com as referências do Postgres.
   Salvar faz o redeploy. **Conferir no celular:** login, um lançamento e o resumo.
4. **Trocar a senha do `postgres`**, de volta ao psql:
   ```
   \password postgres
   ```
   Em seguida, no serviço Postgres, atualizar `PGPASSWORD` e `POSTGRES_PASSWORD` com essa senha (o
   `DATABASE_URL` deriva delas). Mudar só a variável não troca a senha dentro de um banco que já
   existe; por isso o `\password` vem antes. A API não é afetada: ela usa o `prisma_app`.
5. **Conferir que a senha antiga caiu:** `psql` como `postgres` com a senha antiga deve recusar.
6. **Desligar o TCP Proxy** (serviço Postgres, *Settings* → *Networking*). Para usar o DBeaver
   depois, ligar, usar e desligar de novo; o endereço muda a cada vez.

Se o passo 3 der errado (a API não sobe), volte a `ConnectionStrings__Default` para
`Username=postgres` com a referência `${{Postgres.PGPASSWORD}}`: nada foi apagado, só mudou o dono.

## 2. Conta e acesso (H.2a)

- **Plano Hobby** antes do fim do teste. O teste acaba em 30 dias ou quando os US$ 5 são gastos e
  cai no plano Free (US$ 1/mês), que não sustenta a API e o Postgres; volumes de contas de teste
  são apagados 30 dias depois.
- **Limite de gasto** (*usage limit*) na conta, para não haver surpresa na fatura.
- **2FA** no Railway, no GitHub, na Cloudflare (DNS, R2, Email Routing), no Resend e no e-mail dessas contas (a
  política de privacidade afirma, seção 10). Conta nova de serviço do Prisma Money já nasce com 2FA.
- **"Wait for CI"** no serviço da API (*Settings* → *Deploy*): push com CI vermelho não vai para a
  produção.

## 3. Investigar um problema pelos logs (H.3a)

Em produção, cada evento é uma linha JSON que o Railway filtra (serviço da API → *Logs*). Os campos que
importam: `level`, `message`, `eventName`, `traceId`, `userId`, `route`, `statusCode`, `elapsedMs`.

| Pergunta | Filtro no Railway |
|---|---|
| O que aconteceu na requisição do erro (o `traceId` vem na resposta de erro) | `@traceId:4bf92f…` |
| Tudo o que uma pessoa fez | `@userId:01a0…` (o id vem nos próprios logs, como o `UserRegistered`, ou na tabela de usuários) |
| Só os erros | `@level:error` |
| Tentativas de login erradas | `@eventName:LoginFailed` ou `@eventName:LoginUnknownEmail` |
| Rate limit disparando | `@eventName:RateLimited` |
| Telas que quebraram no navegador de alguém | `@eventName:ClientError` (com `@userId:…` para uma pessoa) |
| Requisições de uma rota | `@route:/statements/{id}/pay` |

Um `traceId` identifica **uma requisição**: todos os logs dela têm o mesmo. Erros em requisições
diferentes (o Resumo faz várias ao abrir) têm `traceId`s diferentes; para vê-los juntos, filtre pelo
`userId` e olhe o mesmo minuto.

O `ClientError` traz a tela (`screen`), o tipo (`crash`: a tela quebrou; `silent`: erro num clique ou
numa promessa, sem nada na tela), a versão do build e a pilha no campo `exception`. A pilha vem com os
nomes compactados do build de produção (`index-….js:1:23456`): a mensagem, a tela e o horário costumam
bastar.

Os logs nunca têm valor, descrição, nome de conta, e-mail, senha ou token (`CLAUDE.md`, seção 8). O
Railway guarda 7 dias no Hobby e 3 no Free.

## 4. Tarefa das recorrências (2.25 e 2.26)

Os lançamentos que se repetem são gerados por uma tarefa dentro da própria API (`RecurrenceWorker`), que roda
ao subir e depois de hora em hora, agindo como cada usuário. O débito automático (2.26) passa pela mesma
tarefa: sai no próximo dia útil do vencimento, então num fim de semana ou feriado a execução não gera nada e o
débito aparece no dia útil seguinte. Isso é normal, não falha. Ela não duplica: rodar de novo, duas vezes ao
mesmo tempo ou depois de a API ficar fora do ar só alcança o que faltou (`docs/fase-2.md`, 2.14, A3 a A6). Uma
falha nunca derruba a API: vai para o log, e a próxima hora tenta de novo.

| Pergunta | Filtro no Railway |
|---|---|
| A tarefa está viva? (uma linha por hora, com usuários, lançamentos criados, pendentes e falhas) | `@eventName:RecurrenceRunFinished` |
| Ela subiu junto com a API? (uma linha a cada deploy ou reinício) | `@eventName:RecurrenceWorkerStarted` |
| Alguma série falhou? (o id da série vem no log; tenta de novo na hora seguinte) | `@eventName:RecurrenceFailed` |
| A execução inteira falhou? (ex.: banco fora do ar) | `@eventName:RecurrenceRunFailed` |
| O que uma série gerou | `@eventName:RecurrenceGenerated` (com o `RecurrenceId`) |
| Série pulada porque outra execução ou uma edição chegou antes (normal, sem ação) | `@eventName:RecurrenceSkipped` |
| Séries criadas, editadas e encerradas; pendências lançadas e descartadas | `@eventName:RecurrenceStarted`, `RecurrenceEdited`, `RecurrenceEnded`, `RecurrencePendingLaunched`, `RecurrencePendingDiscarded` |
| Débito automático conferido pelo sino (se o valor foi corrigido, `Corrected` vem `true`) | `@eventName:AmountConfirmed` |

Se o `RecurrenceRunFinished` parar de aparecer, a API está fora do ar ou a tarefa foi desligada: ela liga por
padrão e só desliga com `Recurrences__Runner__Enabled=false` (usado nos testes de integração). Falhas
repetidas da mesma série não somem sozinhas: o id no log leva à série (tabela `recurrences`), e o
`generated_through` dela mostra até onde gerou.

Como os outros eventos, estes só levam ids, tipos e contagens: nunca valor, descrição ou nome.

## 4.1 Catálogo de ativos da bolsa (investimentos, etapa 2)

A API mantém a lista de ativos da B3 (ações, FIIs, ETFs, BDRs) numa tabela global, `assets`, sincronizada com a
brapi por uma tarefa dentro da própria API (`AssetListWorker`): uma vez por dia, às 4h de São Paulo, e logo ao
subir se a tabela estiver vazia (o primeiro deploy). São 3 requisições por sincronização; a lista não exige
token. A brapi lista 2.337 códigos, mas o catálogo fica com uns 1.960 ativos: o fracionário ("BBAS3F") é o mesmo
ativo do lote padrão e entra uma vez só, com o código dele. Depois da lista, a mesma tarefa baixa os logos que faltam
(ou mudaram de endereço), limpa o SVG e guarda em `asset_logos` (uns 1.900 logos, 3 MB, na primeira vez); a tela os
pede ao Prisma, nunca à brapi. Sem nenhum logo guardado, a tarefa roda ao subir. Ativo que some da lista fica inativo (`inactive_since`) e nunca é apagado. Uma falha nunca derruba a
API: vai para o log, e o dia seguinte tenta de novo; perder um dia não muda nada.

| Pergunta | Filtro no Railway |
|---|---|
| A sincronização rodou? (contagens de novos, atualizados, reativados e inativados) | `@eventName:AssetListSynced` |
| Quando roda a próxima? (uma linha ao subir e depois de cada execução) | `@eventName:AssetListNextRun` |
| A brapi respondeu? (páginas, total, itens pulados, tempo) | `@eventName:AssetListFetched` |
| A brapi estava fora do ar ou respondeu errado (aviso, sem ação) | `@eventName:AssetListSyncUnavailable` |
| A lista veio pequena demais e nada foi gravado (trava de sanidade) | `@eventName:AssetListSyncRefused` |
| Falha inesperada (ex.: banco fora do ar) | `@eventName:AssetListSyncFailed` |
| Tipo de ativo novo na brapi, que entrou como `Unknown` | `@eventName:AssetKindUnknown` |
| Item da brapi pulado (código inválido, sem nome, repetido) | `@eventName:AssetListItemSkipped` |
| Logos: quantos pendentes, guardados, removidos, falhos e recusados (depois de cada sincronização) | `@eventName:AssetLogosSynced` |
| Logo que não veio ou não passou na limpeza (com o código do ativo; tenta de novo no dia seguinte) | `@eventName:AssetLogoSkipped` |
| Falha inesperada dos logos | `@eventName:AssetLogoSyncFailed` |

- **`AssetListSyncRefused` repetido** por vários dias: a lista da brapi encolheu de verdade ou mudou de
  formato. Rode o teste de contrato (`PRISMA_LIVE_TESTS=1 dotnet test tests/Prisma.Api.Tests --filter
  Category=Live`) para saber qual.
- **`AssetKindUnknown`**: ensine o tipo novo ao `BrapiAssetKinds` (e ao `AssetKind`, com migration, se for um
  tipo de verdade novo).
- **Configuração** (todas opcionais): `Brapi__Token` (a lista não precisa; a cotação, numa etapa futura, sim),
  `Brapi__BaseUrl`, `Brapi__TimeoutSeconds`, `Brapi__PageSize` (até 2.000), `Brapi__MaxPages`,
  `Assets__Sync__HourOfDay` (0 a 23, hora de São Paulo) e `Assets__Sync__Enabled=false` para desligar.

## 4.2 Eventos da empresa e ativos antigos (investimentos, I.7c1)

Desdobramento, grupamento e conversão (incorporação ou troca de código) ficam numa tabela global,
`corporate_events`, alimentada pela **curadoria**: o arquivo `src/Prisma.Api/Infrastructure/Market/corporate-events.json`,
que vai dentro da imagem. A API o aplica ao subir e depois de cada sincronização do catálogo. Na mesma passada entram,
já inativos, os ativos que saíram da bolsa antes de o catálogo existir (ex.: BCFF11). A carteira de cada pessoa é
recalculada na leitura, então um evento novo ou corrigido vale na hora para todos, sem migração de dados.

**Cadastrar uma troca ou um desdobramento** (ex.: alguém avisa que falta CVBI11):

1. Leia o fato relevante do fundo ou da empresa: data-com (ou o dia da troca), proporção, código de destino. Na troca,
   o último pregão do código antigo sai do fato relevante ou da COTAHIST da B3 (`docs/investimentos.md`, I.7c).
2. No arquivo, acrescente o ativo antigo em `historicalAssets` (código, nome, tipo, último pregão), se ele não estiver
   no catálogo, e o evento em `events`, com uma `key` nova e estável e a `source` (de onde veio o número).
3. `dotnet test` (o teste `Embedded_file_is_valid` confere o arquivo), PR e deploy.
4. Conferir no log: `@eventName:CorporateEventsSynced` (ativos antigos e eventos acrescentados).

**O arquivo conhece `Split`, `Conversion` e `Liquidation`.** Qual usar sai do fato relevante:

- `Conversion`: troca de código ou incorporação em que o custo antigo segue para o destino.
- `Liquidation` (I.7c4): o fundo foi liquidado e pago em cotas de outro, com valor atribuído a elas (ex.: BCFF11 → BTHF11,
  "custo médio de aquisição das novas cotas em R$ 10,25"). Leva `targetUnitPrice` (o valor de cada cota recebida) e,
  se houve dinheiro, `cashPerUnit` (por cota da origem, antes do IR retido; rendimentos do mês ficam fora, são
  proventos). Em texto, com ponto e até 8 casas: `"10.25"`, `"83.3946076"`.

Tipo desconhecido, campo de outro formato ou valor por cota fora do padrão (`"10,25"`, 9 casas) recusa o arquivo
inteiro: nenhum evento muda, e o `dotnet test` acusa (`Embedded_file_is_valid`). Caso com escolha da pessoa (CRFB3) ou
sem documento oficial (IRDM11, por ora): só o ativo em `historicalAssets`, e a pessoa lança as notas
(`docs/investimentos.md`, "Casos reais da curadoria").

Corrigir um evento é mudar a linha dele (a mesma `key`); tirar a linha tira o evento. Nunca troque a `key` de um evento
que já foi para a produção: vira um evento novo e o antigo sai.

| Pergunta | Filtro no Railway |
|---|---|
| A curadoria foi aplicada? (ativos antigos, eventos novos, mudados e tirados) | `@eventName:CorporateEventsSynced` |
| Evento pulado porque o código (ou o destino) não está no catálogo | `@eventName:CorporateEventSkipped` |
| Arquivo recusado e nada mudou (chave repetida, proporção inválida) | `@eventName:CorporateEventsRefused` |
| Falha inesperada | `@eventName:CorporateEventSyncFailed` |

- **`CorporateEventSkipped`**: o destino ainda não veio da brapi (código novo) ou foi digitado errado. Se for código
  novo, a próxima sincronização do catálogo resolve; se não, corrija o arquivo.
- **Configuração:** `Assets__Events__Enabled=false` desliga a passada ao subir (a da tarefa diária continua junto com
  o catálogo).

## 4.3 Ativos que faltam: a fila da curadoria (investimentos, I.7c3)

Quem não acha um código na busca toca em "Avisar que falta". O pedido é só o código, um por pessoa, na tabela
`asset_requests` (com dono). Quando o código entra no catálogo, pela brapi ou pelo arquivo da seção 4.2, a pessoa vê
"CVBI11 chegou" na tela de Investimentos: nada a fazer além de trazer o ativo.

**Ver a fila** (os códigos que esperam, do mais pedido ao menos pedido; sem nome nem e-mail de ninguém). A consulta é a
mesma do teste `AssetRequestsTests`; mudou uma, muda a outra. Na produção, pelo `psql` como na seção 1 (o TCP Proxy
fica ligado só enquanto se consulta):

```sql
SELECT r.symbol, count(*) AS requests, min(r.created_at) AS first_requested_at
FROM asset_requests r
WHERE r.deleted_at IS NULL
  AND NOT EXISTS (SELECT 1 FROM assets a WHERE a.symbol = r.symbol)
GROUP BY r.symbol
ORDER BY requests DESC, first_requested_at;
```

**Atender um código:** pesquisar o que aconteceu com ele (fato relevante, COTAHIST) e seguir o roteiro da seção 4.2.
Código que nunca vai entrar (erro de digitação, ativo de fora da bolsa) fica na fila por ora: a recusa com motivo vem
com a tela de administração (`docs/investimentos.md`, I.7c3).

| Pergunta | Filtro no Railway |
|---|---|
| Quem pediu o quê, quando (só o código e o `userId`) | `@eventName:AssetRequested` |
| Pedidos de um código | `@eventName:AssetRequested @symbol:CVBI11` |
| Avisos cancelados ou dispensados, e desfeitos | `@eventName:AssetRequestRemoved`, `@eventName:AssetRequestRestored` |

- **Limites:** 20 avisos esperando por pessoa; 10 envios por minuto por pessoa (`RateLimiting__AssetRequests__PermitLimit`).

## 4.4 Eventos que alguém disse que não conferem (investimentos, I.7c2)

Cada pessoa confere, uma vez, o que um evento fez com a quantidade dela ("Conferir eventos", em Investimentos). O "Não
confere" fica na tabela `event_reviews` (com dono), com a quantidade que a corretora mostra, quando a pessoa sabe. A
carteira dela não muda: o evento vale até a curadoria corrigir a linha dele no arquivo (seção 4.2) ou tirá-la.

**Ver os "Não confere"** da versão atual de cada evento (sem nome nem e-mail de ninguém). A consulta é a mesma do teste
`EventReviewsTests`; mudou uma, muda a outra:

```sql
SELECT e.key, a.symbol, count(*) AS people, array_agg(r.reported_quantity ORDER BY r.created_at) AS reported
FROM event_reviews r
JOIN corporate_events e ON e.id = r.corporate_event_id
JOIN assets a ON a.id = e.asset_id
WHERE r.verdict = 'Disputed' AND r.deleted_at IS NULL AND r.event_version = e.updated_at
GROUP BY e.key, a.symbol
ORDER BY people DESC, e.key;
```

**Atender:** conferir o evento no fato relevante. Se estiver errado, corrigir a linha dele no arquivo (a mesma `key`):
no deploy, o evento muda, e ele volta a pedir conferência a quem já tinha conferido, inclusive a quem disse que não
conferia. Se estiver certo, nada a fazer por ora (a resposta à pessoa vem com a tela de administração).

| Pergunta | Filtro no Railway |
|---|---|
| Quem disse que um evento não confere (só o evento e o `userId`) | `@eventName:EventReviewDisputed` |
| Conferências feitas e desfeitas | `@eventName:EventReviewsConfirmed`, `@eventName:EventReviewUndone` |

## 5. Backup fora do Railway (H.2b)

Todo dia às 03:00 de São Paulo, o serviço `backup` copia o banco (`pg_dump`), tranca a cópia com a sua chave
pública do `age` e a guarda no Cloudflare R2. Só a chave privada, que fica só com você, abre a cópia. O R2 guarda
90 dias e não deixa apagar nada com menos de 30 (trava do bucket), nem com o token. O Healthchecks.io manda
e-mail se um dia o backup falhar ou não rodar.

**Onde está cada coisa** (montado em 2026-10-02):

| Peça | Onde fica | Nome e configuração | Segredo e onde ele está |
|---|---|---|---|
| Código | repositório, `ops/backup/` | `Dockerfile` (Alpine 3.23, `pg_dump` 18, `age`, `rclone`, `curl`), `backup.sh`, `restore.sh`, `counts.sql`, `test/roundtrip.sh` | — |
| Serviço | Railway, mesmo projeto da API | `backup`; cron `0 6 * * *` (UTC = 03:00 em São Paulo); *Restart Policy* `Never`; *Watch Paths* `/ops/backup/**`; "Wait for CI" ligado | variáveis do serviço (seção 5.1, passo 6) |
| Usuário do banco | Postgres de produção | `prisma_backup`, só lê (`pg_read_all_data`); criado por `ops/postgres/backup-user.sql` | senha na variável `PGPASSWORD` do serviço `backup` |
| Chave pública do `age` | Railway | variável `AGE_RECIPIENT` (`age1900pv…`); não é segredo | — |
| Chave privada do `age` | só com o dono | **a única que abre os backups; não há como recuperá-la** | app **Senhas** do Mac (iCloud), entrada `Prisma — chave do backup (age)`, campo Notas; e uma cópia fora do computador (papel ou pendrive) |
| Cópias | Cloudflare → **R2 Object Storage** | bucket `prisma-backups`, classe Standard; ciclo de vida: apagar com 90 dias (e a regra padrão que aborta envios incompletos em 7); trava (*Bucket lock*): 30 dias | — |
| Acesso ao bucket | Cloudflare → R2 → *Manage API tokens* | token de conta `prisma-backup-railway`: **Object Read & Write**, só no bucket `prisma-backups`, sem validade | *Access Key ID* e *Secret* nas variáveis `RCLONE_CONFIG_R2_*` do serviço `backup` |
| Alerta | **Healthchecks.io** (plano grátis) | check `prisma-backup`: cron `0 6 * * *`, fuso UTC, tolerância de 1 hora; avisos por e-mail | endereço de aviso na variável `HEALTHCHECK_URL` do serviço `backup` |
| Verificação contínua | GitHub Actions | job `Backup (round trip)` do `.github/workflows/ci.yml`, a cada push e PR | — |

Os nomes dos arquivos são `prisma-<data e hora em UTC>Z.dump.age` (ex.: `prisma-2026-10-02T172151Z.dump.age`).

O CI (job `Backup (round trip)`) faz backup e restauração num Postgres de teste, com o schema real, a cada push.

**Perder a chave privada é perder todos os backups.** Guarde em dois lugares antes da primeira execução.

### 5.1 Preparar (uma vez)

1. **Versão do Postgres.** No serviço Postgres do Railway, a imagem diz a versão: hoje `postgres-ssl:18`
   (conferido em 2026-10-02). A imagem do backup usa o `pg_dump` 18. Se o Railway mudar de versão, a imagem
   precisa acompanhar (`ops/backup/Dockerfile`), senão o `pg_dump` recusa e o Healthchecks avisa.
2. **Chave do `age`**, no seu terminal:
   ```bash
   brew install age
   age-keygen -o ~/prisma-backup-key.txt     # mostra a chave pública (age1...)
   ```
   O arquivo tem três linhas: a data, a chave pública (comentário) e a privada (`AGE-SECRET-KEY-1...`).
   Guarde o arquivo inteiro no app **Senhas** do Mac (sincroniza pelo iCloud com o iPhone): `pbcopy <
   ~/prisma-backup-key.txt` copia sem mostrar na tela; numa entrada nova (título `Prisma — chave do backup
   (age)`), cole no campo **Notas**; depois `pbcopy < /dev/null`. Faça também uma cópia fora do computador
   (impressa ou num pendrive guardado), para o caso de perder o acesso à conta Apple. Só apague o arquivo do Mac
   (`rm ~/prisma-backup-key.txt`) depois de restaurar com a cópia do cofre (5.2). A chave pública (`age1...`)
   não é segredo.
3. **Cloudflare R2:**
   - criar o bucket `prisma-backups`;
   - em *Settings* → *Object lifecycle rules*: apagar objetos depois de 90 dias;
   - em *Settings* → *Bucket lock rules*: reter todos os objetos por 30 dias;
   - em *Manage API tokens*, criar um token **Object Read & Write** aplicado **só a esse bucket**. Anote o
     *Access Key ID*, o *Secret Access Key* e o endpoint (`https://<account id>.r2.cloudflarestorage.com`).
4. **Healthchecks.io:** criar o check `prisma-backup` com agenda cron `0 6 * * *`, fuso UTC e tolerância de
   1 hora. Copiar o endereço de aviso (`https://hc-ping.com/...`). O e-mail da conta já recebe os alertas.
5. **Usuário `prisma_backup`.** Gere uma senha (`openssl rand -hex 24`) e guarde. Ligue o TCP Proxy do Postgres
   e, como na seção 1:
   ```
   psql "postgresql://postgres@<host do proxy>:<porta>/<banco>"
   \i ops/postgres/backup-user.sql
   \password prisma_backup
   ```
   A conferência deve mostrar `superusuario` e `escreve` falsos e `le_tudo` verdadeiro. **Desligue o TCP Proxy.**
6. **Serviço `backup` no Railway**, lendo o mesmo repositório da API com outro `Dockerfile`. **Nessa ordem:**
   criar com *+ Create* → **Empty Service** (renomear para `backup`), preencher as variáveis e só então ligar o
   repositório. Criado direto de *GitHub Repo*, o Railway publica na hora com o `Dockerfile` da raiz (uma cópia
   da API, sem configuração).
   - *Variables* → *Raw Editor* (as de `${{ }}` são referências ao serviço Postgres; o nome tem de ser o dele):
     ```
     RAILWAY_DOCKERFILE_PATH=ops/backup/Dockerfile
     PGHOST=${{Postgres.PGHOST}}
     PGPORT=${{Postgres.PGPORT}}
     PGDATABASE=${{Postgres.PGDATABASE}}
     PGUSER=prisma_backup
     PGPASSWORD=<senha do passo 5>
     AGE_RECIPIENT=<chave pública age1...>
     BACKUP_DEST=r2:prisma-backups
     RCLONE_CONFIG_R2_TYPE=s3
     RCLONE_CONFIG_R2_PROVIDER=Cloudflare
     RCLONE_CONFIG_R2_REGION=auto
     RCLONE_CONFIG_R2_ENDPOINT=<endpoint do passo 3>
     RCLONE_CONFIG_R2_ACCESS_KEY_ID=<do passo 3>
     RCLONE_CONFIG_R2_SECRET_ACCESS_KEY=<do passo 3>
     RCLONE_CONFIG_R2_NO_CHECK_BUCKET=true
     HEALTHCHECK_URL=<endereço do passo 4, sem barra no final>
     ```
   - *Settings*: *Source* → **Connect Repo** (`vagnerwentz/prisma`, `main`) e "Wait for CI" ligado; *Build* →
     *Watch Paths* `/ops/backup/**` (só republica quando o backup muda); *Deploy* → *Cron Schedule*
     `0 6 * * *` (UTC: o agendador do Railway não conhece o `IClock`; 03:00 em São Paulo, que não tem horário de
     verão) e *Restart Policy* `Never` (quem avisa a falha é o Healthchecks).
7. **Primeira execução.** Para não esperar a madrugada, ponha o cron uns 10 minutos à frente, em UTC (São Paulo
   + 3 h: 14:20 em São Paulo é `20 17 * * *`), e volte para `0 6 * * *` depois. Confira: uma linha
   `backup: count` por tabela e `backup: ok file=... bytes=...` no log (o painel pode embaralhar a ordem de linhas
   do mesmo instante; as de `checkpoint` são do Postgres), o arquivo no bucket com o mesmo tamanho e o check verde
   no Healthchecks. Guarde as contagens: são a referência da restauração de prova.

### 5.2 Restauração de prova

É o "Pronto quando" da H.2b. Refaça de tempos em tempos (a cada poucos meses, ou depois de mudar o backup):
backup nunca restaurado não é backup.

1. Baixe o arquivo mais recente do bucket (painel do R2) e mova para `~/Downloads/prisma-backup/`, **fora do
   repositório** (o git o veria como arquivo novo).
2. Recrie a chave privada **a partir do cofre** (o app Senhas), não do arquivo original: num desastre, é o que
   sobra. A ordem importa, porque o `pbpaste` lê a área de transferência na hora do Enter: cole no terminal
   `pbpaste > ~/prisma-restore-key.txt && chmod 600 ~/prisma-restore-key.txt` sem apertar Enter, copie as Notas
   da entrada no app Senhas, volte e aperte Enter. Se o original ainda existir, `cmp` dos dois pode acusar só a
   quebra de linha do fim, que o app descarta: não faz falta.
3. Restaure num **Postgres 18 provisório** (o da produção), separado do banco do dia a dia: os dados de
   produção não se misturam com os de desenvolvimento, e apagar o provisório não toca no seu. Na raiz do repositório:
   ```bash
   docker build -f ops/backup/Dockerfile -t prisma-backup .
   PGPASSWORD="$(openssl rand -hex 16)" && export PGPASSWORD     # senha descartável, só deste terminal
   docker run -d --name prisma-restore-pg -e POSTGRES_PASSWORD="$PGPASSWORD" -p 127.0.0.1:5433:5432 postgres:18
   docker run --rm \
     -v ~/Downloads/prisma-backup:/in:ro -v ~/prisma-restore-key.txt:/key.txt:ro \
     -e AGE_IDENTITY=/key.txt -e PGHOST=host.docker.internal -e PGPORT=5433 -e PGUSER=postgres -e PGPASSWORD \
     prisma-backup /opt/backup/restore.sh /in/<arquivo>.dump.age
   ```
   O `127.0.0.1` importa: sem ele, o Postgres com os dados de produção fica aberto para a rede local (o Wi-Fi).
   Se o restore disser que não conectou, espere alguns segundos e rode de novo: o Postgres ainda está subindo.
   Restaura no banco `prisma_restore`, recriado a cada vez. Os avisos `NOTICE` (banco que ainda não existia,
   rclone sem arquivo de configuração) são normais.
4. **Contagens:** as linhas `restore: count` devem ser iguais às `backup: count` do log da mesma execução no
   Railway. Uma diferença pequena só é normal se alguém lançou algo às 03:00, entre a cópia e a contagem.
5. **Resumo de um mês:** suba uma API **paralela** na porta 7160, apontando para a cópia e servindo o próprio app
   (a do dia a dia e o Vite continuam como estão: o proxy do Vite aponta fixo para a 7153). A tarefa das
   recorrências fica desligada, para não gerar lançamentos na cópia:
   ```bash
   (cd src/prisma-web && npx vite build --outDir /tmp/prisma-restore-web --emptyOutDir)
   ASPNETCORE_ENVIRONMENT=Development ASPNETCORE_URLS=https://localhost:7160 \
   ASPNETCORE_WEBROOT=/tmp/prisma-restore-web Recurrences__Runner__Enabled=false \
   ConnectionStrings__Default="Host=localhost;Port=5433;Database=prisma_restore;Username=postgres;Password=$PGPASSWORD" \
   dotnet run --project src/Prisma.Api --no-launch-profile
   ```
   Abra `https://localhost:7160`, entre com a **sua** conta (a senha é a da produção) e compare o Resumo de um
   mês com o do celular: devem ser idênticos. Não altere nada na cópia.
6. **Limpeza:** a cópia tem os dados de todos que usam o app. Ao terminar: Ctrl+C na API paralela,
   `docker rm -f -v prisma-restore-pg` (apaga o Postgres provisório e os dados dele), `rm -rf
   /tmp/prisma-restore-web`, apague o arquivo baixado e `~/prisma-restore-key.txt`, e feche o terminal (o
   `PGPASSWORD` some com ele).

**Feita em 2026-10-02** com o backup `prisma-2026-10-02T172151Z.dump.age` (57.743 bytes) e a chave do cofre:
as 13 contagens iguais às do log e o Resumo igual ao da produção.

### 5.3 Emergência: restaurar a produção

Ainda não ensaiado. Se o banco de produção se perder:

1. Crie um Postgres novo no projeto do Railway e ligue o TCP Proxy dele.
2. Rode o passo 3 da seção 5.2 contra ele: `PGHOST`, `PGPORT`, `PGUSER=postgres` e a senha do Postgres novo,
   e `-e RESTORE_DB=<banco do Postgres novo>`. O script só restaura num banco sem tabelas.
3. Rode `ops/postgres/app-user.sql` e `ops/postgres/backup-user.sql` nele (a restauração deixa tudo com o
   `postgres`) e defina as senhas.
4. Aponte a API (seção 1, passo 3) e o serviço `backup` (seção 5.1, passo 6) para o Postgres novo. Desligue o
   TCP Proxy. As sessões continuam valendo: as chaves do cookie vêm na cópia.
5. Antes de reabrir, reaplique as contas apagadas depois da cópia (5.6).

### 5.4 No dia a dia

Nada a fazer enquanto o Healthchecks não mandar e-mail. Se mandar, o log do serviço `backup` no Railway diz o
motivo (`backup: failed status=...` e a linha anterior). O backup não usa o `AppLog`: é um script, e as linhas
são texto simples, com só nomes de tabela, contagens, nome e tamanho do arquivo.

### 5.5 Manutenção

| Quando | O que fazer |
|---|---|
| **A cada 3 meses** (próxima: janeiro de 2027) ou depois de mudar o backup | Restauração de prova (5.2). Anote a data e o arquivo no fim da 5.2. |
| O Railway mudar a versão do Postgres | Trocar o pacote `postgresql<N>-client` (e o Alpine, se preciso) em `ops/backup/Dockerfile` e o `postgres:<N>` do teste (`ops/backup/test/roundtrip.sh`). Sem isso, o `pg_dump` recusa e o Healthchecks avisa. No mesmo PR, levar o desenvolvimento e os testes para a mesma versão (seção 6). |
| Mudar o horário do backup | Mudar o cron no Railway **e** no check do Healthchecks: os dois em UTC e iguais, senão chega alerta falso. |
| Suspeita de vazamento do token do R2 | Criar outro token (5.1, passo 3), trocar as variáveis `RCLONE_CONFIG_R2_*` e apagar o antigo na Cloudflare. A trava de 30 dias impede que um token vazado apague as cópias recentes. |
| Suspeita de vazamento da senha do `prisma_backup` | `\password prisma_backup` (com o TCP Proxy ligado só durante isso) e atualizar `PGPASSWORD` no serviço. A senha só lê: não altera nem apaga nada. |
| Perda da chave privada | As cópias antigas viram ilegíveis. Gerar um par novo (5.1, passo 2), trocar `AGE_RECIPIENT`, guardar a privada nos dois lugares e fazer uma restauração de prova com a cópia seguinte. |
| Trocar de Mac ou de conta Apple | Conferir que a entrada da chave continua no app Senhas, ou levá-la para o novo cofre antes de apagar o antigo. |


### 5.6 Contas apagadas, depois de restaurar (A.3)

A cópia restaurada é de antes das exclusões mais recentes: quem apagou a conta depois dela volta a existir no banco. A
política de privacidade promete apagar de novo. A API faz isso sozinha para todo id que estiver em `deleted_accounts`
(ao subir e a cada 10 minutos, evento `AccountDeletionReapplied`), mas a tabela restaurada também é velha: falta pôr
nela os ids apagados depois da cópia.

1. **Se o banco antigo ainda abre** (restauração por dado errado, não por perda), copie a lista dele:
   `SELECT user_id, deleted_at FROM deleted_accounts WHERE deleted_at >= '<data da cópia>';`
2. **Se o banco se perdeu**, a lista sai dos logs da API no Railway: filtro `@eventName:AccountDeleted`, do dia da
   cópia até hoje; cada linha traz o `userId`. Os logs do Railway guardam 7 dias (Hobby): restaure sempre a cópia mais
   recente e faça este passo logo.
3. Insira os ids no banco restaurado (como `postgres`, com o TCP Proxy ligado só durante isso):
   `INSERT INTO deleted_accounts (user_id, deleted_at) VALUES ('<id>', '<instante do log>') ON CONFLICT DO NOTHING;`
4. Suba a API. Confira no log um `AccountDeletionReapplied` por id que estava no banco, e nenhum `ErasureRunFailed`.

| Pergunta | Filtro no Railway |
|---|---|
| Quem apagou a conta (a lista de reserva para depois de restaurar) | `@eventName:AccountDeleted` |
| Tentativas de apagar com a senha errada ou bloqueada | `@eventName:AccountDeletionRejected` |
| A limpeza está viva? (uma linha a cada deploy) | `@eventName:ErasureWorkerStarted` |
| Quanto a limpeza apagou (só quando apagou algo) | `@eventName:DeletedItemsPurged` |
| Conta que voltou com um backup e foi apagada de novo | `@eventName:AccountDeletionReapplied` |
| A limpeza falhou | `@eventName:ErasureRunFailed` |

Os logs do Railway duram 7 dias no Hobby (seção 3): o passo 2 só funciona com uma cópia de menos de uma semana.

Os ids ficam 90 dias em `deleted_accounts` (o prazo das cópias) e depois são esquecidos (`DeletedAccountsForgotten`).
O que a pessoa exclui pelo "Desfazer" não precisa de passo nenhum: a cópia restaurada traz a linha com o `deleted_at`,
e a limpeza a apaga de novo na primeira passada.

**Se a limpeza falhar** (`ErasureRunFailed` a cada 10 minutos): a passada inteira é desfeita, e nada é apagado até a
causa sair. Quase sempre é tabela nova com chave estrangeira para uma tabela limpa, sem estar no `ErasureRunner`. O
teste `DeletedItemsPurgeTests` deveria ter pego antes.

## 6. Postgres local: mudar de versão

O desenvolvimento (`docker-compose.yml`) e os testes (`PostgresFixture`) usam a mesma versão da produção. Quando
ela mudar, troque a imagem nos dois. Nos testes basta isso: o Testcontainers cria um banco vazio a cada execução.

O banco local tem dados, e o Postgres não lê a pasta de dados de outra versão principal: a passagem é copiar do
antigo e restaurar no novo, num volume novo, deixando o antigo como plano B. Foi assim do 17 para o 18
(2026-10-02). Na raiz do repositório, com o banco antigo ainda no ar:

1. **Copiar tudo**, para fora do repositório (o arquivo tem os dados de quem usa o app localmente):
   ```bash
   docker exec prisma-postgres-1 pg_dumpall -U prisma > ~/prisma-local-dumpall.sql
   tail -3 ~/prisma-local-dumpall.sql        # termina em "PostgreSQL database cluster dump complete"
   ```
2. **Contar as linhas de cada tabela**, para comparar depois. O mesmo comando vale no passo 5:
   ```bash
   docker exec -i prisma-postgres-1 psql -U prisma -d prisma -At -F' ' > ~/prisma-contagens-antes.txt <<'SQL'
   select string_agg(format('select %L as t, count(*) as n from %I.%I', schemaname||'.'||relname, schemaname, relname), ' union all ' order by schemaname, relname)
   from pg_stat_user_tables \gset
   :string_agg order by t;
   SQL
   ```
3. **Trocar a versão no `docker-compose.yml`**: a imagem e o nome do volume (ex.: `postgres18-data`), conferindo
   na página da imagem oficial onde a versão nova guarda os dados (a partir do 18, o volume monta
   `/var/lib/postgresql`, não mais `.../data`). Depois, `docker compose up -d --wait`: o container é recriado com
   um volume vazio, e a imagem já cria o usuário e o banco `prisma`.
4. **Restaurar e recalcular as estatísticas** (elas não vêm na cópia):
   ```bash
   docker exec -i prisma-postgres-1 psql -U prisma -d postgres -q < ~/prisma-local-dumpall.sql
   docker exec prisma-postgres-1 psql -U prisma -d prisma -qc "analyze"
   ```
   Dois erros são normais: `role "prisma" already exists` e `database "prisma" already exists` (a imagem já os
   criou). Qualquer outro, pare e investigue.
5. **Conferir**: rode a contagem do passo 2 para `~/prisma-contagens-depois.txt` e compare com
   `diff ~/prisma-contagens-antes.txt ~/prisma-contagens-depois.txt` (nenhuma diferença); `select version()`
   mostra a versão nova; a API sobe e `/api/health` responde `Healthy`; `dotnet test` verde.
6. **Limpar**: apague `~/prisma-local-dumpall.sql` e as contagens. **Voltar atrás**, se preciso: devolva a imagem e
   o volume antigos ao compose e rode `docker compose up -d`. Quando não precisar mais do antigo,
   `docker volume rm prisma_postgres-data` (o nome antigo; `docker volume ls` lista).

## 7. Página de apresentação e domínio

A página de apresentação (`docs/landing.md`) já vai na imagem, mas só aparece em `/` com o cadastro aberto
(`Registration__Open=true`). Até lá, nada muda na produção.

- **Domínio `prismamoney.com.br`** (no ar desde 2026-10-13; o porquê de cada escolha em `docs/landing.md`, seção 10):

  | Peça | Onde | O que tem |
  |---|---|---|
  | Registro | **GoDaddy** (revendedora do Registro.br) | renovação automática; nameservers `gemma.ns.cloudflare.com` e `otto.ns.cloudflare.com`, sem IP. A lista de DNS do painel da GoDaddy não vale mais |
  | DNS | **Cloudflare** (plano Free, conta do R2) → DNS → Records | CNAME `@` → `n16bbu6d.up.railway.app`; CNAME `www` → `psxfoc41.up.railway.app`; TXT `_railway-verify` e `_railway-verify.www` (prova de posse para o Railway); TXT `_dmarc` (`p=quarantine`, veio da GoDaddy, relatórios para um endereço dela até o Resend) |
  | Site | **Railway** → serviço da API → *Settings* → *Networking* | domínios `prismamoney.com.br` e `www.prismamoney.com.br`, porta 8080; certificado HTTPS do Railway |

  - **Nuvem cinza (*DNS only*) nos dois CNAME, sempre.** A laranja (*Proxied*) poria a Cloudflare na frente: a API
    lê o IP do último salto (`ForwardedHeaders`, `Program.cs`), que passaria a ser o da Cloudflare, e o rate limit
    do login juntaria pessoas diferentes num IP só. Ligar a laranja exige antes ajustar a API (cabeçalho
    `CF-Connecting-IP` ou as redes da Cloudflare como proxies conhecidos). Por isso as opções da Cloudflare para robôs
    de IA, cache e SSL/TLS não agem no site.
  - **`www` não redireciona:** responde o mesmo site, com sessão separada (cookie por host). O `canonical` aponta
    para a raiz. Redirecionar seria na API (a regra de redirecionamento da Cloudflare só age com a nuvem laranja).
  - **Conferir:** `https://prismamoney.com.br/`, `/og.jpg`, `/robots.txt`, `/sitemap.xml` e `/api/health`
    respondem 200, e `http://` redireciona para `https://`.
  - **E-mail** (Email Routing do `contato@`, registros do Resend, DMARC): seção 8.
- **Imagem do link no WhatsApp:** os apps guardam a prévia por um tempo. Para conferir depois de mudar a `og.jpg`,
  colar o link numa conversa nova. O depurador de compartilhamento do Facebook mostra a prévia que os sites leem e
  renova o cache do Facebook; o WhatsApp renova o dele com o tempo.
- **Compressão:** o front sai comprimido pela própria API (Brotli ou gzip, conforme o navegador pede). Para conferir:
  `curl -sI -H "Accept-Encoding: br" https://prismamoney.com.br/` deve trazer `content-encoding: br`; um endereço
  sob `/api` não traz.

## 8. E-mail: receber, enviar e responder (H.4)

Por que cada peça: `docs/email.md`. Três serviços, todos no plano gratuito, nesta ordem. Nenhuma senha ou chave passa
pela conversa, por arquivo versionado ou pelo histórico do shell.

| Endereço | Recebe | Envia |
|---|---|---|
| `contato@prismamoney.com.br` | sim: Email Routing repassa ao Gmail do dono | o dono, pelo Gmail ("Enviar e-mail como", 8.3) |
| `nao-responda@prismamoney.com.br` | não (sem regra: volta para quem mandou) | o app, pelo Resend, com "responder para" o `contato@` |

Nenhum dos dois é uma caixa de e-mail: o Resend envia por qualquer endereço do domínio verificado, e o Email Routing
só repassa.

### 8.1 Receber o `contato@` (Cloudflare Email Routing)

1. Cloudflare → `prismamoney.com.br` → *Email* → *Email Routing* → *Get started*.
2. *Custom address* `contato` → *Action* "Send to an email" → o Gmail do dono. A Cloudflare manda um e-mail de
   verificação para o Gmail: clicar no link.
3. Aceitar os registros que ela propõe (*Add records and enable*): três MX na raiz (`route1/2/3.mx.cloudflare.net`) e um
   TXT de SPF na raiz. Só pode haver **um** TXT `v=spf1` na raiz: se já houver um (vindo da GoDaddy), juntar os dois.
4. *Catch-all* desligado: o `nao-responda@` e endereços inventados voltam para quem mandou.
5. **Conferir:** de outro e-mail (não o próprio Gmail de destino, que o Gmail esconde), mandar para o `contato@` e ver
   chegar. Logo depois de criar os registros, o servidor de quem envia pode ainda lembrar que o domínio não recebia
   e-mail: espere alguns minutos e mande de novo. O registro de atividade do Email Routing mostra cada mensagem.
6. **Filtro no Gmail:** as primeiras mensagens repassadas caem no spam (domínio novo, mensagem repassada). Busca
   `to:contato@prismamoney.com.br` → *Criar filtro* → **Nunca enviar para spam** + marcador **Prisma Money**. O marcador
   reúne as conversas dos pedidos de titulares (seção 9).

Feito em 2026-10-15. A tela nova da Cloudflare fica na conta (*Email Service* → *Email Routing*): primeiro
*Destination addresses* (o Gmail, verificado pelo link), depois *Onboard Domain* (cria os MX, o SPF e o DKIM), depois a
regra `contato`.

### 8.2 Enviar pelo app (Resend)

1. Conta no Resend com 2FA. *Domains* → *Add domain* → `prismamoney.com.br`, região **São Paulo** (`sa-east-1`).
2. Colar na Cloudflare (*DNS* → *Records* → *Add record*) os registros que o Resend mostrar. Em 2026-10-13 foram três,
   todos com TTL *Auto* e a **nuvem cinza** (*DNS only*):

   | Tipo | Nome | Conteúdo | Para quê |
   |---|---|---|---|
   | TXT | `resend._domainkey` | `p=MIGfMA0…` (a chave pública inteira, como o Resend mostra) | DKIM: assina os e-mails pelo domínio |
   | CNAME | `send` | `send.forge.rmta.net` | SPF e retorno dos e-mails devolvidos |
   | CNAME | `rsend` | `rsend-sae1.forge.rmta.net` | idem, na região de São Paulo (`sae1`) |

   No campo *Name* vai só o começo (`send`, não `send.prismamoney.com.br`): a Cloudflare completa. Os nomes não colidem
   com os do 8.1, que ficam na raiz. A chave do DKIM é pública (qualquer um a lê no DNS); não é segredo.
3. *Verify* e esperar o domínio ficar *Verified*.
4. **DMARC:** o `_dmarc` atual veio da GoDaddy (`p=quarantine`, relatórios para um endereço dela). Trocar o destino
   dos relatórios pelo da Cloudflare (*Email* → *DMARC Management*, que dá um endereço e o painel de leitura), mantendo
   `p=quarantine`. Os e-mails do Resend saem assinados pelo domínio e passam.
5. *API Keys* → criar `prisma-api`, permissão *Sending access*, só o domínio `prismamoney.com.br`. Colar direto no
   Railway, no serviço da API, como `Email__Resend__ApiKey`. A chave só aparece uma vez: se perder, apague e crie
   outra.
6. Remetente e responder-para já vão no `appsettings.json` (não são segredo); `Email:Transport` já é `Resend` fora
   de desenvolvimento.
   **A chave precisa estar no Railway antes do deploy da H.4a:** sem ela, a API não sobe (de propósito, para não
   aceitar cadastro cujo e-mail nunca sai). O deploy falha no health check e o Railway mantém a versão anterior no ar.
7. **Conferir** (com a H.4b e a H.4c no ar): "Esqueci a senha" no login com o próprio e-mail (o cadastro está fechado),
   e-mail recebido no Gmail com "enviado por prismamoney.com.br" e, em *Mostrar original*, `SPF: PASS`, `DKIM: PASS` e
   `DMARC: PASS`. Não precisa trocar a senha: o link vence em 1 hora.

### 8.3 Responder como `contato@` (Gmail)

1. No Resend, outra chave: `gmail-contato`, *Sending access*, só o domínio. Separada da do app: se vazar ou for
   apagada, uma não derruba a outra.
2. Gmail → *Configurações* → *Ver todas as configurações* → *Contas e importação* → *Enviar e-mail como* → *Adicionar
   outro endereço de e-mail*.
3. Nome `Prisma Money`, endereço `contato@prismamoney.com.br`, *Tratar como alias* marcado.
4. Servidor SMTP `smtp.resend.com`, porta `587`, usuário `resend`, senha: a chave do passo 1, colada direto no Gmail.
   Conexão TLS.
5. O Gmail manda um código para o `contato@`, que chega pelo 8.1: confirmar.
6. Em *Ao responder uma mensagem*, escolher "Responder do mesmo endereço para o qual a mensagem foi enviada".
7. **Conferir:** responder a um e-mail que chegou no `contato@` e ver, do outro lado, o remetente `contato@` sem o
   endereço pessoal.

As respostas pelo Gmail contam na cota do Resend junto com os e-mails do app (gratuito: 100 por dia, 3.000 por mês).

### 8.4 No dia a dia

- **Filtros no Railway** (o nome do evento, não o texto da mensagem):

  | Pergunta | Filtro |
  |---|---|
  | A tarefa subiu com a API? (uma linha a cada deploy ou reinício) | `@eventName:EmailOutboxWorkerStarted` |
  | Uma volta inteira falhou? (ex.: banco fora do ar) | `@eventName:EmailOutboxRunFailed` |
  | E-mails enviados (id da linha, tipo, usuário, tentativas) | `@eventName:EmailSent` |
  | Falha passageira, com o motivo e a próxima tentativa | `@eventName:EmailRetryScheduled` |
  | Desistiu (recusado, ou 24 h sem conseguir) | `@eventName:EmailFailed` |
  | Pedido barrado pelo limite por pessoa | `@eventName:EmailThrottled` |
  | Cadastro com e-mail que já tem conta (vai o aviso ao dono) | `@eventName:RegistrationOfExistingAccount` |
  | E-mails confirmados / links recusados (motivo: vencido, mexido, senha errada) | `@eventName:EmailConfirmed` / `@eventName:EmailConfirmationRejected` |
  | Login com a senha certa e o e-mail sem confirmar | `@eventName:LoginFailed` com `@reason:EmailNotConfirmed` |
  | Pedidos de senha nova / senhas trocadas / links recusados | `@eventName:PasswordResetRequested` / `@eventName:PasswordReset` / `@eventName:PasswordResetRejected` |

- **E-mail que não chegou:** o id da linha vem nos logs acima; no banco, `outbox_emails` pelo id (status, tentativas,
  último erro). No Resend, *Emails* mostra o que saiu e o que voltou.
- **Alguém não consegue entrar:** a própria pessoa resolve. Sem confirmar o e-mail, o login mostra "Reenviar e-mail";
  sem lembrar a senha, "Esqueci a senha" no login (link de 1 hora, que também confirma o e-mail e destrava a conta
  bloqueada). Ninguém troca senha à mão no banco.
- **Prévia dos e-mails** depois de mudar texto ou layout:
  `PRISMA_EMAIL_PREVIEW=<pasta> dotnet test tests/Prisma.Api.Tests --filter FullyQualifiedName~EmailPreview`.
- **Resend fora do ar, ou chave errada (401/403):** a fila tenta de novo por 24 h (30 s, 2 min, 10 min, 1 h, depois
  a cada 6 h). Conserte a chave nesse prazo e nada se perde. Depois disso, a pessoa usa "Reenviar".
- **Em desenvolvimento** os e-mails viram `.html` em `src/Prisma.Api/.dev-emails/` (`Email:Transport=Folder`). Para
  testar o envio real, User Secrets `Email:Transport=Resend` e `Email:Resend:ApiKey`.
- **Contrato com o Resend** (envia para `delivered@resend.dev`, gasta um da cota):
  `PRISMA_LIVE_TESTS=1 PRISMA_RESEND_API_KEY=<chave> dotnet test tests/Prisma.Api.Tests --filter Category=Live`. Antes
  do domínio verificado, `PRISMA_RESEND_FROM="onboarding@resend.dev"`.

## 9. Pedidos dos titulares (LGPD, art. 18 e 19)

A política de privacidade (seção 7) promete atender pelo `contato@prismamoney.com.br` e registrar cada pedido. Sem
encarregado formal (agente de pequeno porte, Res. CD/ANPD 2/2022, art. 11): quem atende é o dono.

**Prazos (art. 19):** confirmar que há dados e o acesso simplificado, de imediato; a declaração completa (a cópia), em
até **15 dias** do pedido. Corrigir e apagar: sem demora.

1. **Registrar** o pedido no mesmo dia: data, tipo (confirmação, acesso/cópia, correção, eliminação, informação sobre
   compartilhamento, outro), o id da conta e a data da resposta. O registro fica fora do git (`.lgpd/pedidos.md`, local)
   e sem o e-mail: o e-mail já está na conversa do Gmail (marcador "LGPD").
2. **Conferir quem pede**, na medida do pedido: o pedido precisa vir do e-mail da conta. Se vier de outro endereço,
   responda pedindo que escreva do e-mail cadastrado. Nunca mande dado para um endereço que não seja o da conta.
3. **Atender:**
   - **Confirmação ou informação** (o que guardamos, com quem compartilhamos): responda com o resumo e o link da
     política (`https://prismamoney.com.br/privacidade`).
   - **Cópia dos dados** (acesso e portabilidade, enquanto o "Baixar meus dados", A.3b, não existe):
     `SELECT id FROM users WHERE normalized_email = upper('<e-mail>');` e depois, como `prisma_backup` (só lê), com o
     TCP Proxy do Postgres ligado só durante isso:
     `psql "<conexão do prisma_backup>" -v user_id=<id> -At -f ops/privacy/export-user.sql > copia.json`.
     Mande o arquivo como anexo, do `contato@`, para o e-mail da conta. Apague o arquivo local depois de enviar.
   - **Correção:** quase tudo a pessoa corrige no app; o e-mail da conta ainda não se troca pelo app (fica para uma etapa
     de conta). Se pedir, troque à mão e registre.
   - **Eliminação:** "Apagar conta", no menu da conta, apaga tudo na hora (A.3). Se a pessoa não conseguir entrar, a
     senha nova pelo e-mail resolve; só apague por ela, à mão, em último caso, com o `AccountEraser` (não há comando
     pronto: peça a quem mexe no código).
4. **Responder** e anotar a data da resposta no registro. Mensagens do `contato@` ficam 1 ano depois da última da
   conversa (política, seção 3.5): apague as mais antigas uma vez por ano.

Tabela nova com `user_id` entra também em `ops/privacy/export-user.sql` (o apagar conta e a limpeza têm teste que acusa;
a consulta da cópia não).

## 10. Incidente de segurança (LGPD, art. 48; Res. CD/ANPD 15/2024)

Incidente: acesso não autorizado, vazamento, perda ou alteração de dado pessoal (ex.: senha do banco vazada, chave do
Resend exposta, backup aberto). A política promete comunicar a pessoa e a ANPD quando houver risco ou dano relevante.
Dado financeiro pesa: a Res. 15/2024 o lista entre os que tornam a comunicação provável.

**Prazos:** comunicar à ANPD e às pessoas afetadas em **3 dias úteis** a partir de quando se soube que o incidente
atingiu dado pessoal; complementar em até 20 dias úteis. Agente de pequeno porte tem prazos em dobro (Res. 2/2022):
confira no texto da resolução no dia, antes de contar com eles. **Todo incidente fica registrado por 5 anos**, mesmo o
que não precisou ser comunicado.

1. **Conter**, na hora:
   - Senha do banco: `\password prisma_app` (e `prisma_backup`), atualize a variável no Railway (seção 1).
   - Chave do Resend: apague no painel do Resend, crie outra e troque `Email__Resend__ApiKey` (seção 8.2).
   - Sessões: apagar as chaves do cookie (`DELETE FROM data_protection_keys;`) e reiniciar a API derruba todas as
     sessões; todo mundo entra de novo.
   - Token do R2 ou chave do backup: seção 5.5.
2. **Guardar a prova antes que suma:** os logs do Railway duram 7 dias. Exporte o período (filtros da seção 3, pelo
   `traceId`, `userId` e `@level:error`) e guarde fora do git, com o horário de cada passo.
3. **Avaliar:** que dados (conta, lançamentos, e-mails), de quantas pessoas (os `userId` dos logs), desde quando, se
   foram só lidos ou também alterados, e o risco para elas (financeiro, de exposição).
4. **Comunicar**, se houver risco ou dano relevante:
   - **ANPD:** pelo formulário de comunicação de incidente de segurança no site da ANPD (gov.br/anpd).
   - **Pessoas afetadas:** e-mail do `contato@`, em linguagem simples: o que aconteceu, que dados, quando, o que foi
     feito, o que a pessoa pode fazer (ex.: trocar a senha) e o contato.
   - O conteúdo (art. 48, §1º): natureza dos dados, pessoas afetadas, medidas de segurança, riscos, motivo do atraso
     (se houver) e o que foi e será feito.
5. **Registrar** (`.lgpd/incidentes.md`, fora do git): data em que se soube, o que aconteceu, a avaliação, se foi
   comunicado (e por quê não, se não foi), as medidas e as datas. Guardar 5 anos.
6. **Corrigir a causa** e anotar no `PLAN.md` o que muda para não acontecer de novo.
