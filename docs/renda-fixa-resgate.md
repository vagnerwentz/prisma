# Resgate de renda fixa: especificação e plano

> **Estado (2026-10-19): especificação aprovada pelo dono (inclusive as quatro recomendações da seção 8); R.22 a R.26 feitas (domínio e motor, API e banco, contas, tela, Carteira e documentos).** Revê a
> **D5** de `docs/renda-fixa.md` (resgates fora da v1): a decisão passa a ser a **D18** (seção 7 daquele arquivo). Etapas R.22 a R.26 no `PLAN.md`
> (a R.17 a R.21 é o IPCA, **em espera**: `docs/renda-fixa-ipca.md`).

Legenda: **[D]** decidido pelo dono (2026-10-19); **[REC]** recomendação minha, aguardando o dono.

---

## 1. O que o dono decidiu **[D]**

1. **Parcial e total.** A pessoa digita o valor **bruto** que resgatou, como no banco. Principal e rendimento saem na proporção; a faixa de IR do aporte não muda; o que sobra continua rendendo.
2. **Imposto calculado, com o líquido do extrato opcional.** O app calcula IOF e IR do pedaço resgatado. Se a pessoa informar o líquido que o banco pagou, **o do extrato vale** e a diferença é o imposto ("o extrato manda").
3. **Contas, opcional.** O principal que saiu volta como **transferência** (conta de investimento → conta corrente ou carteira) e o **rendimento líquido** entra como **receita** na conta de destino. "Cai em qual conta?" é opcional, como o "Saiu de uma conta?" do aporte; excluir pergunta se os lançamentos ficam ou saem junto.
4. **A pessoa escolhe o aporte.** O botão "Resgatar" fica em cada aporte, como no banco (cada aplicação se resgata à parte). Resgate FIFO por objetivo pode vir depois, sem mudar o modelo.

---

## 2. Modelo

`FixedIncomeRedemption` (`fixed_income_redemptions`), filha do aporte, com dono (`UserId`, filtro global) e soft delete (regra 7):

| Campo | O que guarda |
|---|---|
| `DepositId` | o aporte resgatado |
| `RedemptionDate` | o dia do resgate (`DateOnly`) |
| `GrossCents` | o valor bruto resgatado (o que a pessoa digitou ou o saldo inteiro) |
| `PrincipalCents` | a parte do principal que saiu (**congelada** na gravação) |
| `YieldCents` | `GrossCents − PrincipalCents`: o rendimento bruto que saiu |
| `IofCents`, `IrCents` | o imposto calculado; **nulos** quando a pessoa informou o líquido (não dá para separar) |
| `TaxCents` | o imposto total: soma dos dois, ou `GrossCents − líquido informado` |
| `NetCents` | `GrossCents − TaxCents` |
| `TaxSource` | `Estimated` ou `Informed` |
| `TaxRuleVersion` | a regra (`FixedIncomeTax`) que valeu; nulo se informado |
| `TransferPairId?`, `IncomeTransactionId?` | os lançamentos nas contas, quando houve (seção 5) |
| `ClientRequestId` | contra duplicar (toque duplo), único por pessoa, como no aporte |

Nada é recalculado depois: o resgate é um **fato realizado**, ao contrário do imposto "se resgatar hoje" dos aportes em aberto. O saldo restante é sempre calculado.

---

## 3. Cálculo

Para um aporte de principal `P` e fator `F(t)` (o do motor de hoje, que não muda), com resgates anteriores a `d` já tirando `Σ principal`:

- Principal restante antes do resgate: `Prem = P − Σ PrincipalCents` dos resgates anteriores.
- Saldo bruto em `d`: `S(d) = round(Prem × F(d))`, com a regra de sempre (a taxa do dia do resgate **não** entra: ela já é do que sobra).
- Resgate de `X` (bruto), com `0 < X ≤ S(d)`:
  - `PrincipalCents = round(Prem × X / S(d))`, meio centavo para cima, em inteiro (`Int128`); se `X = S(d)`, é `Prem` (sem sobra de centavo).
  - `YieldCents = X − PrincipalCents`.
  - **Imposto** (`FixedIncomeTax`, a mesma regra versionada por produto): o prazo é da data do **aporte** até `d`, e o imposto incide sobre o `YieldCents` **do pedaço resgatado**. A faixa de IR é a do aporte, não a do dia em que foi pedido o resgate parcial anterior. Se a pessoa informa o líquido `N`: `TaxCents = X − N`, com `0 ≤ TaxCents ≤ YieldCents` (imposto nunca passa do rendimento: IOF é limitado a ele e o IR incide sobre o que sobra; `N` fora disso é recusado com mensagem).
