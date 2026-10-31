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
| `ConfirmEmail` | cadastro; "reenviar" | Confirme seu e-mail no Prisma Money | `/confirmar-email#u=…&t=…`, vale **3 dias** |
| `AlreadyRegistered` | cadastro com e-mail que já tem conta | Alguém tentou criar uma conta com seu e-mail | `/entrar` e, na nota, `/esqueci-senha` |
| `ResetPassword` | "esqueci a senha" | Crie uma senha nova no Prisma Money | `/nova-senha#u=…&t=…`, vale **1 hora** |
| `PasswordChanged` | senha trocada ou redefinida | Sua senha do Prisma Money foi trocada | `/esqueci-senha`, para quem não reconhece a troca |
| `ConfirmEmailReminder` | disparo do admin, para quem não confirmou há N dias | Falta só confirmar seu e-mail no Prisma Money | `/confirmar-email#u=…&t=…`, vale **3 dias** |
| `MissYou` | disparo do admin, para quem está sem acesso há N dias (**bloqueado** até o descadastro) | Sentimos sua falta no Prisma Money | `/entrar` |

Os dois últimos não nascem de um pedido da pessoa: o dono os dispara pelo admin, com público, teto e travas próprios
(`docs/admin-email.md`, 3.3). Não passam pelo limite do `EmailQueue`, e o conteúdo e o token também saem na hora do envio.

- Prazos diferentes pedem dois provedores de token no Identity (o padrão usa um só, de 1 dia, para tudo):
  `ConfirmEmailTokenProvider` e `ResetPasswordTokenProvider`. Cada token vale só para a sua finalidade.
- Nota e parágrafos aceitam `[texto](https://…)` como link (o "Crie uma senha nova" dos avisos).
- O link abre uma **tela do app**, que chama a API por `POST`. Os antivírus de e-mail (o "links seguros" do Outlook,
  por exemplo) abrem os links antes da pessoa, sem rodar JavaScript: um `GET` na API gastaria o token.
- Texto curto, com o motivo do e-mail e o que fazer se não foi a pessoa. Os textos finais vão para revisão do dono
  na etapa em que cada e-mail entra. Textos em `Features/Auth/AuthEmails.cs`.

### Visual (`EmailLayout`, H.4b)

Pedido do dono: e-mail bonito, estruturado como um documento, com a identidade do app (`CLAUDE.md`, 7.1).

- **Cabeçalho em tinta** (`#171723`) com o logo e "Prisma Money" em serifada; logo abaixo, a **faixa do espectro**
  (as seis cores do `PrismLogo`) em células de tabela, que aparece mesmo com as imagens bloqueadas.
- **Corpo:** rótulo pequeno (o tipo do e-mail), título serifado, parágrafos, **botão em tinta**, o link por extenso
  para quem não consegue tocar no botão, um **quadro de dados** (conta, validade do link) e uma **nota** em caixa
  ("Não foi você?"). Rodapé discreto: "E-mail automático do Prisma Money.", sem convidar a responder (decisão do
  dono, 2026-10-14). O "responder para" `contato@` continua: quem responde mesmo assim chega ao dono, em vez de
  receber um erro de entrega do `nao-responda@`.
- **Feito para leitor de e-mail:** tabelas, estilos em linha, cores em hexadecimal (os tokens do app convertidos de
  oklch), largura máxima de 560 px. Serifada do sistema (Georgia): fonte da web não carrega no Gmail e seria terceiro.
- **Logo:** PNG 144×144 (3x) em `public/email/prisma-logo.png`, servido pelo próprio domínio, igual para todos (não
  identifica quem abriu). Sem ele, o nome e a faixa seguram a identidade.
- **Modo escuro** (Apple Mail, Outlook do celular): o `<style>` troca papel, cartão e texto, e o cabeçalho ganha um
  violeta escuro para continuar se destacando do cartão. Tela estreita: o quadro de dados empilha rótulo e valor.
- **Prévia:** `PRISMA_EMAIL_PREVIEW=<pasta> dotnet test tests/Prisma.Api.Tests --filter FullyQualifiedName~EmailPreview`
  grava cada e-mail como `.html` e `.txt`. Conferido em 320, 390 e 760 px, claro e escuro.

---

