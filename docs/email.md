# E-mail: envio, confirmação de conta e senha (etapas H.4)

> Especificação escrita em 2026-10-13, com as decisões do dono. Antes de abrir o cadastro público (`PLAN.md`,
> pendências): sem confirmação de e-mail, qualquer pessoa cria conta com o e-mail de outra.
> Configuração dos serviços (Cloudflare, Resend, Gmail): `docs/operacao.md`, seção 8.

---

## 1. Decisões do dono

| Tema | Decisão |
|---|---|
| Fornecedor | **Resend** (aprovado em 2026-10-13), chamado por HTTP com o `HttpClient` tipado, sem pacote, como a brapi |
| Troca de fornecedor | O código só conhece `IEmailTransport`. Outro fornecedor é outra implementação e uma configuração; textos e fila não mudam |
| Resiliência | **Fila no Postgres (outbox) + tarefa em segundo plano**, não microsserviço com RabbitMQ ou Kafka. Resend fora do ar não derruba nem atrasa o cadastro: o e-mail espera na fila (seção 3) |
| Sem confirmar | **Não entra.** Login com a senha certa e o e-mail não confirmado é recusado, com o "reenviar" à mão |
| Remetente | `Prisma Money <nao-responda@prismamoney.com.br>`, com **responder para** `contato@prismamoney.com.br`: quem responde chega ao dono |
| Responder como `contato@` | Gmail do dono com "Enviar e-mail como", pelo SMTP do Resend (`docs/operacao.md`, 8.3). Sem e-mail corporativo pago |

### Por que não microsserviço com fila (RabbitMQ, Kafka)

- **Não resolve o problema principal.** A API grava a conta no banco e publica na fila: se cair entre os dois, o
  e-mail se perde (o problema da *dupla escrita*). A solução conhecida para isso é o outbox no próprio banco; com ele,
  o RabbitMQ seria uma peça a mais em cima, não no lugar.
- **Custo e operação.** Mais dois serviços no Railway (o broker e o consumidor), cada um com memória, deploy, alerta
  e atualização. O volume é de alguns e-mails por dia.
- **A porta fica aberta.** Quem lê a fila é uma classe só (`EmailOutboxWorker`). Se um dia fizer sentido, ela sai da
  API para um serviço próprio lendo a mesma tabela, ou passa a publicar num broker. Quem pede o e-mail não muda.

---

## 2. Peças

```
Handler (cadastro, esqueci a senha…)
   │  grava EmailOutbox junto com o resto, na mesma transação
   ▼
outbox_emails (Postgres) ──► EmailOutboxWorker (BackgroundService)
                                │  monta o e-mail (EmailTemplates) e o link com o token
                                ▼
                             IEmailTransport
                                ├─ ResendEmailTransport   (produção)
                                ├─ FolderEmailTransport   (desenvolvimento: .html numa pasta local)
                                └─ FakeEmailTransport     (testes: guarda em memória)
```

- **`IEmailTransport.Send(EmailMessage, ct)`** devolve `Sent`, `Retry` (falha passageira: rede, tempo esgotado, 429,
  5xx) ou `Reject` (falha que repetir não conserta: endereço inválido, 4xx de validação, chave errada).
- **`EmailMessage`**: remetente, responder-para, destinatário, assunto, HTML e texto puro.
- **`IEmailComposer`**: um por tipo de e-mail, monta o conteúdo na hora do envio (com o link e o token novos). Nulo
  quando o motivo deixou de existir (quem pediu a confirmação já confirmou): a linha fica `Skipped` e nada sai. Entra
  com o fluxo que pede o tipo (H.4b em diante); tipo sem composer vai para `Failed` (`no_composer`).
- **`EmailTemplates`**: um método por tipo de e-mail, em pt-BR, HTML simples (tabela, estilos em linha, sem imagem
  externa nem rastreamento) e a versão em texto puro. Sem biblioteca de templates.
- **Configuração** (`Email`):

  | Chave | Produção | Desenvolvimento | Testes |
  |---|---|---|---|
  | `Email:Transport` | `Resend` | `Folder` | `Fake` |
  | `Email:From` | `Prisma Money <nao-responda@prismamoney.com.br>` | igual | igual |
  | `Email:ReplyTo` | `contato@prismamoney.com.br` | igual | igual |
  | `Email:Resend:ApiKey` | variável de ambiente no Railway (`Email__Resend__ApiKey`) | User Secrets, se quiser testar o envio real | — |
  | `Email:Folder` | — | `.dev-emails/` (fora do git) | — |
  | `Email:Outbox:Enabled` | ligado | ligado | desligado (cada teste chama o processador) |
  | `Email:Outbox:PollSeconds` / `BatchSize` | 5 / 20 | igual | lote de 500 |
  | `App:PublicUrl` (H.4b) | `https://prismamoney.com.br` | `https://localhost:5173` | o do teste |

  Fora de desenvolvimento, `Email:Transport` diferente de `Resend` ou chave ausente impede a API de subir: melhor
  falhar no deploy do que aceitar cadastros cujo e-mail nunca sai.

