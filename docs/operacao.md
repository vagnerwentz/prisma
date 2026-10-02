# Operação da produção (Railway)

Roteiros para a produção de uso próprio (etapas H.2a a H.2c do `PLAN.md`). Nenhum segredo neste
arquivo: senhas ficam no gerenciador de senhas e nas variáveis do Railway.

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
- **2FA** no Railway, no GitHub e no e-mail dessas contas.
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

## 5. Backup fora do Railway (H.2b)

Todo dia às 03:00 de São Paulo, o serviço `backup` copia o banco (`pg_dump`), tranca a cópia com a sua chave
pública do `age` e a guarda no Cloudflare R2. Só a chave privada, que fica só com você, abre a cópia. O R2 guarda
90 dias e não deixa apagar nada com menos de 30 (trava do bucket), nem com o token. O Healthchecks.io manda
e-mail se um dia o backup falhar ou não rodar.

| Peça | Onde | Quem guarda o segredo |
|---|---|---|
| Imagem e scripts | `ops/backup/` (`backup.sh`, `restore.sh`, `Dockerfile`) | — |
| Usuário que só lê | `ops/postgres/backup-user.sql` (`prisma_backup`) | senha nas variáveis do serviço `backup` |
| Chave do `age` | pública no Railway; privada no gerenciador de senhas e numa cópia offline | você |
| Cópias | bucket do R2 | token restrito ao bucket, no Railway |
| Alerta | Healthchecks.io | endereço de aviso, no Railway |

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
   Copie o conteúdo inteiro do arquivo para o gerenciador de senhas (nota segura) e faça uma cópia offline
   (impressa ou num pendrive guardado). Depois apague o arquivo do Mac: `rm ~/prisma-backup-key.txt`. A chave
   pública (`age1...`) não é segredo.
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
6. **Serviço `backup` no Railway:** *New* → *GitHub Repo* → este repositório, com o nome `backup`.
   - *Settings*: *Watch Paths* `ops/backup/**`; *Cron Schedule* `0 6 * * *` (UTC, igual a 03:00 em São
     Paulo); *Restart Policy* `Never` (quem avisa a falha é o Healthchecks); "Wait for CI" ligado.
   - *Variables* (as de `${{ }}` são referências ao serviço Postgres):
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
     RCLONE_CONFIG_R2_ENDPOINT=<endpoint do passo 3>
     RCLONE_CONFIG_R2_ACCESS_KEY_ID=<do passo 3>
     RCLONE_CONFIG_R2_SECRET_ACCESS_KEY=<do passo 3>
     RCLONE_CONFIG_R2_NO_CHECK_BUCKET=true
     HEALTHCHECK_URL=<endereço do passo 4>
     ```
7. **Primeira execução.** Para não esperar a madrugada, troque o cron por alguns minutos à frente (em UTC) e
   volte para `0 6 * * *` depois. Confira: o log do serviço termina em `backup: ok file=... bytes=...`, o
   arquivo aparece no bucket e o check fica verde no Healthchecks.

### 5.2 Restauração de prova

É o "Pronto quando" da H.2b. Refaça de tempos em tempos (a cada poucos meses, ou depois de mudar o backup):
backup nunca restaurado não é backup.

1. Baixe o arquivo mais recente do bucket (painel do R2) para `~/Downloads/prisma-backup/`.
2. Recrie a chave privada num arquivo, a partir do gerenciador de senhas: `~/prisma-restore-key.txt`.
3. Restaure num **Postgres 18 provisório** (o da produção), separado do banco do dia a dia: o Postgres do
   `docker compose` é o 17 e não lê com segurança uma cópia do 18. Na raiz do repositório:
   ```bash
   docker build -f ops/backup/Dockerfile -t prisma-backup .
   PGPASSWORD="$(openssl rand -hex 16)" && export PGPASSWORD     # senha descartável, só deste terminal
   docker run -d --name prisma-restore-pg -e POSTGRES_PASSWORD="$PGPASSWORD" -p 5433:5432 postgres:18
   docker run --rm \
     -v ~/Downloads/prisma-backup:/in:ro -v ~/prisma-restore-key.txt:/key.txt:ro \
     -e AGE_IDENTITY=/key.txt -e PGHOST=host.docker.internal -e PGPORT=5433 -e PGUSER=postgres -e PGPASSWORD \
     prisma-backup /opt/backup/restore.sh /in/<arquivo>.dump.age
   ```
   Se o restore disser que não conectou, espere alguns segundos e rode de novo: o Postgres ainda está subindo.
   Restaura no banco `prisma_restore`, recriado a cada vez.
4. **Contagens:** as linhas `restore: count` devem ser iguais às `backup: count` do log da mesma execução no
   Railway. Uma diferença pequena só é normal se alguém lançou algo às 03:00, entre a cópia e a contagem.
5. **Resumo de um mês:** suba a API local apontando para a cópia e entre com a sua conta:
   ```bash
   export ConnectionStrings__Default="Host=localhost;Port=5433;Database=prisma_restore;Username=postgres;Password=$PGPASSWORD"
   dotnet run --project src/Prisma.Api
   ```
   Com o `npm run dev`, abra o Resumo de um mês e compare com o do celular.
6. **Limpeza:** a cópia tem os dados de todos que usam o app. Ao terminar:
   `docker rm -f -v prisma-restore-pg` (apaga o Postgres provisório e os dados dele), apague o arquivo baixado
   e `~/prisma-restore-key.txt`, e feche o terminal (o `PGPASSWORD` some com ele).

### 5.3 Emergência: restaurar a produção

Ainda não ensaiado. Se o banco de produção se perder:

1. Crie um Postgres novo no projeto do Railway e ligue o TCP Proxy dele.
2. Rode o passo 3 da seção 5.2 contra ele: `PGHOST`, `PGPORT`, `PGUSER=postgres` e a senha do Postgres novo,
   e `-e RESTORE_DB=<banco do Postgres novo>`. O script só restaura num banco sem tabelas.
3. Rode `ops/postgres/app-user.sql` e `ops/postgres/backup-user.sql` nele (a restauração deixa tudo com o
   `postgres`) e defina as senhas.
4. Aponte a API (seção 1, passo 3) e o serviço `backup` (seção 5.1, passo 6) para o Postgres novo. Desligue o
   TCP Proxy. As sessões continuam valendo: as chaves do cookie vêm na cópia.

### 5.4 No dia a dia

Nada a fazer enquanto o Healthchecks não mandar e-mail. Se mandar, o log do serviço `backup` no Railway diz o
motivo (`backup: failed status=...` e a linha anterior). O backup não usa o `AppLog`: é um script, e as linhas
são texto simples, com só nomes de tabela, contagens, nome e tamanho do arquivo.