- Saldo **depois** de um resgate: `Prem' = Prem − PrincipalCents`, e `S(t) = round(Prem' × F(t))` para `t ≥ d`. Como o saldo é linear no principal, `S(d) − X` e `Prem' × F(d)` coincidem a menos de **1 centavo** (arredondamento) enquanto o fator é pequeno: propriedade testada. O principal é um inteiro em centavos, então o erro do arredondamento dele é multiplicado pelo fator: até `F/2` centavos por resgate, que só passa de 1 centavo com fator acima de 2 (anos a taxa alta) **e** principal de poucos centavos. Nesse caso o centavo de principal que sobra vale o saldo inteiro, e o rendimento nunca some (a trava `Prem' ≥ 1` num resgate parcial garante isso). Resgate de principal zero (só rendimento) é válido.
- O motor ganha uma versão de `Balance` e `Series` que recebe a lista dos resgates do aporte (data e principal): o principal vale por trechos, e o resgate com data `≤ asOf` já conta. O CDI, o CDI+ e o pré-fixado usam o mesmo caminho, porque a mudança é só no principal.

Regras do pedido (a API e o domínio recusam, em pt-BR):
- Data não pode estar no futuro, nem ser anterior à do aporte, nem **anterior à do último resgate do mesmo aporte** (os valores congelados dependem da ordem). Duas no mesmo dia valem, na ordem em que foram gravadas.
- Valor entre R$ 0,01 e o saldo da data. "Resgatar tudo" preenche o saldo de hoje.
- **[REC]** CDI e CDI+: a data precisa ter as taxas até o dia útil anterior; senão o saldo estaria subestimado e o resgate sai errado. Recusa com `RatesUnavailable` e o texto "As taxas do CDI ainda não chegaram até essa data". Pré-fixado não depende de taxa publicada.
- Vencimento e liquidez **não bloqueiam**: a pessoa pode ter resgatado antes ou depois do vencimento; a tela só avisa se for antes.
- Aporte com resgate trava valor, data, produto, índice **e taxa** (como o aporte financiado trava valor e data): mudar o contrato mudaria o que foi congelado (409, código `RedeemedDepositLocked`). Observação, objetivo, vencimento e liquidez continuam editáveis.

**Excluir (Desfazer)**, 30 minutos, soft delete: só o **último** resgate de cada aporte pode ser excluído, porque os seguintes foram congelados em cima dele (409 `RedemptionNotLast`). **Restaurar** só volta se o aporte está como estava quando o resgate foi gravado: nenhum resgate vivo criado depois dele e nenhum criado antes que continue excluído (409 `RedemptionCannotReturn`); olhar só a data não bastaria (excluir o R1 e criar um R2 com data anterior faria o R1, ao voltar, contar o principal duas vezes).

---

## 4. Efeito nos números

- **Aportado** do objetivo, do grupo e da Carteira passa a ser o **principal restante**; aporte totalmente resgatado sai do saldo e do total. Ele **não** some: continua listado, mas à parte (seção 6).
- **Resgatado**, número novo e realizado: soma dos `NetCents` (líquido recebido) e dos `YieldCents` brutos, por aporte, objetivo e total, com o imposto pago. Rótulos fixos: "Resgatado (líquido)", "Imposto pago". Nunca "lucro".
- **Gráfico de evolução** (bruto): cai na data do resgate. A nota do gráfico diz que o resgate aparece como queda.
- **Carteira** (R.9): o peso da renda fixa é o aportado **restante na data do ano** (resgate depois do fim do ano não conta nele): sai de graça da regra de data de referência.
- O "Investido" do Resumo cai pelo **principal** devolvido à conta corrente (a transferência, regra 5), e o rendimento entra em "Entrou" como receita. Resgate sem conta (acompanhamento) não mexe no Resumo.

---

## 5. As contas

Só aporte **financiado por uma conta** (`TransferPairId`) oferece "Cai em qual conta?": é ele que diz qual a conta de investimento de onde o principal sai. Aporte que só era acompanhado não tem conta de origem; o resgate dele é só registro. **[REC]** Não perguntar a conta de investimento num aporte que nunca passou por uma: ficaria sem origem para a transferência.

