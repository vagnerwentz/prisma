# Admin: pessoas inativas e e-mail em massa (AE)

Conversa do dono com o assistente em 2026-10-29. **Estão feitas a AE.1 (último acesso), a AE.2 (contagem, API), a AE.3 (o admin como
entrada e a tela de Pessoas), a AE.4 (o mecanismo de disparos, no backend) e a AE.5 (a tela dos disparos)**; o descadastro (AE.6) e a
liberação do "sentimos sua falta" (AE.7) estão **em espera por decisão do dono (2026-10-31)**: dependem da base legal do e-mail de
relacionamento, e nada disso se implementa sem ele pedir. O lembrete de confirmação já pode ser usado. O que já existe e se reaproveita: a política `Admin`
(`docs/admin-mercado.md`), a fila `outbox_emails` com o Resend (`docs/email.md`) e o `EmailLayout` com a prévia
(`EmailPreviewTests`).

## 1. Ideia

O dono quer, pelo admin, achar quem não entra há algum tempo e mandar um e-mail ("faz tempo que você não aparece"), vendo
antes como o e-mail fica. Os dias de inatividade são escolhidos por ele a cada uso, não fixos.

## 2. Último acesso (AE.1, feita)

**Por que não só no login.** O cookie é persistente: quem usa o app todo dia por semanas nunca passa pelo login. Um
carimbo no login mostraria essa pessoa como "sumida" e ela receberia o e-mail de quem faz tempo que não entra.

**Como funciona.** `AppUser.LastSeenAt` (`users.last_seen_at`, UTC, vazio até o primeiro pedido). O `LastSeenStamper`
(`Infrastructure/Auth/LastSeen.cs`), ligado por `UseLastSeen` logo depois da autenticação, carimba no **primeiro pedido
autenticado de cada dia civil em São Paulo**:

```sql
UPDATE users SET last_seen_at = <agora>
WHERE id = <pessoa> AND (last_seen_at IS NULL OR last_seen_at < <início do dia em São Paulo, em UTC>)
```

- **Precisão de um dia, de propósito.** A pergunta é "há quantos dias não entra". O dia é o de São Paulo
  (`IClock.Today`, `SaoPauloTime.StartOf`), nunca o de UTC: um pedido às 23h30 de lá não cai no dia seguinte.
- **Um UPDATE só, sem carregar a entidade.** Não mexe em `UpdatedAt` nem no token de concorrência; dois pedidos ao mesmo
  tempo não brigam (o segundo não encontra linha a mudar). Conta apagada em outro aparelho: zero linhas.
- **Memória do dia (`ILastSeenCache`).** Evita o UPDATE em todo pedido. Hoje é a memória do processo
  (`InMemoryLastSeenCache`): é só otimização, a fonte da verdade é o UPDATE condicional, então reinício, deploy ou uma
  segunda instância custam um UPDATE a mais por pessoa e por dia, nunca um valor errado. **Redis não entrou** (decisão do
  dono, 2026-10-29): serviço a mais para vigiar e ponto de falha em todo pedido, para poupar um UPDATE barato. A interface é
  assíncrona (`ValueTask`) para que, se um dia houver Redis por outro motivo (rate limit distribuído, por exemplo), trocar
  seja registrar outra classe.
- **Falhar não derruba o pedido.** Exceção ao carimbar vira o aviso `LastSeenStampFailed` (2015) e o pedido segue. A
  memória só guarda o dia depois de gravar, então a falha tenta de novo no pedido seguinte.
- **O que conta como acesso.** Qualquer pedido à API com sessão válida, inclusive a consulta do sino que volta ao
  servidor quando a aba ganha o foco: aba aberta é uso. O login em si não carimba (a sessão só existe a partir do pedido
  seguinte, que é imediato). Pedido sem sessão ou com sessão inválida (selo de segurança trocado) não carimba.