## 5. Os fluxos

### 5.1 Cadastro com confirmação (H.4b)

1. `/cadastro` ganha **repetir a senha** (conferido só na tela; a API recebe uma senha).
2. `POST /auth/register` cria a conta e o pedido `ConfirmEmail` e **não entra mais**: responde 202, e a tela vira
   "Confira seu e-mail", com o endereço, o botão "Reenviar" (espera de 60 s entre toques) e "Voltar para entrar".
3. **E-mail que já tem conta:** a mesma resposta 202 e a mesma tela, e o dono do e-mail recebe `AlreadyRegistered`.
   Hoje o cadastro responde "e-mail já em uso" (409), o que conta a qualquer um quem tem conta. Se a conta existente
   ainda não confirmou, vai `ConfirmEmail` de novo em vez de `AlreadyRegistered`.
4. `/confirmar-email` pede **a senha do cadastro** e chama `POST /auth/confirm-email`. Deu certo: entra e vai para o
   Resumo, com o aviso "E-mail confirmado". Link vencido ou usado: "Este link não vale mais", com campo de e-mail e
   "Enviar outro link". Senha errada: "A senha não confere com a do cadastro" (código `WrongPassword`); o link não é
   gasto, e a senha errada conta para o bloqueio, como no login.
   **Por que a senha** (decisão do dono, 2026-10-14; antes o link entrava direto): o link prova que a pessoa tem o
   e-mail, não que foi ela quem escolheu a senha. Sem a senha, alguém se cadastraria antes com o e-mail de outra
   pessoa e uma senha sua, a dona do e-mail confirmaria pelo link e o intruso entraria na conta dela depois
   (pré-sequestro). Com a senha, a dona do e-mail que não a sabe cria uma nova pelo e-mail (H.4c), e a do intruso
   deixa de valer. A ordem no servidor: o link primeiro (link inválido não testa senha nem conta tentativa), depois
   a senha, e só então confirma.
5. `POST /auth/login` com a senha **certa** e o e-mail não confirmado: 403 com o código `EmailNotConfirmed` (campo
   `code` do ProblemDetails, `Error.Code`), e a tela oferece "Reenviar e-mail". Com a senha errada, a mesma resposta
   de sempre: o estado da conta só aparece para quem sabe a senha. Por isso o login confere a senha à mão
   (`CheckPasswordAsync`, com o bloqueio por tentativas) antes do estado: o `PasswordSignInAsync` do Identity diria
   "não confirmado" sem olhar a senha.
6. `POST /auth/resend-confirmation` (e-mail) responde sempre 202, exista a conta ou não. O botão "Reenviar" espera
   60 s entre toques (o limite da fila) e responde "Pronto. Confira sua caixa de entrada.", e não "Enviamos outro
   e-mail": dentro do minuto a fila barra o pedido sem a tela saber, e o e-mail mais recente é que vale.
7. **Contas que já existem** (o dono, o pai, contas de teste): a migration as marca como confirmadas. Ninguém fica
   trancado do lado de fora no deploy.
8. `RequireConfirmedEmail = true` no Identity.

### 5.2 Esqueci a senha (H.4c)