Ao salvar, na **mesma gravação** do resgate:
- **Transferência** (`Transfer.Create`, `TransferLegs`) de `PrincipalCents`, da conta de investimento do aporte para a conta de destino (corrente ou carteira), na data do resgate, descrição "Resgate de renda fixa". Fora de toda soma de receita e despesa (regra 5).
- **Receita** de `NetCents − PrincipalCents` (o rendimento líquido), na conta de destino, na data do resgate. Se for zero, não há lançamento. **[REC]** Categoria "Rendimentos" do catálogo padrão, se existir; senão, sem categoria (a etapa R.24 confere o catálogo antes de decidir). O imposto não vira despesa: é abatido do valor.
- Se o líquido informado for **menor que o principal** (imposto > rendimento é impossível pela regra da seção 3, então só ocorre com erro de digitação), a API recusa antes de gravar.

Excluir o resgate pergunta, como o aporte financiado (R.4b): "Manter os lançamentos nas contas" ou "Excluir junto" (os dois lançamentos saem). O "Desfazer" devolve tudo.

---

## 6. Tela (R.25)

- Em cada aporte (lista de um objetivo e "Sem objetivo"): ação **Resgatar** (a barra de ações do aporte, verbo + destino). Painel no mesmo molde do aporte (`BottomSheet`): data (hoje), valor bruto com o atalho **Resgatar tudo**, **prévia** (rendimento que sai, imposto estimado, líquido), "Valor líquido do extrato (opcional)", "Cai em qual conta?" (só se financiado). Nada de "lucro" nem "você ganhou": "Rendimento bruto", "Imposto", "Líquido recebido".
- Aporte com resgate parcial: a linha mostra o saldo restante e "Resgatado R$ …"; o detalhe lista os resgates, cada um com data, bruto, imposto, líquido e o botão de excluir (só o último).
- Aporte inteiramente resgatado: vai para uma seção recolhida **"Resgatados"** do objetivo, com o que rendeu e o imposto pago; fora do saldo e do "Aportado".
- A nota "Estimativa; o extrato manda" continua; o resgate registrado é fato, e a tela diz "Resgate registrado".
- Conferir nos dois modos e em 320/390/1280, com toque real (CLAUDE.md, 7.1). Texto "sem resgates" some das notas da renda fixa.

---

## 7. Etapas

Cada uma tem teste junto (CLAUDE.md, seção 5) e para no fim aguardando revisão. A migration só é aplicada no banco de dev com autorização do dono.

| Etapa | Entrega | Pronto quando |
|---|---|---|
| **R.22** | **Domínio e motor.** `FixedIncomeRedemption` (criação, validação, congelar principal e imposto; imposto informado), `Balance`/`Series` com os resgates do aporte, `RemainingPrincipal`. Nada de banco nem API. | Testes antes: oráculo independente em `BigInteger`; propriedade (CsCheck): `Σ principal + restante = P`, parcial seguido de total zera, `S(d) − X` e `Prem' × F(d)` diferem ≤ 1 centavo, resgate em data qualquer = `Series` no ponto; bordas (resgate no dia do aporte, no mesmo dia de outro, `X = S(d)`, `X = 1`); imposto da faixa do aporte (180/181/360/361/720/721 dias), IOF abaixo de 30 dias, LCA/LCI; líquido informado fora de `[principal, X]` recusado; mutações provadas |
| **R.23** | **API e migration.** Tabela, `POST /fixed-income/deposits/{id}/redemptions`, `GET`, exclusão e restauração (só o último); `balances` e `evolution` passam a descontar os resgates (campos novos, só acrescenta); `SaveDeposit` trava o contrato do aporte com resgate; o log (`AppLog`, só ids). | Integração com Postgres: valores gravados = domínio; dois usuários; exclusão restaura; ordem e travas (409 onde couber); `ClientRequestId` repetido devolve o mesmo; migration revisada; `export-user.sql` e os testes de exclusão de conta acusam a tabela nova |
| **R.24** | **Vínculo com as contas.** Transferência do principal + receita do rendimento na mesma gravação; excluir com "manter/excluir junto". Confere o catálogo de categorias. | Integração: saldo das contas, "Investido" e "Entrou" do Resumo; transferência fora de receita e despesa; rendimento zero sem receita; aporte sem financiamento recusa a conta |
| **R.25** | **Tela.** Painel "Resgatar", prévia, lista dos resgates, "Resgatados", chave Bruto/Líquido coerente, `gen:api`, textos. | Vitest nas contas puras da tela; conferência visual nos dois modos e em 320/390/1280, com toque real |
| **R.26** | **Carteira e documentos.** Peso pelo principal restante, notas da renda fixa sem "sem resgates", `CLAUDE.md`, `docs/renda-fixa.md`, `docs/operacao.md` se preciso, texto de privacidade (item de pendência) e `README`. | Testes do `PortfolioSnapshot` com resgate no ano e depois dele |