---

## 3. A fila (`outbox_emails`)

### Regras

1. **O pedido de e-mail nasce na mesma gravação que o motivo dele.** Cadastro: o usuário, as categorias e o pedido
   de confirmação entram juntos, ou nada entra.
2. **A fila não guarda o link nem o token.** Guarda o tipo (`EmailKind`), o usuário e o destinatário; o token é gerado
   na hora do envio (os tokens do Identity não são gravados: valem pela assinatura e pelo `SecurityStamp`). Banco
   vazado ou backup aberto não dá link para entrar na conta de ninguém, e um reenvio sai com token novo.
3. **Tentativas com espera crescente:** 30 s, 2 min, 10 min, 1 h, depois a cada 6 h, até 24 h desde o pedido. Passou
   disso, ou o transporte disse `Reject`: a linha fica `Failed`, com o motivo (sem o endereço), e um log `Error`.
4. **Sai em segundos quando dá certo.** A tarefa consulta a fila a cada 5 s (consulta pequena, com índice parcial nos
   pendentes); lote cheio, consulta de novo na hora. Um aviso em memória do handler para a tarefa foi descartado: o
   aviso chegaria antes do fim da transação de quem pede, e a tarefa não veria a linha.
5. **Duas execuções não mandam o mesmo e-mail.** A reserva empurra o `NextAttemptAt` para daqui a 2 min numa só
   instrução, com `FOR UPDATE SKIP LOCKED`: outra execução não pega a linha, e nenhuma transação fica aberta durante o
   envio. Se a API cair no meio, a reserva vence e o e-mail volta para a fila.
   **Sem `Idempotency-Key` no Resend:** o conteúdo muda a cada tentativa (token novo), e o Resend recusa a mesma chave
   com outro conteúdo. Se a resposta se perder depois do envio (tempo esgotado, a API cair), a pessoa recebe dois
   e-mails, com links que valem os dois. Raro e inofensivo; perder o e-mail não seria.
6. **Limites:** no máximo 1 pedido do mesmo tipo para o mesmo usuário por minuto e 5 por dia (confere a própria
   fila), além do rate limit por IP dos endpoints. Excedido, o endpoint responde como se tivesse enfileirado: não
   revela nada e não gasta a cota do Resend (gratuito: 100 por dia, 3.000 por mês).
7. **Retenção:** linha enviada ou falha é apagada depois de 30 dias (a própria tarefa, uma vez por dia). O endereço é
   dado pessoal; a fila não é histórico.
8. **Tabela sem dono**, em `AppDbContext.GlobalTables`, com o motivo: só a API escreve e só a tarefa lê; nenhum
   endpoint a lista. Fica fora do filtro de dono porque a tarefa atende todo mundo, e o `UserId` é só referência.

### Campos

`Id`, `Kind`, `UserId`, `ToAddress`, `Status` (`Pending`, `Sent`, `Skipped`, `Failed`), `Attempts`, `NextAttemptAt`,
`LastError` (código, nunca o endereço nem o corpo), `ProviderMessageId`, `CreatedAt`, `SentAt`.

### Logs (`AppLog`)

`EmailSent` (id da linha, tipo, usuário, tentativas), `EmailRetryScheduled` (motivo e próxima tentativa, `Warning`),
`EmailFailed` (motivo, `Error`), `EmailSkipped`, `EmailThrottled` (limite da regra 6, `Warning`), `EmailProcessingFailed`
(exceção ao montar, `Error`), `EmailOutboxRunFailed` e `EmailOutboxCleaned`. O pedido em si não tem log: ele só existe
se a gravação de quem pede der certo, e o log viria antes dela. **Nunca** o endereço, o assunto, o link ou o token (regra de
segurança do `CLAUDE.md`, seção 8; o teste de arquitetura já recusa parâmetro com nome sensível).

---

## 4. Os e-mails

| Tipo (`EmailKind`) | Quando | Assunto | Link |
|---|---|---|---|
| `ConfirmEmail` | cadastro; "reenviar" | Confirme seu e-mail no Prisma Money | `/confirmar-email?u=…&t=…`, vale **3 dias** |
| `AlreadyRegistered` | cadastro com e-mail que já tem conta | Alguém tentou criar uma conta com seu e-mail | `/entrar` e `/esqueci-senha` |
| `ResetPassword` | "esqueci a senha" | Crie uma senha nova no Prisma Money | `/nova-senha?u=…&t=…`, vale **1 hora** |
| `PasswordChanged` | senha trocada ou redefinida | Sua senha do Prisma Money foi trocada | `/esqueci-senha`, para quem não reconhece a troca |

