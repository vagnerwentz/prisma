# Admin: pessoas inativas e e-mail em massa (AE)

Conversa do dono com o assistente em 2026-10-29. **Estão feitas a AE.1 (último acesso), a AE.2 (contagem, API) e a AE.3 (o admin como
entrada e a tela de Pessoas)**; o resto (descadastro, segmentos, campanha, disparo) é desenho para decidir, sem etapa no `PLAN.md`
até o dono aprovar. O que já existe e se reaproveita: a política `Admin`
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

Outros fluxos de admin que surgiram: reenviar a confirmação a quem travou no cadastro (o de menor risco), aviso de
manutenção ou de mudança nos termos, ver e-mails parados na fila, abrir e fechar o cadastro sem o Railway.

## 4. Em aberto (decisão do dono)

- Base legal do e-mail de inativos: legítimo interesse (precisa da avaliação em `.lgpd/`) ou consentimento no cadastro.
- Redação da política para o e-mail de relacionamento (o último acesso, por si, ficou fora dela, seção 2).
- Disparo sempre manual (recomendado) ou automático a quem passar do prazo.
- Quantas pessoas existem hoje: se forem poucas, uma consulta SQL e um envio manual podem bastar por um tempo.
