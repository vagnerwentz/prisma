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
