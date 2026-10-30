# Patrimônio investido — fase I.9

> **Estado: I.9a a I.9d feitas (2026-10-28 e 29): domínio, API, tela e documentos. Conferida pelo dono em 2026-10-29 (roteiro de 11 passos, "deu tudo certo") e com a suíte completa verde. Falta só aplicar na produção (seção 8), com o "pode" do dono.** Migrations aplicadas só no banco de desenvolvimento. Etapas em `PLAN.md`, "I.9".
> se o código ou outro documento discordar, pare e pergunte. Substitui a regra de "Investido" da D20 (`docs/renda-fixa.md`, 7.4c) e o
> "Em ativos" da I.8c (`docs/investimentos.md`, seção I.8).

## 1. O problema

O "Investido" do Resumo e de Contas só enxerga o que tem **trilha de caixa**: o saldo das contas de investimento mais o rendimento dos
aportes de renda fixa que saíram de uma conta pelo app, com a transferência viva (D20), mais o "Em ativos" das notas vinculadas (I.8c).
Quem investe há anos tem renda fixa e ações sem nenhuma dessas trilhas; para o app enxergar o dinheiro antigo, a pessoa teria de
recriar todo o histórico de entradas, senão as contas ficam negativas. Achado do dono (2026-10-28, banco local): 16 aportes vivos
(R$ 151.753,84), só 1 (R$ 10.000,00) entra no "Investido"; as ações só entram se a nota foi vinculada.

A causa é misturar duas coisas:

1. **Caixa:** quanto dinheiro está em cada conta. Só fecha com histórico completo.
2. **Patrimônio investido:** o que a pessoa possui em ativos. O app já sabe, pelas notas de corretagem e pelos aportes, sem histórico de caixa.

## 2. Decisão (dono, 2026-10-28)

O "Investido" passa a vir **dos ativos**, não das contas. O caixa das corretoras vira opcional.

| Linha | De onde vem |
|---|---|
| **Disponível** | Saldo das contas **corrente e carteira** ativas (como hoje). |
| **Investido** | `investedCents` de `GET /portfolio` de hoje (principal que sobra da renda fixa, depois dos resgates, **mais** as ações, FIIs, ETFs e BDRs ao custo, de **todas** as notas, vinculadas ou não) **mais** o rendimento bruto da renda fixa (soma dos `grossYieldCents` do mesmo `GET /portfolio`). |
| **Caixa nas corretoras** | Só de conta de investimento com **acompanhar o caixa** ligado: saldo da conta **menos** o principal que sobra dos aportes de renda fixa que ela financiou. |
| **Patrimônio** | Disponível + Investido + Caixa nas corretoras. |

## 3. Regras

1. **`Account.TracksCash`** (`accounts.tracks_cash`, booleano, falso por padrão): só faz sentido em conta de investimento; nas outras fica falso e a
   API o recusa verdadeiro ("Só a conta de investimento acompanha o caixa."). **Conta existente e nova começam desligadas.** Desligar não apaga
   nada: o saldo e o extrato da conta seguem como estão, só deixam de entrar nas somas do Resumo.
2. **Conta de investimento sem acompanhar o caixa não entra em nenhuma soma de saldo.** Transferir dinheiro para ela só tira do "Disponível"; o
   que foi investido aparece em "Investido" pelos ativos. Não há dupla contagem nem conta negativa que atrapalhe: o saldo dela deixa de
   mandar. **Limitação aceita:** dinheiro transferido para uma corretora sem acompanhar o caixa e ainda não investido não aparece em lugar
   nenhum; quem guarda caixa lá liga a opção.
3. **Sem dupla contagem com o caixa acompanhado.** O principal de um aporte financiado por uma conta fica no saldo dela (R.32) **e** em
   "Investido" (o aporte vale no `GET /portfolio`). Por isso o **caixa livre** da conta é `saldo − principal que sobra dos aportes que ela
   financiou` (só aportes vivos, com a transferência viva, depois de resgates). Uma nota **vinculada** (I.8) já tirou o valor do saldo, e a
   compra entra em "Investido" pela nota: não há o que descontar. Venda vinculada crédita o caixa e baixa o custo da posição, no mesmo gesto.
4. **O caixa livre pode ser negativo** (histórico incompleto); a tela mostra o número, sem esconder, e a conta não é bloqueada.
5. **O saldo da conta não muda** em nenhuma situação (continua batendo com o extrato do banco): muda só a leitura.
6. **"Em ativos" da I.8c sai das somas.** O campo `assetsCents` de `GET /accounts/balances` fica (a API só acrescenta, tela aberta antes do
   deploy), mas a tela deixa de somá-lo; `AccountAssets` fica no domínio.
7. **O card "Investido" do mês** (fluxo: aportes menos resgates em contas de investimento) é outra coisa e passa a se chamar **"Aportado"**,
   para não ser lido como patrimônio. O campo `investedCents` do resumo mensal não muda.