---

## 8. Perguntas ao dono (as quatro recomendações foram aprovadas em 2026-10-19) **[D]**

1. **Datas das taxas:** recusar resgate de CDI/CDI+ em data sem taxa publicada (recomendado) ou aceitar e avisar?
2. **Aporte sem financiamento:** só registro, sem conta (recomendado), ou perguntar também a conta de investimento?
3. **Excluir só o último resgate do aporte** (recomendado, pelos valores congelados) ou permitir qualquer um, recalculando os seguintes?
4. **Categoria da receita do rendimento:** "Rendimentos" do catálogo padrão; a R.24 confere se existe e propõe o que fazer se não.

## 9. Fora do escopo

Resgate FIFO por objetivo, resgate automático no vencimento (aporte vencido só ganha um aviso depois, se o dono quiser), projeção, IPCA e IPCA+ (em espera), come-cotas (não existe em CDB) e tesouro direto.

## 10. Como a R.22 ficou (domínio)

`Prisma.Domain/FixedIncome/FixedIncomeRedemption.cs`: `FixedIncomeRedemption.Create(deposit, previous, rates, date, grossCents, informedNetCents, clientRequestId, today)` devolve o resgate com principal, rendimento e imposto congelados, ou o erro em pt-BR. `RedemptionStep(Date, Principal)` e `FixedIncomeEngine.Balance`/`Series` com a lista de passos: sem passos, o resultado é idêntico ao de antes (testado). Resgate com principal zero é válido; passo negativo, antes do aporte ou que faça a soma passar do aporte é recusado. A checagem das taxas usa `ReferenceRateCoverage.ExpectedLastDate` (o calendário bancário continua só no diagnóstico). Sem tabela, API nem tela até a R.23.

## 11. Como a R.23 ficou (API e banco)

- **Tabela** `fixed_income_redemptions` (migration `AddFixedIncomeRedemptions`; **aplicada no banco de dev só com autorização do dono**): dono e soft delete como os aportes; chave estrangeira em **cascata** para o aporte (o resgate é parte dele: sai de vez quando a limpeza dos excluídos apaga o aporte); índice único `(user_id, client_request_id)` que conta o excluído; constraints que o banco impõe (`ck_fixed_income_redemptions_*`): bruto = principal + rendimento, imposto ≤ rendimento, líquido = bruto − imposto, e IOF/IR/regra juntos no calculado e ausentes no informado.
- **Rotas** (`Features/FixedIncome/Redemptions.cs`, sob `/fixed-income-deposits/{id}/redemptions`): `POST` (corpo `clientRequestId`, `redemptionDate`, `grossCents`, `netCents?`), `GET`, `DELETE /{redemptionId}` e `POST /{redemptionId}/restore`. Erros: 404 (aporte ou resgate que não é seu), 400 do domínio (valor, data, taxas, líquido), 409 (`RedemptionNotLast`, `RedemptionCannotReturn`, `RedeemedDepositLocked`, e o token de concorrência).
- **Concorrência:** criar, excluir e restaurar tocam o aporte (`UpdatedAt`, token `xmin`): dois resgates ao mesmo tempo nunca vendem o mesmo saldo duas vezes (o segundo dá 409). Pedido repetido com o mesmo `clientRequestId` devolve o resgate que já existe.
- **Saldos** (`GET /fixed-income/balances`): `principalCents` do aporte e do grupo é o que **sobra**; `redeemed` (aporte) e `redeemed*Cents` (grupo) trazem o já resgatado até a data de referência; resgate depois da data não conta. Só acrescenta campos. **Evolução:** o saldo cai no dia do resgate (que ganha ponto próprio). **Objetivos** (`GET /savings-goals`): o aportado é o principal restante.
- **Excluir o aporte** leva os resgates junto (ficam invisíveis, pois a consulta parte do aporte) e **desfazer** o aporte os traz de volta. A exclusão de conta, a limpeza dos excluídos e a cópia dos dados da pessoa (`resgatesRendaFixa`) cobrem a tabela nova.
- **A Carteira** (`GetPortfolio`) lê o principal do mesmo saldo e portanto **já pesa pelo que sobra** (esta nota dizia o contrário, por engano; corrigido e provado na R.26, seção 14). A tela é a R.25 (seção 13).