1. "Esqueci a senha" no `/entrar` (ao lado do rótulo da senha, levando o e-mail já digitado) abre `/esqueci-senha`:
   campo de e-mail, `POST /auth/forgot-password` e a resposta sempre igual ("Se houver uma conta com esse e-mail,
   enviamos um link para criar uma senha nova. Ele vale por 1 hora."). Conta sem o e-mail confirmado também recebe.
   Limites da fila (1 por minuto, 5 por dia) e do `auth` por IP.
2. `/nova-senha#u=…&t=…` tem a senha nova e repetir a senha; `POST /auth/reset-password` troca a senha. As telas
   `/esqueci-senha` e `/nova-senha` abrem com ou sem sessão. A senha errada na confirmação de e-mail (5.1) leva a
   "Criar uma senha nova".
3. Ao trocar: o `SecurityStamp` muda (as outras sessões caem, seção 5.4), o contador de senhas erradas zera, o
   bloqueio por tentativas acaba, o e-mail fica **confirmado** (o link provou a posse) e vai `PasswordChanged`.
4. Depois de trocar, **entra direto**.
5. Link vencido, usado ou de uma conta que trocou a senha depois: "Este link não vale mais", com "Pedir outro link".
   A tela só sabe disso ao salvar (não há consulta prévia do link): quem abre um link usado preenche a senha e então
   vê o aviso.

### 5.3 Trocar a senha (H.4d)

1. No menu da conta, "Trocar senha" abre a tela `/trocar-senha` (como o "Apagar conta", e não um painel: os dois
   itens de conta do menu ficam iguais): senha atual, senha nova, repetir a senha nova.
2. `POST /auth/change-password`: senha atual errada é recusada (400 com o código `WrongPassword`, não 401, que
   levaria ao login) e conta para o bloqueio, como no login (senão a tela viraria um jeito de adivinhar a senha sem
   limite com uma sessão roubada). Conta bloqueada não troca, nem com a senha certa.
3. Esta sessão continua (o cookie é renovado com o selo novo, `RefreshSignInAsync`); as outras caem; vai
   `PasswordChanged`.

### 5.3b Contador de senhas erradas com prazo (H.4e)

O contador do Identity só zerava com a senha certa ou com o bloqueio: quem errasse uma vez por semana acabaria
bloqueado por uma soma de semanas. Agora a senha errada só soma com as anteriores se vier **até 15 minutos depois da
última** (`AppUserManager.AccessFailedAsync`, coluna `last_access_failed_at`, hora pelo `IClock`); mais tarde, a
contagem recomeça. Fica no `UserManager` para valer em todo lugar que pede a senha (entrar, confirmar o e-mail,
trocar a senha, apagar a conta). A janela conta da última senha errada, não da primeira: 5 erros com 14 minutos entre
cada um ainda bloqueiam. A migration zerou as contagens antigas, que não tinham data. O fim do bloqueio (15 minutos)
continua com o Identity, que lê o relógio do sistema.

### 5.4 Sessões abertas em outros aparelhos

O cookie confere o `SecurityStamp` de tempos em tempos (`SecurityStampValidator`, padrão de 30 min). Fica em
**5 min**: uma consulta a mais por sessão a cada 5 min, e a senha trocada derruba o aparelho perdido em minutos, não
em meia hora.

---

## 5.5 Segurança do link de confirmação

- **Não se adivinha:** o token do Identity é um pacote cifrado e assinado com as chaves do Data Protection (as mesmas
  do cookie de sessão, no Postgres), com o id da pessoa, a finalidade, a hora e o `SecurityStamp`. Um caractere
  trocado invalida a assinatura; sem a chave do servidor, não se fabrica um.
- **Vale para uma pessoa e uma finalidade,** por 3 dias e uma vez (confirmar troca o `SecurityStamp`, o que mata este e
  os outros links já enviados).
- **Uma resposta só** para link errado, vencido, usado ou de conta que não existe: chutar ids não revela quem tem conta.
  Rate limit do `auth` (5 por minuto por IP).
- **O token não passa por servidor ao abrir o link:** vai depois do `#` (o navegador não manda o fragmento), a tela o
  lê e o manda no corpo do POST, e o tira da barra de endereço (não fica no histórico). A tela não carrega nada de
  terceiros.
- **Quem tem as chaves do Data Protection** (acesso ao banco ou a um backup aberto) fabrica tanto o link quanto o
  cookie de sessão: o mesmo risco que já existia, coberto pelo acesso restrito ao banco e pelo backup criptografado.

## 6. Fora destas etapas

- **Trocar o e-mail da conta** (pede confirmar o novo endereço): quando fizer falta.
- **Aviso de login de aparelho novo, 2FA:** depois do cadastro público, se fizer sentido.
- **E-mail em massa** (confirmação de quem travou, "sentimos sua falta"): o mecanismo de disparos existe desde a AE.4 (`docs/admin-email.md`, 3.3), com a trava de que e-mail de relacionamento só sai com o descadastro, que ainda não existe (AE.6, em espera por decisão do dono).
- **E-mail de boas-vindas, resumo do mês, lembrete de fatura:** produto, não conta; cada um entra com decisão própria.
  A fila e o transporte já servem a eles.
- **Webhook do Resend** (devolvido, marcado como spam): só com volume que justifique.