8. **Ações ao custo, não a mercado.** A cotação (I.7d) fica fora desta fase; com ela, "Investido" ganha o valor de hoje em outra etapa. A tela
   diz "ao custo" onde couber.
9. **`AccountYield` ganha `principalCents`** (o principal que sobra dos aportes financiados por aquela conta): só acrescenta. O rendimento por
   conta (`grossYieldCents`) deixa de entrar em "Investido" pela conta (entra pela carteira) e serve só ao caixa livre e à linha de Contas.
10. **Um endpoint, uma conta.** `GET /dashboard/net-worth` (I.9c) devolve o Disponível, o Investido, o rendimento, o Caixa nas corretoras e, por
    conta de investimento ativa, se acompanha o caixa e o caixa livre. Usa o `NetWorthSplit` do domínio sobre o que `GET /portfolio`,
    `/fixed-income/balances` e `/accounts/balances` já calculam: a regra fica num lugar só (decidido na I.9c, no lugar de o front refazer a
    fórmula).

## 4. Valores esperados (caso do dono, banco local, 2026-10-28)

| Linha | Antes (D20 + I.8c) | Depois |
|---|---|---|
| Disponível | R$ 4.250,30 (hoje o saldo do Itaú) | igual |
| Investido | R$ 10.259,81 (caixa da Íon R$ 4.469,41 + R$ 40,70 de rendimento + R$ 5.749,70 em ativos) | **todo** o principal que sobra dos 16 aportes + as ações ao custo + o rendimento de todos os aportes |
| Caixa nas corretoras | — | nada (a Íon não acompanha o caixa) |

O valor exato de "Investido" sai do `GET /portfolio` do usuário; o teste de integração usa um conjunto semeado com resposta conhecida.

Casos de teste obrigatórios (domínio, `NetWorthSplit`): renda fixa sem trilha de caixa conta inteira; ação sem nota vinculada conta; conta
sem acompanhar o caixa é ignorada; conta com caixa: saldo menos principal financiado; nota vinculada não duplica; venda vinculada; caixa
livre negativo preservado; conta inativa fora; cartão fora; o total Disponível + Investido + Caixa **não muda** ao ligar ou desligar o
acompanhamento de uma conta que não tem aporte financiado nem nota vinculada **e** tem saldo zero.

## 5. Etapas

- **I.9a Domínio (feita)** — `NetWorthSplit` (puro, sem banco); testes antes da implementação. `Account.TracksCash` ficou para a I.9b, com a coluna.
- **I.9b API e migration (feita)** — `accounts.tracks_cash` (aditiva, falsa por padrão); criar e editar conta aceitam `tracksCash` opcional;
  `AccountYield.principalCents`; `.http` e `gen:api`. **A migration só se aplica no banco com o "pode" do dono.**
- **I.9c Tela (feita)** — `splitBalances` reescrita (Resumo "Hoje" e Contas), opção "Acompanhar o caixa" na conta de investimento, card do mês
  "Aportado"; Vitest; conferência em 320/390/1280, claro e escuro, com toque real.
- **I.9d Documentos e produção (feita, menos a produção em si)** — varredura (`CLAUDE.md`, `README.md`, `docs/`, memória), nova D21 em `docs/renda-fixa.md`, suíte completa uma
  vez, roteiro da produção. **Absorve a I.8e.**

## 6. Perguntas ao dono (respondidas: o padrão valeu nas três)

- Conta de investimento nova começa com o caixa desligado? (sim)
- A conta desligada mostra, na lista de Contas, "Caixa não acompanhado" em vez de valor? (sim; o saldo continua na tela da conta)
- O saldo de uma conta desligada continua visível dentro da conta? (sim)

## 7. Decisões da implementação

- **`GET /dashboard/net-worth` nasceu na I.9c** (a regra 10 previa medir antes): `GetNetWorth` junta `GetPortfolio`, `GetBalances` e
  `ListAccountBalances` e entrega ao `NetWorthSplit`. A regra fica num lugar só (domínio, com mutações provadas), e o Resumo faz uma chamada. A
  medição de custo fica para a produção (seção 8): o cálculo da Carteira roda a cada abertura do Resumo, com as respostas valendo 60 s no front.
- **`AccountYield.PrincipalCents` tem valor padrão 0** (a API só acrescenta), e `AccountResponse.TracksCash` também (falso).
- **A conta de investimento sem acompanhar o caixa não mostra valor em Contas**, só "caixa não acompanhado"; o saldo e o extrato continuam
  dentro dela. Quem quer ver o caixa liga a opção.
- **Limitação aceita (regra 2):** dinheiro transferido para uma corretora sem acompanhar o caixa e ainda não investido não aparece em lugar
  nenhum; sai do "Disponível" e não entra no "Investido". Quem guarda caixa lá liga a opção.