## 12. Como a R.24 ficou (contas)

- **Pedido:** `POST .../redemptions` aceita `funding: { toAccountId }`, opcional. Sem ele, o resgate é só registro. Com ele, **na mesma gravação** do resgate: a transferência do **principal** (`FixedIncomeRedemptionFunding`, domínio: da conta de investimento do aporte para a de destino, `PaymentMethod.Ted`, descrição "Resgate de renda fixa", na data do resgate) e a **receita** do rendimento líquido (`NetCents − PrincipalCents`, na conta de destino, categoria padrão "Rendimentos" se a pessoa ainda a tem, descrição "Rendimento de renda fixa"). O imposto não vira despesa. Principal zero não tem transferência; rendimento líquido zero não tem receita.
- **Só aporte financiado** (decisão do dono, seção 8): é o aporte que saiu de uma conta pelo app que diz qual a conta de investimento. Sem financiamento vivo, ou com a conta de investimento apagada, a conta é recusada: 400, código `RedemptionNeedsFundedDeposit` ("registre só o resgate"). Destino que não é conta corrente nem carteira: 400 `RedemptionNeedsPaymentAccount`; conta que não é sua: 404 (o filtro de dono a esconde). Recusado, nada é gravado.
- **Resposta:** `funding` (`transferPairId`, `incomeTransactionId`, `investmentAccountId`, `destinationAccountId`) aparece no resgate e na lista enquanto algum lançamento está vivo; some se a pessoa os excluiu pelos lançamentos.
- **Excluir:** `DELETE .../redemptions/{id}?transfer=keep|delete`. Com lançamentos vivos, a escolha é obrigatória (400 `TransferChoiceRequired`), como no aporte financiado: `keep` tira só o resgate e deixa a transferência e a receita nas contas; `delete` leva os três juntos (mesmo instante), e **desfazer** traz os três de volta (409 se uma das contas foi excluída nesse meio-tempo). Excluir os lançamentos pelas telas de lançamentos não exige escolha: o resgate simplesmente deixa de mostrar o vínculo.
- **Efeito no Resumo:** o "Investido" cai pelo principal devolvido, o "Entrou" sobe pelo rendimento líquido, e o "Saiu" não muda (testado contra o dashboard).
- **Banco:** colunas `transfer_pair_id` e `income_transaction_id` (sem chave estrangeira, como no aporte), índice `(user_id, transfer_pair_id)`; migration `AddRedemptionFunding`.
- **Fora de propósito:** excluir o **aporte** não mexe nos lançamentos do resgate (só nos do aporte, pela escolha de sempre): a transferência e a receita são lançamentos da pessoa nas contas.

## 13. Como a R.25 ficou (tela)