- Prazos diferentes pedem dois provedores de token no Identity (o padrão usa um só, de 1 dia, para tudo).
- O link abre uma **tela do app**, que chama a API por `POST`. Os antivírus de e-mail (o "links seguros" do Outlook,
  por exemplo) abrem os links antes da pessoa, sem rodar JavaScript: um `GET` na API gastaria o token.
- Texto curto, com o motivo do e-mail e o que fazer se não foi a pessoa. Os textos finais vão para revisão do dono
  na etapa em que cada e-mail entra.

---

## 5. Os fluxos

### 5.1 Cadastro com confirmação (H.4b)

1. `/cadastro` ganha **repetir a senha** (conferido só na tela; a API recebe uma senha).
2. `POST /auth/register` cria a conta e o pedido `ConfirmEmail` e **não entra mais**: responde 202, e a tela vira
   "Confira seu e-mail", com o endereço, o botão "Reenviar" (espera de 60 s entre toques) e "Voltar para entrar".
3. **E-mail que já tem conta:** a mesma resposta 202 e a mesma tela, e o dono do e-mail recebe `AlreadyRegistered`.
   Hoje o cadastro responde "e-mail já em uso" (409), o que conta a qualquer um quem tem conta. Se a conta existente
   ainda não confirmou, vai `ConfirmEmail` de novo em vez de `AlreadyRegistered`.
4. `/confirmar-email` chama `POST /auth/confirm-email`. Deu certo: **entra direto** (o link prova a posse do e-mail)
   e vai para o Resumo, com o aviso "E-mail confirmado". Link vencido ou usado: "Este link não vale mais", com campo
   de e-mail e "Enviar outro".
5. `POST /auth/login` com a senha **certa** e o e-mail não confirmado: 403 com o código `EmailNotConfirmed`, e a tela
   oferece "Reenviar o e-mail de confirmação". Com a senha errada, a mesma resposta de sempre: o estado da conta só
   aparece para quem sabe a senha.
6. `POST /auth/resend-confirmation` (e-mail) responde sempre 202, exista a conta ou não.
7. **Contas que já existem** (o dono, o pai, contas de teste): a migration as marca como confirmadas. Ninguém fica
   trancado do lado de fora no deploy.
8. `RequireConfirmedEmail = true` no Identity.

### 5.2 Esqueci a senha (H.4c)

1. "Esqueci a senha" no `/entrar` leva a `/esqueci-senha`: campo de e-mail e a resposta sempre igual ("Se houver uma
   conta com esse e-mail, enviamos um link para criar uma senha nova. Ele vale por 1 hora.").
2. `/nova-senha` tem a senha nova e repetir a senha; `POST /auth/reset-password` troca a senha.
3. Ao trocar: o `SecurityStamp` muda (as outras sessões caem, seção 5.4), o contador de senhas erradas zera, o
   bloqueio por tentativas acaba, o e-mail fica **confirmado** (o link provou a posse) e vai `PasswordChanged`.
4. Depois de trocar, **entra direto**.
5. Link vencido, usado ou de uma conta que trocou a senha depois: "Este link não vale mais", com "Pedir outro".

### 5.3 Trocar a senha (H.4d)

1. No menu da conta, "Trocar senha" abre um painel: senha atual, senha nova, repetir a senha nova.
2. `POST /auth/change-password`: senha atual errada é recusada e conta para o bloqueio, como no login (senão a tela
   viraria um jeito de adivinhar a senha sem limite com uma sessão roubada).
3. Esta sessão continua (o cookie é renovado); as outras caem; vai `PasswordChanged`.

### 5.4 Sessões abertas em outros aparelhos

O cookie confere o `SecurityStamp` de tempos em tempos (`SecurityStampValidator`, padrão de 30 min). Fica em
**5 min**: uma consulta a mais por sessão a cada 5 min, e a senha trocada derruba o aparelho perdido em minutos, não
em meia hora.

---

## 6. Fora destas etapas

- **Trocar o e-mail da conta** (pede confirmar o novo endereço): quando fizer falta.
- **Aviso de login de aparelho novo, 2FA:** depois do cadastro público, se fizer sentido.
- **E-mail de boas-vindas, resumo do mês, lembrete de fatura:** produto, não conta; cada um entra com decisão própria.
  A fila e o transporte já servem a eles.
- **Webhook do Resend** (devolvido, marcado como spam): só com volume que justifique.