- **O caixa livre negativo aparece como é** (regra 4); numa conta com histórico incompleto ele é esperado, e desligar o acompanhamento é o caminho.
- **`balanceInAccounts`, `splitBalances` e o "Em ativos" da tela saíram do front** (código morto); `assetsCents` e `AccountAssets` ficaram na API
  e no domínio, sem uso na tela.
- **Defeito da I.8d achado e corrigido na I.9c:** salvar uma nota vinculada não recarregava saldos e lançamentos (`invalidateTrades`).
- **Conferência do dono (2026-10-29):** roteiro de 11 passos no banco local (contas, renda fixa antiga sem transferência, ação antiga sem
  vínculo, aporte com origem, ligar o caixa, transferência parada, nota vinculada, aviso de conta negativa, excluir e desfazer, desligar o
  caixa, celular). Resultado: tudo certo. Regra de ouro conferida: "Investido" do Resumo = total investido da Carteira + rendimento da renda fixa.
- **Suíte completa:** rodada pelo dono em 2026-10-29 e passou inteira (.NET e Vitest). Os números parciais de cada etapa estão no `PLAN.md`.

## 8. Produção (roteiro; nada foi aplicado)

**O que muda para quem usa.** O "Investido" do Resumo sobe (passa a incluir todos os aportes e todas as ações); o card do mês vira
"Aportado"; a conta de investimento (a Íon, por exemplo) passa a dizer "caixa não acompanhado" e a não mostrar valor; a nota de corretagem
ganha "Debitar da conta" e o extrato, a linha "Compra/Venda de ativos" (só de nota vinculada). **Nenhum saldo de conta muda** e nenhuma nota
existente é vinculada sozinha.

**Migrations (as duas só acrescentam, sem apagar nada; a imagem aplica no deploy, `Database:MigrateOnStartup`):**
- `AddTradeNoteSettlement`: `trade_notes.settlement_account_id` (anulável, chave estrangeira restrita para `accounts`), `transactions.trade_note_id`
  (anulável, **sem** chave estrangeira, índice único filtrado `ux_transactions_trade_note`) e a restrição `ck_transactions_trade_settlement`.
- `AddAccountTracksCash`: `accounts.tracks_cash` (`boolean`, padrão falso, `NOT NULL`) e a restrição `ck_accounts_tracks_cash`.
- Nenhuma variável de ambiente nova.

**Antes do deploy.** Conferir que o backup diário mais recente tem menos de 24 horas (`docs/operacao.md`, seção 5) e, se quiser, disparar
um backup na hora. Conferir que o `main` está verde no CI (`dotnet test`, front e o backup de ida e volta).

**Depois do deploy, nesta ordem:**
1. `GET /api/health` responde `Healthy`, e o log não traz `ErasureRunFailed` nem erro de migration.
2. No Resumo, o "Investido" deve bater com o total investido de Investimentos → Carteira mais o rendimento da renda fixa.
3. Em Contas, a Íon (ou qualquer conta de investimento) diz "caixa não acompanhado"; o saldo dentro dela é o de antes.
4. Abrir uma nota existente: aparece "Debitar da conta" com "Nenhuma" marcada. **Não é preciso vincular nada**: o "Investido" não depende do vínculo.
5. Medir `GET /dashboard/net-worth` no log (`RequestFinished`, rota modelo e duração) por alguns dias; com as notas e aportes do dono, a
   expectativa é de centenas de milissegundos no máximo. Se passar disso, a decisão é guardar o resultado por alguns segundos no servidor ou
   dividir o endpoint, não mexer na regra.

**Vincular a nota da PETR4 de 22/10 (opcional).** Só faz diferença para quem liga o acompanhamento do caixa da Íon: aí a nota debita o caixa dela.
Para a Íon sem acompanhar o caixa, nada a fazer.

**Reverter.** As migrations têm `Down`, mas o código antigo não conhece o tipo de lançamento `TradeSettlement`: antes de voltar o código, é
preciso apagar as liquidações já gravadas e desvincular as notas, senão a leitura dos lançamentos quebra. Em SQL, sob o `prisma_app`, com
cuidado e depois de um backup: `DELETE FROM transactions WHERE type = 'TradeSettlement'` e `UPDATE trade_notes SET settlement_account_id = NULL`.
Sem nota vinculada, não há liquidação, e o `Down` roda limpo.

**Privacidade (LGPD).** Nenhuma categoria nova de dado: `tracks_cash` é uma preferência da conta, e `trade_note_id` e
`settlement_account_id` são vínculos entre dados que a pessoa já tinha. `ops/privacy/export-user.sql` usa `to_jsonb` da linha, então as colunas
novas entram na cópia sozinhas (conferido). `AccountEraser` apaga por `user_id`, e a limpeza do excluído (`ErasureRunner`) leva a liquidação junto
com a nota e não apaga conta que uma nota ainda aponta (testes `TradeNoteSettlementApiTests`). A política de privacidade e os termos **não mudam**
(versão 1.0 mantida; nenhum novo aceite).