- **Contas de antes da coluna.** A migration carimba com o instante dela quem já confirmou o e-mail ("visto no máximo
  até aqui"), para ninguém parecer sumido antes de o primeiro pedido carimbar de verdade. Quem nunca confirmou fica vazio:
  vazio quer dizer "nunca usou o app". Por isso, **uma campanha de inativos só é confiável a partir de N dias depois do
  deploy** (N = os dias escolhidos).
- **Privacidade.** Dado pessoal novo (atividade). Vai na cópia dos dados (`ultimoAcessoEm`, `ops/privacy/export-user.sql`),
  sai junto com a conta (é coluna de `users`) e não entra em log. **Decisão do dono (2026-10-30):** não citar na
  política de privacidade; a versão fica como está (citar subiria a versão e pediria novo aceite de todos). Volta a ser
  assunto na revisão jurídica (A.2) e, de qualquer forma, antes de usar o dado em e-mail de relacionamento (seção 4).

Consultas prontas: `docs/consultas-saude.md`, seção 2.

## 2.1 Logs (padrão do projeto, `CLAUDE.md` seção 6)

Todo evento é declarado em `Infrastructure/Logging/AppLog.cs` com `[LoggerMessage]`, mensagem em inglês, só ids e tipos
(o teste de arquitetura recusa parâmetro com nome sensível). Faixa 2xxx (acesso).

| Evento | Nível | Quando | Campos |
|---|---|---|---|
| `LastSeenStampFailed` (2015) | Warning | o carimbo do último acesso falhou (banco lento, coluna ausente); o pedido segue | `exception`; `userId` pelo escopo |
| `UserSummaryRequested` (2016) | Information | um admin consultou `GET /admin/users/summary` com `days` válido | `userId` de quem pediu, `days` |
| `EmailCampaignSent` (5009) | Information | um disparo foi para a fila (3.3) | `campaignId`, `kind`, `userId` de quem pediu, `recipients`, `days` |
| `EmailCampaignRefused` (5010) | Warning | uma trava recusou o disparo | `kind`, `userId`, `code` (`UnsubscribeRequired`, `NoRecipients`, `TooManyRecipients`, `AudienceChanged`) |
| `EmailTestSent` (5011) / `EmailTestFailed` (5012) | Information / Warning | o "enviar teste para mim" saiu / o fornecedor não aceitou | `kind`, `userId` (e `reason`, um código curto, na falha) |

- **Por que o carimbo que dá certo não loga:** seria uma linha por pessoa por dia (o nível Information é "o que se quer contar", e
  isso já está no banco, em `last_seen_at`). Só a falha, que é o que alguém precisa investigar, vira linha.
- **Por que a contagem loga quem pediu, e não o resultado:** é o mesmo rastro do disparo do admin de mercado
  (`MarketDataSyncRequested`): quem usou uma superfície de admin fica só no log. Os números devolvidos não vão junto (a resposta
  já os tem), e um pedido recusado (`days` fora de 1 a 365, ou quem não é admin) não gera o evento: a linha da requisição
  (`RequestFinished`, com o 400 ou o 403) já o registra.
- Testes: `LastSeenTests` (a falha vira o aviso, com o `userId`, e o pedido segue) e `AdminUsersApiTests` (o evento leva o
  `userId` e o `days`, não leva os números, e não sai em pedido recusado). Provados com mutações (sem o log, id errado, log antes
  de validar).

## 3. Desenho para decidir (sem etapa ainda)

Ordem proposta, cada passo útil sozinho:

1. **Contagens no admin (AE.2 a API e AE.3 a tela, feitas).** Só números, nunca a lista de pessoas. Logs do Railway não servem
   para isso (expiram e são eventos, não estado); o banco é a fonte. Ver a seção 3.1.
2. **Descadastro.** Preferência por pessoa gravada **no Prisma** (não nos *Audiences* do Resend, que copiariam a lista de
   e-mails para fora e deixariam a preferência longe do apagar-conta e da cópia dos dados). Link com token assinado, sem
   exigir login, e os cabeçalhos `List-Unsubscribe` e `List-Unsubscribe-Post` (um clique) no envio; confirmar na hora de
   implementar que a API do Resend aceita cabeçalhos próprios. E-mail de conta (senha, confirmação) nunca é bloqueado
   pela preferência. Obrigatório se a base legal for legítimo interesse (direito de oposição, LGPD art. 18, § 2º).
3. **Segmentos com contagem.** "Sem acesso há N dias" (N escolhido pelo dono), "cadastrou e nunca usou", "não confirmou
   o e-mail". A tela mostra **quantas pessoas**, não quem.
4. **Campanha com prévia.** Modelos fixos com poucos campos editáveis (assunto e um parágrafo), no `EmailLayout`; prévia
   em claro e escuro, estreito e largo; "enviar teste para mim".
5. **Disparo real**, manual, com confirmação ("enviar para 12 pessoas?"), teto de destinatários, limite de taxa, ninguém
   recebe a mesma campanha duas vezes, prioridade abaixo dos e-mails de conta na fila (cota do Resend) e histórico das
   campanhas (como o `market_sync_runs`). Log só com ids.
6. **Depois, se houver volume:** webhook do Resend (devolvido, marcado como spam) para parar de enviar a quem reclamou.

### 3.1 A contagem (AE.2)

`GET /admin/users/summary?days=N`, na política `Admin` (a regra do teste que recusa rota `/admin` sem ela vale).

- **N é do dono**, de 1 a 365 (padrão 30). Primeiro pensei em janelas fixas de 7, 30 e 90 dias; a decisão foi um número só,
  porque responde exatamente ao que se quer ver e deixa a API menor. Os atalhos de 7, 30 e 90 existirão só na tela, preenchendo
  o campo (AE.3).
- **Resposta:** `days`, `accounts`, `confirmed`, `unconfirmed`, `active`, `inactive`, `neverUsed`. Só números.
- **Regra (`ActivityWindow`, no domínio):** dias civis de São Paulo entre o último acesso e hoje. **Ativa** = vista há menos de
  N dias; **inativa** = há N dias ou mais; **nunca usou** = confirmou o e-mail e não tem acesso gravado. Não há buraco entre as
  duas metades, então vale `active + inactive + neverUsed = confirmed`, para qualquer N (a invariante é teste). Só entram
  contas com o e-mail confirmado: quem não confirmou não pôde usar o app.
- **No SQL**, o corte é um instante só (`ActivityWindow.ActiveSince`: o começo, em São Paulo, do dia de N−1 dias atrás),
  comparado com `last_seen_at`; o banco não converte o fuso de cada linha. Uma propriedade em CsCheck garante que isso dá o
  mesmo resultado que contar os dias civis.
- **O mesmo N serve ao segmento do e-mail de quem não entra** (item 3 acima): "inativos há N dias" é exatamente o `inactive`
  daqui, no mesmo código, então a contagem que o dono vê e a lista que o disparo usaria nunca divergem.
- **Aviso na tela (AE.3):** contas de antes do registro de acesso foram carimbadas pela migration (seção 2); o filtro só é
  confiável N dias depois do deploy.

### 3.2 O admin como entrada (AE.3, feita)

O `/admin` era a tela de dados de mercado. Com mais de uma área, virou uma página de entrada com um cartão por área, e cada área
ganhou a própria rota: `/admin/mercado` e `/admin/pessoas`. O menu da conta tem uma só entrada, "Administração". Mais áreas
(campanhas, fila de e-mails) entram como novos cartões em `AdminHomePage`, sem mexer no resto. Cada área leva o "voltar" para a
entrada (`AdminBack`).

**Tela de Pessoas.** Um campo "Dias sem acesso" (1 a 365, validado na tela antes de consultar; o servidor continua decidindo) com
os atalhos 7, 30 e 90, que só preenchem o campo; a consulta espera 300 ms sem digitar. O número grande é quem está sem acesso há N
dias ou mais; abaixo, quem acessou nos últimos N dias, quem confirmou o e-mail e nunca usou, quem ainda não confirmou e o total de
contas, e o aviso de que o filtro só é confiável N dias depois do deploy. Trocar o número mantém a contagem anterior esmaecida;
com o campo inválido a contagem some (a anterior seria de outro N).

### 3.3 Disparos de e-mail (AE.4, o mecanismo; a tela é a AE.5, seção 3.4)

Pedido do dono (2026-10-31): não um botão que reenvia a confirmação, e sim um mecanismo para **várias formas de disparo** (confirmação
de quem travou, "sentimos sua falta", novidades). Cada disparo é a combinação de **um modelo** (qual e-mail) e **um público** (a quem
vai), e reaproveita tudo o que já existia: `EmailKind` + `IEmailComposer`, a fila `outbox_emails`, o `EmailLayout`, a prévia e a
regra de "N dias" (`ActivityWindow`).

| Modelo (`code`) | `EmailKind` | Categoria | Público | Intervalo entre disparos |
|---|---|---|---|---|
| `confirm-reminder` "Falta confirmar o e-mail" | `ConfirmEmailReminder` | **Conta** | não confirmou o e-mail há N dias ou mais (padrão 3) | uma vez por conta, para sempre |
| `miss-you` "Sentimos sua falta" | `MissYou` | **Relacionamento** | confirmou e está sem acesso há N dias ou mais (padrão 30) | 30 dias |

**Modelo novo** é: uma linha no `EmailCampaignCatalog`, o texto (`CampaignEmails`) e o montador (`IEmailComposer`). Nada mais muda.

**A API** (`/admin/emails`, política `Admin`, `src/Prisma.Api/Http/admin-email.http`):

| Rota | O que faz |
|---|---|
| `GET /templates` | os modelos, e se cada um pode sair agora (`sendable`, `blockedCode`) |
| `GET /audience?template=&days=` | quantas pessoas estão no público agora (`count`) e o teto por disparo (`cap`); só a contagem |
| `POST /preview` | o e-mail com dados de exemplo (assunto, HTML pronto e texto); nunca uma pessoa de verdade nem um token que valha; vale para modelo bloqueado |
| `POST /test` | o e-mail de exemplo **só para o endereço do próprio admin**, com o assunto `[Teste] …`; sai na hora pelo transporte, sem fila e sem contar como disparo; 502 `SendFailed` se o fornecedor recusar |
| `POST /send` | o disparo: `{ template, days, expectedCount, clientRequestId }` |
| `GET /campaigns` | o histórico (modelo, dias, quantas pessoas, quando), sem dado de pessoa |

**As travas do `POST /send`** (a regra é `CampaignGuard`, no domínio, na ordem em que o dono as encontra):

1. **Categoria.** Modelo de **relacionamento** é opcional para a pessoa e **só sai com o descadastro** (`UnsubscribeRequired`). A trava é
   de código (`EmailCampaignCatalog.UnsubscribeAvailable`, falso até a AE.6), não de cuidado: o "sentimos sua falta" já existe, aparece
   na prévia e no teste, mas não sai para ninguém. E-mail **de conta** (a confirmação) é parte do serviço contratado e não depende disso.
2. **Alguém no público** (`NoRecipients`) e **dentro do teto** (`TooManyRecipients`; padrão 50, `Email:Campaigns:MaxRecipients`):
   passou do teto, o envio é **recusado**, nunca cortado. O teto vem da cota do fornecedor (o plano gratuito do Resend: 100 por dia).
3. **A contagem que o dono viu** (`expectedCount`): se o público mudou entre olhar e enviar, é `409 AudienceChanged`, e nada sai.
4. **Ninguém duas vezes.** `email_campaign_recipients` (uma linha por pessoa e disparo) guarda quem recebeu cada modelo, e o público
   de um modelo exclui quem já recebeu: sempre (uma vez por conta) ou dentro do intervalo (`CampaignCooldown`). A fila apaga as linhas
   depois de 30 dias, então o registro de quem recebeu precisa de tabela própria; ela leva `user_id` com cascata, e sai com a conta.
5. **O mesmo pedido, uma vez.** `clientRequestId` é único em `email_campaigns`: repetir o pedido (toque duplo, nova tentativa)
   devolve o disparo que já existe, sem enfileirar de novo. A mesma chave para outro modelo é `409 RequestIdReused`.
6. **Um disparo por vez de cada modelo.** Um lock do Postgres por modelo (`pg_advisory_xact_lock`) serializa dois disparos do mesmo
   modelo: o segundo espera o primeiro gravar quem recebeu e, ao olhar o público, já não os vê. Disparo, destinatários e e-mails da
   fila entram numa **transação só**: ou tudo, ou nada.
7. **Limite por pessoa de quem pede.** Teste e disparo dividem `admin-email` (6 por minuto, `RateLimiting:AdminEmail:PermitLimit`).

**Escalonamento.** Cada e-mail do disparo entra na fila com a primeira tentativa adiada (`Email:Campaigns:StaggerSeconds`, padrão
5 s, um depois do outro). A fila é ordenada por `NextAttemptAt`, então o e-mail de senha ou de confirmação de outra pessoa, pedido no
meio de um disparo, passa à frente, e a cota do fornecedor não estoura de uma vez. O disparo **não usa** o `EmailQueue` (o limite de
5 por dia e 1 por minuto por pessoa e tipo é de quem pede um e-mail de conta para si): os tipos novos têm o próprio limite, e o
reenvio normal da confirmação não é consumido por um lembrete.

**Na hora do envio** o montador confere de novo: o lembrete de confirmação não sai para quem confirmou entre o pedido e o envio
(`Skipped`), e o "sentimos sua falta" não sai para quem voltou depois de entrar na fila (`LastSeenAt` depois do pedido). O link de
confirmação do lembrete é novo (token gerado na hora, vale 3 dias), como o do cadastro.

**Quem é "travou".** Conta sem o e-mail confirmado há N dias ou mais (dias civis de São Paulo, a regra de `ActivityWindow`). Quem nunca
confirmou e tem N dias recebe **um** lembrete na vida da conta; limpar contas nunca confirmadas é outra etapa (S.3 do `PLAN.md`).

**Logs** (seção 2.1): `EmailCampaignSent`, `EmailCampaignRefused` e `EmailTestSent`/`EmailTestFailed`; nunca endereço, assunto ou texto.

Outros fluxos de admin que surgiram: aviso de manutenção ou de mudança nos termos, ver e-mails parados na fila, abrir e fechar o
cadastro sem o Railway.

## 3.4 A tela dos disparos (AE.5, feita) e os passos seguintes

`/admin/emails`, o cartão "E-mails" da entrada do admin. De cima para baixo:

1. **Qual e-mail.** Um cartão por modelo, com o nome, a categoria ("E-mail de conta" ou "De relacionamento") e a descrição. O de
   relacionamento leva um cadeado e "Só prévia e teste por enquanto." (a trava de `UnsubscribeRequired`).
2. **Para quem.** O campo de dias (1 a 365, validado antes de consultar, com espera de 300 ms) e atalhos do modelo (3, 7 e 14 para a
   confirmação; 30, 60 e 90 para o "sentimos sua falta"), que só preenchem o campo. Abaixo, o número de pessoas do público, a frase
   do público com o N escolhido e o teto por disparo. Trocar o número mantém a contagem anterior esmaecida; com o campo inválido
   ela some.
3. **Como fica.** O assunto e o e-mail com dados de exemplo, num `iframe` com `sandbox` vazio (sem scripts, sem sair da página).
   Funciona também para o modelo bloqueado.
4. **Enviar.** "Enviar teste para mim" (sai na hora, só para o seu endereço) e "Enviar para N pessoas". O segundo abre uma confirmação
   ("Enviar "…" para N pessoas? Depois de enviado, não dá para desfazer.") que **trava a contagem que você viu** e uma chave de pedido
   (`clientRequestId`) estável: se o público mudar enquanto a confirmação está aberta, ela fecha; se mudar depois do toque, o servidor
   recusa (409) e a tela mostra o motivo e a contagem nova. O botão só vale com o modelo liberado, dias válidos e público entre 1 e o
   teto, e diz por que não quando não vale.
5. **Últimos disparos.** Modelo, dias, quantas pessoas e a hora (São Paulo). Nenhum dado de pessoa.

O disparo não tem como ser desfeito depois de enviado; o e-mail sai aos poucos, nos minutos seguintes (escalonado na fila). Quem
recebe nunca aparece na tela, só quantos.

Passos seguintes:

- **AE.6 (EM ESPERA):** o descadastro (preferência por pessoa no Prisma, link assinado sem login, cabeçalhos `List-Unsubscribe` e
  `List-Unsubscribe-Post`), que depende da base legal.
- **AE.7 (EM ESPERA, depende da AE.6):** liberar o "sentimos sua falta" (`UnsubscribeAvailable` verdadeiro, rodapé de descadastro no texto).

## 4. Em aberto (decisão do dono)

- Base legal do e-mail de inativos: legítimo interesse (precisa da avaliação em `.lgpd/`) ou consentimento no cadastro.
- Redação da política para o e-mail de relacionamento (o último acesso, por si, ficou fora dela, seção 2).
- ~~Disparo manual ou automático~~ **Decidido: sempre manual** (AE.4): o dono vê a contagem, a prévia e confirma cada envio; nada sai sozinho a quem passar do prazo.
- Quantas pessoas existem hoje: se forem poucas, uma consulta SQL e um envio manual podem bastar por um tempo.