- **Prévia na API, não na tela.** O imposto e o principal que sai são regra de dinheiro do domínio (`Int128`, regra de IOF e IR por produto, versionada): refazê-los em TypeScript seria duplicar a regra e arriscar um número diferente do gravado. Por isso `GET /fixed-income-deposits/{id}/redemptions/preview?date=&grossCents=&netCents=` roda a mesma criação do resgate, sem gravar, e devolve o resgate como seria gravado. Pedido que o domínio recusaria vem como `problem`/`problemCode` (e o saldo da data em `balanceCents`), não como erro HTTP, para a tela mostrar o motivo enquanto a pessoa digita. Um teste confere que a prévia é igual ao que o `POST` grava.
- **Data inicial = último dia possível.** `latestDate` (domínio: `ReferenceRateCoverage.LatestRedemptionDate`): no CDI, o primeiro dia útil depois da última taxa gravada, sem passar de hoje; no pré-fixado, hoje. A conferência pela tela achou o defeito que isso resolve: o padrão "Hoje" falhava toda vez que o CDI do dia útil anterior ainda não tinha saído (por exemplo, uma segunda-feira), e a pessoa caía num erro ao abrir o painel.
- **Onde:** botão **Resgatar** em cada aporte com principal (`.tinted-action`, faixa ciano), fora do botão da linha (que abre a edição); painel `RedemptionSheet` (data, valor bruto com "Resgatar tudo", prévia com "Rendimento bruto que sai", "Imposto estimado" (ou "pelo extrato") e "Líquido que cai", "Líquido do extrato" opcional, "Cai em qual conta?" só para aporte que saiu de uma conta, já sugerindo a conta de onde ele saiu). O aviso "Registrar resgate" fica no rodapé do painel; o toast tem **Desfazer** (exclui o resgate junto com os lançamentos que ele criou).
- **No painel do aporte** (`DepositSheet`): com resgate, valor, data, produto e taxa viram texto ("Contrato: CDB · 100% do CDI") e a seção **Resgates** lista cada um (bruto, data, líquido, imposto, "caiu numa conta"); só o último tem **Excluir**; se caiu numa conta, a pergunta "Manter os lançamentos / Excluir junto". Não há tela de "desfazer" fora do aviso, e a API recusa (409) o que a regra proíbe.
- **Números:** `Aportado` é o principal que sobra; o objetivo e o total ganham a linha **"Já resgatado: líquido R$ … · imposto pago R$ …"** (nunca "lucro"); aporte resgatado por inteiro vai para a seção recolhida **"Resgatados (n)"**, com o que recebeu e o imposto pago, e deixa de ter botão. O gráfico (sempre bruto) cai no dia do resgate e diz isso na legenda. A nota "sem resgates" saiu do aviso do que a renda fixa ainda não faz.
- **Código:** `RedemptionSheet.tsx` (novo), `GoalsTab.tsx` (`DepositRow`, "Resgatados"), `DepositSheet.tsx`, `fixedIncome.ts` (contas puras e textos, com Vitest), `fixedIncomeQueries.ts` (`useRedemptions`, `useRedemptionPreview`, `useCreateRedemption`, `useDeleteRedemption`, `useRestoreRedemption`; todos invalidam também o dinheiro), tipos regenerados (`gen:api`).
- **Conferência visual** (toque real, 390, 320 no escuro e 1280): painel com a prévia, lista do aporte com "Resgatar", "Resgatados" aberto, painel do aporte com os resgates e a pergunta de excluir; saldos das contas conferidos antes e depois de excluir junto.

## 14. Como a R.26 ficou (Carteira e documentos)

- **A Carteira já descontava os resgates**, sem código novo para isso: ela lê o principal do mesmo saldo da aba Objetivos (`GetBalances`), que desde a R.23 é o que sobra na data de referência. A nota da seção 11 ("a Carteira ainda não desconta") estava errada. O que faltava era **prová-lo por teste** (`PortfolioRedemptionTests`) e um defeito pequeno: o contador de aportes da linha (`DepositCount`) contava também os resgatados por inteiro. Agora conta só os que ainda têm principal na data (`GetPortfolio.OpenDeposits`).
- **Regras provadas:** o peso e o "Investido" são o principal que sobra; o resgate depois do fim do ano **não** conta na carteira daquele ano (a de 2025 é a de 31/12/2025, com o aporte inteiro); o objetivo resgatado por inteiro sai da Carteira (some a linha e o grupo) mas continua nos anos anteriores ao resgate.
- **Fora de propósito:** a Carteira não mostra o que já foi resgatado (isso vive na aba Objetivos: "Já resgatado" e "Resgatados"), e o rendimento já pago não entra como "rendimento na renda fixa" (que é o do que continua investido).
- **Documentos varridos:** `docs/renda-fixa.md` (estado, "fora da v1", "sem resgate", a seção 5.3, D5 e a lista da seção 12), `docs/investimentos.md`, `CLAUDE.md`, `README.md` e `PLAN.md`.
- **Texto legal (esclarecido em 2026-10-20, versão 1.0 mantida):** o resgate guarda os mesmos tipos de dado que o aporte (valores, datas, o líquido que a pessoa digita), sem dado novo de natureza diferente, então não pede texto próprio; mas a política (seção 3.2) e os termos não citavam renda fixa desde a R.8. A pedido do dono foram esclarecidos: a renda fixa (objetivos, aportes e resgates) entre os dados registrados e o rendimento e imposto estimado entre as finalidades, a observação do aporte entre os textos livres, o Banco Central entre as fontes públicas (sem receber dado de ninguém) e, nos termos, a linha de que rendimento e imposto da renda fixa são estimativas. Por ser esclarecimento, a versão não subiu (ninguém precisa aceitar de novo) e o histórico da política registra a data; a revisão do advogado (A.2) continua pendente.
