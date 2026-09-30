# Fase 2 — Dashboard

Detalhamento da Fase 2 do `PLAN.md`. Regras permanentes estão em `CLAUDE.md`; aqui fica
só o que é específico desta fase.

Ao final da fase, quem abre o app vê o mês de relance: quanto entrou, quanto saiu, quanto
sobrou e quanto foi investido; onde o dinheiro foi; como o mês se compara aos anteriores; e
quais faturas vêm pela frente.

> **Idioma:** código em inglês; textos de interface e mensagens de erro em pt-BR.

---

## 1. Decisões

- **Visão de caixa:** tudo agrega por `SettlementDate` (`CLAUDE.md`, regra 4). A compra no cartão
  entra no mês do **vencimento** da fatura, não no mês da compra: um jantar de 20/09 numa fatura
  que vence em 05/10 é despesa de outubro.
- **Transferência nunca é receita nem despesa** (`CLAUDE.md`, regra 5). Pagamento de fatura,
  aporte e movimentação entre contas ficam fora de receitas e despesas.
- **O resumo é a tela inicial** (decisão do usuário). A lista de lançamentos sai de `/` e vai
  para `/lancamentos`; a barra inferior ganha "Resumo" como primeira aba.
- **O mês inteiro conta**, inclusive os dias que ainda não chegaram: o mês atual mostra também o
  que já está lançado para depois de hoje (parcelas do cartão, contas agendadas).
- **Consultas sem carregar entidades:** o banco soma agrupado, com `AsNoTracking()`; a regra
  (sinal, o que conta como investido) fica no domínio, que recebe as somas.
- Transação excluída não conta em nada.
- **Estorno** (etapas 2.5 e 2.6, decisões do usuário): abate despesa, nunca é receita; cai na
  fatura aberta na data do estorno; fatura que fica negativa mostra "saldo a favor" e não carrega o
  crédito para a seguinte; compra parcelada é estornada com um crédito único; vale no cartão, na
  conta corrente e na carteira; categoria que fica negativa no mês some do resumo.

---

## 2. Regras

### 2.1 Resumo do mês

- **Receitas** = soma das transações `Income` com `SettlementDate` no mês.
- **Despesas** = soma das transações `Expense` com `SettlementDate` no mês.
- **Sobra** = receitas − despesas (decisão do usuário). Pode ser negativa. O investido **não** é
  descontado: investir é guardar a sobra, não gastar.
- **Investido** = aportes − resgates (decisão do usuário): transferências recebidas por contas do
  tipo `Investment` menos as enviadas por elas, no mês. Transferência entre duas contas de
  investimento se anula. Pode ser negativo (mês em que se resgatou mais do que aportou).
- **No cartão** = a parte das despesas feita em contas `CreditCard`: as faturas que vencem no mês.
  Explica na tela por que compras de meses anteriores aparecem como gasto do mês.

### 2.2 Gastos por categoria

- Só despesas do mês, agrupadas pela **categoria raiz**: a despesa numa subcategoria (Mercado)
  soma na categoria pai (Alimentação).
- Despesa sem categoria vai para "Sem categoria".
- Ordem: do maior valor para o menor. A participação (%) é calculada na tela sobre o total de
  despesas do mês, arredondada para inteiro; categoria com algum gasto mostra pelo menos 1%.
- **Tocar numa categoria abre a lista que soma o mesmo valor:** despesas daquela categoria (com
  as subcategorias) com `SettlementDate` no mês, e não pela data da compra. Sem isso, o jantar de
  20/09 (fatura de outubro) contaria em outubro no resumo e sumiria da lista de outubro.
  "Sem categoria" também abre a lista, com as despesas sem categoria.

### 2.3 Comparativo mensal

- Os **6 meses** que terminam no mês escolhido, do mais antigo para o mais recente, cada um com
  receitas, despesas, sobra e investido (regra 2.1). Mês sem lançamentos aparece com zeros.
- A tela destaca a variação das despesas em relação ao mês anterior ("Gastou 12% a mais que em
  setembro"), arredondada para inteiro; arredondou para 0%, "Gastou o mesmo que em setembro". Mês
  anterior com despesa zero: sem porcentagem.

### 2.4 Próximas faturas e saldo em contas

- **Próxima fatura de cada cartão ativo:** a primeira não paga que vence hoje ou depois, com total
  maior que zero. Mostra cartão, mês, vencimento e total; no topo, a soma de todas.
- **Em contas:** soma dos saldos atuais das contas ativas que não são cartão (regra 2.5 da Fase 1).
  Reaproveita `GET /accounts/balances`.
- Os dois blocos olham para **hoje** (`IClock.Today`), não para o mês escolhido no Resumo.
- O total da fatura é o mesmo da tela do cartão (compras ativas; o pagamento não conta). Fatura
  zerada, por exemplo porque a única compra foi excluída, é pulada: vale a seguinte com valor.
- Fatura vencida e não paga fica fora: aqui só o que ainda vai vencer. As faturas saem pelo
  vencimento, a mais próxima primeiro.

Exemplo, hoje 15/10/2026:

| Cartão | Faturas | Próxima |
|---|---|---|
| Visa (fecha dia 26, vence dia 5) | 05/10 paga; 05/11 R$ 300,00 | 05/11, R$ 300,00 |
| Master (fecha dia 10, vence dia 20) | 20/10 paga; 20/11 R$ 100,00; 20/12 R$ 100,00 | 20/11, R$ 100,00 |
| Elo (fecha dia 5, vence dia 15) | 15/09 R$ 50,00, vencida; 15/10 R$ 80,00 | 15/10, R$ 80,00 (vence hoje) |
| Nubank (fecha dia 10, vence dia 20) | 20/10 zerada; 20/11 R$ 40,00 | 20/11, R$ 40,00 |
| Inter, inativo | 05/11 R$ 999,00 | fica de fora |

Ordem: Elo, Visa, Master, Nubank (Master e Nubank empatam no vencimento: desempata pelo nome do
cartão). A fatura do Master de 20/10 já fechou em 10/10 e foi paga antes de vencer: fatura aberta
não pode ser paga (Fase 1). Soma: **R$ 520,00**.

### 2.5 Estorno

Dinheiro que volta de uma compra: devolução, cancelamento, cobrança indevida. Pode estar ligado à
compra original ("Estornar" num lançamento) ou ser avulso, quando a compra nem foi lançada ou está
num mês que o usuário não quer mexer.

1. **Tipo próprio, `Refund` (Estorno).** Estorno **abate despesa; nunca é receita** (`CLAUDE.md`,
   regra 5 vale igual: transferência continua fora de tudo). Valor sempre positivo; o sentido vem
   do tipo.
2. **Contas:** cartão de crédito, conta corrente e carteira. Conta de investimento recusa ("Estorno
   vai para a conta ou o cartão em que a compra foi feita.").
3. **Meio de pagamento:** no cartão, `Credit`; nas contas, qualquer um menos `Credit` (a mesma regra
   do lançamento simples).
4. **Datas.** `PurchaseDate` é a data do estorno. Nas contas, `SettlementDate` é a mesma data. No
   cartão, o estorno entra na **fatura aberta na data do estorno**, pelo mesmo cálculo da compra
   (regra 1 do calculador, `docs/fase-1.md`), e `SettlementDate` é o vencimento dela, **não o da
   fatura da compra original**. É assim que o banco faz: estorno de compra de uma fatura paga
   aparece na seguinte.
5. **Fatura paga não recebe estorno**, como não recebe compra: se a data cair no ciclo de uma fatura
   paga, recusa ("Esta fatura já está paga. Lance o estorno com a data em que ele apareceu no
   cartão.").
6. **Categoria:** opcional; se informada, de despesa. O estorno ligado nasce com a categoria da
   compra, e o usuário pode trocar.
7. **Vínculo (opcional).** `RefundedTransactionId` aponta a compra original, que precisa ser uma
   despesa ativa do mesmo usuário. O estorno ligado fica na **mesma conta** da compra. A soma dos
   estornos ligados a uma compra não passa do valor dela ("O estorno passa do valor da compra.
   Restam R$ 450,00 para estornar."). Na compra parcelada, o limite é o **total da compra** e vale
   para os estornos ligados a qualquer parcela dela.
   Compra que já tem estorno não muda de tipo nem de conta e não fica menor do que já foi estornado
   ("Já foram estornados R$ 150,00 desta compra; o valor não pode ficar abaixo disso.").
8. **Compra parcelada: crédito único.** O estorno é um lançamento só, na fatura da data do estorno;
   as parcelas continuam como estão (cancelar parcelas futuras fica para depois).
9. **Total da fatura** = compras − estornos da fatura. **Total negativo é saldo a favor**: a fatura
   mostra "Saldo a favor R$ X", não aceita pagamento (regra existente: "Não há valor a pagar nesta
   fatura."), fica fora de "Próximas faturas" (regra 2.4 já exige total maior que zero) e **não
   passa o crédito para a fatura seguinte**.
10. **Saldos (regra 2.5 da Fase 1).** Nas contas, o estorno **soma** ao saldo. No cartão, "falta
    pagar" = soma, pelas faturas não pagas, do total de cada uma **contado a partir de zero** (fatura
    com saldo a favor conta zero, pela regra 9); o limite disponível sai desse valor.
11. **Resumo (2.1) e comparativo (2.3), pela data de caixa:** despesas = despesas − estornos do mês;
    "no cartão" = o mesmo, só nos cartões; receitas não mudam; sobra = receitas − despesas líquidas.
    Mês só com estornos tem despesa negativa, mostrada com "−".
12. **Por categoria (2.2):** valor líquido de cada categoria raiz (estorno sem categoria abate "Sem
    categoria"). Categoria com líquido zero ou negativo **some** da lista. A participação (%) passa a
    ser sobre a **soma das categorias exibidas**; quando algo some por causa de estorno, a lista
    avisa ("R$ 150,00 de estornos em categorias sem gastos no mês").
13. **Detalhamento de uma categoria** (2.2): a lista pela data de caixa traz as despesas **e os
    estornos** da categoria; somados com sinal, dão o valor exibido.
14. **Editar:** valor, data, categoria, descrição e meio, como o lançamento simples; no cartão, a data
    nova pode levar o estorno a outra fatura, nunca de ou para uma fatura paga. Conta e vínculo não
    mudam (para trocar, exclua e lance de novo).
15. **Excluir e restaurar:** soft delete, como qualquer lançamento. Restaurar segue as regras de
    criar (fatura paga, limite do vínculo). Excluir a compra original **não** exclui os estornos
    ligados a ela: eles abatem o que o banco de fato devolveu.

Exemplo, hoje 15/10/2026. Itaú (conta corrente, saldo inicial zero), Visa e Master (os dois fecham
dia 26 e vencem dia 5; limites de R$ 5.000,00 e R$ 1.000,00).

| # | Lançamento | Data | Conta | Categoria | Valor | Caixa |
|---|---|---|---|---|---|---|
| 1 | Jantar | 20/09 | Visa | Alimentação › Restaurante | R$ 600,00 | 05/10 |
| 2 | Pagamento da fatura de outubro | 05/10 | Itaú → Visa | | R$ 600,00 | 05/10 |
| 3 | Estorno parcial do jantar (ligado ao 1) | 10/10 | Visa | Restaurante | R$ 150,00 | 05/11 |
| 4 | Sapato | 10/10 | Visa | Compras | R$ 300,00 | 05/11 |
| 5 | Estorno avulso (compra não lançada) | 12/10 | Visa | Compras | R$ 40,00 | 05/11 |
| 6 | Mercado (débito) | 03/10 | Itaú | Alimentação › Mercado | R$ 250,00 | 03/10 |
| 7 | Estorno do mercado (ligado ao 6) | 06/10 | Itaú | Mercado | R$ 50,00 | 06/10 |
| 8 | Fone | 12/10 | Master | Compras | R$ 200,00 | 05/11 |
| 9 | Estorno avulso | 14/10 | Master | Compras | R$ 300,00 | 05/11 |

Resultado:

- **Faturas.** Visa de outubro: R$ 600,00, paga, não muda. Visa de novembro: 300 − 150 − 40 =
  **R$ 110,00**. Master de novembro: 200 − 300 = **saldo a favor de R$ 100,00**; pagar é recusado.
- **Próximas faturas:** só a Visa de 05/11, R$ 110,00; soma R$ 110,00.
- **Cartões:** Visa, falta pagar R$ 110,00 e disponível R$ 4.890,00. Master, falta pagar **R$ 0,00**
  e disponível **R$ 1.000,00** (o saldo a favor não aumenta o limite).
- **Itaú:** saldo atual −600 − 250 + 50 = **−R$ 800,00**.
- **Outubro (caixa):** despesas 600 + 250 − 50 = **R$ 800,00**, das quais R$ 600,00 no cartão.
  Por categoria: Alimentação R$ 800,00 (100%).
- **Novembro (caixa):** despesas 300 + 200 − 150 − 40 − 300 = **R$ 10,00**, todas no cartão. Por
  categoria: Compras 300 + 200 − 40 − 300 = **R$ 160,00 (100%)**; Alimentação fica em −R$ 150,00 e
  some; aviso "R$ 150,00 de estornos em categorias sem gastos no mês" (160 − 150 = 10).
- **Limite do vínculo:** no jantar restam R$ 450,00; um segundo estorno ligado de R$ 500,00 é
  recusado, um de R$ 450,00 é aceito.
- **Fatura paga:** estorno no Visa com data 25/09 cai no ciclo da fatura de outubro, já paga:
  recusado.
- **Parcelada** (cenário à parte): compra de R$ 900,00 em 3x no Master em 12/10 (parcelas de
  R$ 300,00 em 05/11, 05/12 e 05/01) e estorno total ligado em 14/10: um crédito de R$ 900,00 na
  fatura de 05/11; as três parcelas continuam. Um novo estorno ligado a qualquer parcela é recusado
  (restam R$ 0,00).

### Exemplo (usado nos testes)

Outubro de 2026. Conta corrente Itaú, carteira, Tesouro (investimento) e cartão que fecha dia 26 e
vence dia 5.

| Lançamento | Data | Conta | Categoria | Valor | Entra em outubro? |
|---|---|---|---|---|---|
| Salário | 05/10 | Itaú | Salário | R$ 8.000,00 | receita |
| Aluguel (Pix) | 10/10 | Itaú | Moradia › Aluguel | R$ 2.500,00 | despesa |
| Mercado (débito) | 12/10 | Itaú | Alimentação › Mercado | R$ 800,00 | despesa |
| Jantar no cartão | 20/09 | cartão | Alimentação › Restaurante | R$ 600,00 | despesa (fatura vence 05/10) |
| Sapato no cartão | 10/10 | cartão | Compras | R$ 300,00 | não (fatura vence 05/11) |
| Farmácia (Pix) | 15/10 | Itaú | sem categoria | R$ 100,00 | despesa |
| Pagamento da fatura | 05/10 | Itaú → cartão | | R$ 600,00 | não (transferência) |
| Aporte | 06/10 | Itaú → Tesouro | | R$ 1.500,00 | investido |
| Resgate | 20/10 | Tesouro → Itaú | | R$ 500,00 | investido (abate) |
| Saque | 08/10 | Itaú → carteira | | R$ 200,00 | não (transferência) |
| Despesa excluída | 09/10 | Itaú | Lazer | R$ 999,00 | não (excluída) |

Resultado de outubro:

- Receitas **R$ 8.000,00**; despesas **R$ 4.000,00** (2.500 + 800 + 600 + 100), das quais
  **R$ 600,00** no cartão (o jantar); sobra **R$ 4.000,00**; investido **R$ 1.000,00** (1.500 − 500).
- Por categoria: Moradia R$ 2.500,00 (63%); Alimentação R$ 1.400,00 (35%); Sem categoria
  R$ 100,00 (3%).
- Novembro, só com o sapato: despesas R$ 300,00.

### 2.6 Compromissos herdados (etapa 2.8)

Pedida pelo usuário depois da Fase 2. Na visão de caixa, parte do "Saiu" de um mês são parcelas de
compras feitas meses antes: dizer "gastou 20% a mais" mistura o que foi decidido agora com o que foi
decidido em junho. E as parcelas futuras já estão lançadas: dá para mostrar quanto dos próximos meses
já está comprometido.

**Parcelas de compras anteriores** (olha para o mês escolhido no Resumo):

1. **Parcela herdada** do mês é a despesa de uma compra parcelada com vencimento (`SettlementDate`)
   no mês e **número da parcela 2 ou maior**: a compra já tinha cobrado numa fatura anterior. A
   parcela 1 conta como compra do mês, igual a uma compra à vista: é a primeira cobrança dela.
2. **Herdado** é a soma das parcelas herdadas. **Decidido no mês** é o "Saiu" do Resumo (despesas
   menos estornos, 2.1 e 2.5) menos o herdado. Estorno nunca é herdado: abate o decidido no mês.
3. Se o "Saiu" ficar **abaixo** do herdado (estornos grandes), a divisão não é mostrada: só o
   herdado e a lista, sem "decidido no mês" nem participação.
4. **Lista:** cada parcela herdada, com descrição, categoria, conta, "4/10" (número e total de
   parcelas da compra) e valor. Maior valor primeiro; empate pela data da compra mais antiga, depois
   pela descrição.

**Já comprometido** (olha para **hoje**, como as próximas faturas da 2.4):

5. Para cada um dos **6 meses seguintes ao mês de hoje**, o "Saiu" que o Resumo daquele mês já
   mostra hoje: despesas lançadas com vencimento no mês, estornos abatidos. Inclui parcelas e compras
   à vista no cartão que só vencem depois. Mês sem nada lançado aparece com zero.
6. **Última parcela:** o mês de vencimento da última parcela de compra parcelada lançada, depois do
   mês de hoje, mesmo além dos 6 meses. Sem parcela futura, não aparece.

**Para os dois:** transferência (inclusive pagamento de fatura) nunca conta; excluído (soft delete)
fica fora; cada usuário vê só o seu.

#### Exemplo (usado nos testes)

Hoje é 15/10/2026. Visa fecha dia 26 e vence dia 5; Itaú é a corrente.

| Lançamento | Parcelas (vencimento) | Outubro |
|---|---|---|
| TV, R$ 4.000,00 em 10x, 10/06 | jul/2026 a abr/2027 | 4/10, R$ 400,00 herdado |
| Passagem, R$ 3.600,00 em 6x, 20/08 | set/2026 a fev/2027 | 2/6, R$ 600,00 herdado |
| Mercado, R$ 300,00 em 3x, 02/08 | set a nov/2026 | 2/3, R$ 100,00 herdado |
| Tênis, R$ 600,00 em 3x, 15/09 | out a dez/2026 | 1/3, R$ 200,00 do mês |
| Feira no Pix, R$ 250,00, 03/10, Itaú | — | R$ 250,00 do mês |
| iFood, R$ 80,00 à vista, 28/09 (depois do fechamento) | nov/2026 | — |
| Estorno no Visa, R$ 50,00, 08/10 | fatura de nov/2026 | — |
| Pagamento da fatura de outubro, 05/10 | transferência | não conta |
| Relógio, R$ 900,00 em 3x, 20/08, excluído | set a nov/2026 | 2/3 excluída, não conta |

Outubro: "Saiu" **R$ 1.550,00**; herdado **R$ 1.100,00** (Passagem R$ 600,00, TV R$ 400,00, Mercado
R$ 100,00, nessa ordem); decidido no mês **R$ 450,00**; 71% herdado.

Já comprometido, de novembro a abril: **R$ 1.330,00** (TV, Passagem, Mercado 3/3, Tênis 2/3, iFood,
menos o estorno), **R$ 1.200,00**, **R$ 1.000,00**, **R$ 1.000,00**, **R$ 400,00**, **R$ 400,00**.
Última parcela: **abril de 2027** (TV 10/10).

### 2.7 Por que o gasto mudou (etapa 2.10)

Pedida pelo usuário depois da 2.8. O comparativo diz "Gastou 6% a mais que em agosto" e para aí.
A explicação decompõe a diferença do "Saiu" entre o mês escolhido e o anterior em **motivos** que
somam exatamente a diferença. Tudo pela data de caixa, como o resto do Resumo.

1. **Diferença do mês:** "Saiu" do mês menos o "Saiu" do anterior (2.1, estornos abatidos). A
   porcentagem é a do comparativo (2.3): sem gasto no mês anterior, só o valor, sem %.
2. **Motivos**, cada um com a variação em reais:
   - **Parcelas de compras anteriores:** herdado do mês menos herdado do anterior (2.6).
   - **Uma linha por categoria raiz** (e "Sem categoria"): a variação do que foi **decidido no mês**
     naquela categoria, ou seja, o líquido da categoria (despesas menos estornos, 2.5) sem as
     parcelas herdadas. Assim uma compra parcelada que passa de "do mês" para "herdada" aparece
     como queda na categoria e alta nas parcelas, e as duas se compensam.
3. **Soma exata:** Σ motivos = diferença do mês, sempre, ao centavo. Motivo com variação zero não
   aparece.
4. **Ordem e corte:** maior variação em módulo primeiro; empate, alta antes de queda, depois pelo
   nome. No máximo **3 categorias**; as demais viram uma linha "Outras categorias" com a soma, sempre
   a última, para a soma continuar exata. As parcelas não contam no limite das 3.
5. **Detalhe de cada linha:**
   - Categoria que **subiu:** o maior lançamento decidido no mês nela ("o maior foi Show, R$
     180,00"). Categoria que caiu, sem detalhe.
   - Parcelas: as compras cujas parcelas **começaram** a ser herdadas no mês (parcela 2 no mês) e as
     que **terminaram** no mês anterior (a última parcela foi herdada nele).
6. **Sem nada a explicar** (diferença zero e nenhum motivo): "Gastou o mesmo que em agosto", sem
   lista. Mês sem gasto e anterior sem gasto: a explicação não aparece. Diferença que arredonda para
   0%: "Gastou quase o mesmo que em agosto (+R$ 1,00)", com os motivos.
7. Transferência nunca conta; excluído fica fora; cada usuário vê só o seu.

**Por que determinístico, e não um modelo de IA:** a decomposição é aritmética e precisa bater ao
centavo. Um texto gerado por IA, se vier um dia, deve **redigir** estes motivos já calculados, nunca
calcular: a resposta estruturada do endpoint é o contrato.

#### Exemplo (usado nos testes)

Agosto e setembro de 2026, pela data de caixa.

| | Agosto | Setembro | Variação |
|---|---|---|---|
| Parcelas de compras anteriores | TV 2/10, R$ 400,00 | TV 3/10 R$ 400,00 + Passagem 2/6 R$ 600,00 = R$ 1.000,00 | **+R$ 600,00** |
| Lazer (decidido no mês) | Cinema R$ 100,00 + Passagem 1/6 R$ 600,00 = R$ 700,00 | Show R$ 180,00 + Cinema R$ 70,00 = R$ 250,00 | **−R$ 450,00** |
| Saúde | — | Farmácia R$ 150,00 | **+R$ 150,00** |
| Alimentação | Mercado R$ 900,00 + iFood R$ 300,00 = R$ 1.200,00 | Mercado R$ 800,00 + iFood R$ 250,00 = R$ 1.050,00 | **−R$ 150,00** |
| Transporte | Uber R$ 300,00 | Uber R$ 300,00 | 0, não aparece |
| **"Saiu"** | **R$ 2.600,00** | **R$ 2.750,00** | **+R$ 150,00** (6%) |

Motivos, nessa ordem: Parcelas +R$ 600,00 (começou: Passagem); Lazer −R$ 450,00; Saúde +R$ 150,00
(o maior foi Farmácia, R$ 150,00); Alimentação −R$ 150,00 (empata com Saúde em módulo: a alta
vem antes). Soma: 600 − 450 + 150 − 150 = **+R$ 150,00**.


### 2.8 Autocompletar da descrição (etapa 2.15)

Pedida pelo usuário depois da 2.14, com um protótipo aprovado. Ao digitar a descrição em `/lancar`,
o app sugere descrições já usadas, com a categoria, a conta e o meio da última vez. Escrever a mesma
loja sempre do mesmo jeito melhora o logo reconhecido, a análise por categoria e, na Fase 3, as regras.

**O vocabulário** (backend, `GET /transactions/descriptions`): uma requisição por sessão; o filtro
roda no aparelho, sem requisição a cada tecla.

1. Entram despesas e receitas com `PurchaseDate` nos **últimos 12 meses** (de hoje menos 12 meses, pelo
   `IClock`, em diante) e descrição não vazia. Transferência, estorno e excluído ficam fora.
2. **Compra parcelada conta uma vez:** só a parcela 1 entra, com o valor **total** da compra (é o que
   se digita em Lançar). As outras parcelas não inflam a contagem.
3. **Agrupamento:** a chave é a descrição sem espaços nas pontas, com espaços internos colapsados, em
   minúsculas e sem acento ("Farmácia São João" e "farmacia  sao joao" são a mesma). Despesa e receita
   com a mesma descrição são grupos distintos.
4. **Do grupo, vale o lançamento mais recente** (maior `PurchaseDate`; empate, o criado por último):
   a grafia exibida, a categoria, a conta, o meio e o valor. Mais a contagem de lançamentos e a data do
   último uso.
5. **Ordem:** mais usado primeiro; empate, o usado mais recentemente; depois pela descrição. No máximo
   **300** grupos.

**A sugestão** (frontend, função pura):

6. Só aparecem descrições do tipo escolhido (despesa ou receita); estorno e transferência não sugerem.
7. **Correspondência**, sem distinguir acento nem maiúscula, nesta ordem: início da descrição ("if" →
   "iFood"), início de uma palavra ("açu" → "Pão de Açúcar"), qualquer trecho (a partir de 2 letras).
   Desempate pelo uso: contagem × peso da recência (usado há até 30 dias: 2; até 90: 1; mais: 0,5);
   depois o usado mais recentemente; depois pela descrição. No máximo **5**.
8. **Recentes:** com o campo vazio e focado, as **6** de maior uso (a mesma conta de peso), em chips.
9. **Escolher preenche:** a descrição (a grafia do grupo), a categoria, a conta e o meio. **Nunca
   desfaz uma escolha da pessoa:** categoria ou conta que ela já tocou neste lançamento ficam. Conta
   ou categoria que não existem mais (excluída, inativa) não são preenchidas. O **valor nunca é
   preenchido**; o último aparece como dica ("última: R$ 42,90").
10. O texto digitado **nunca é completado nem trocado sozinho**; nenhuma sugestão vem destacada sem a
    pessoa pedir, então Enter com a lista aberta não escolhe por engano.

#### Exemplo (usado nos testes)

Hoje é 25/09/2026. Nubank é o cartão, Itaú a corrente.

| Lançamento | Entra? |
|---|---|
| iFood, despesa, 20/09/2026, Nubank, Crédito, Alimentação, R$ 42,90 | sim |
| ifood (com espaço no fim), despesa, 10/09/2026, Itaú, Pix, Lazer, R$ 35,00 | sim, no mesmo grupo |
| Farmácia São João, despesa, 05/09/2026, Itaú, Pix, Saúde, R$ 64,80 | sim |
| farmacia  sao joao, despesa, 01/08/2026, Itaú, Pix, Saúde, R$ 30,00 | sim, no mesmo grupo |
| Salário, receita, 05/09/2026, Itaú, Pix, Salário, R$ 8.200,00 | sim |
| Notebook, R$ 6.000,00 em 10x no Nubank, 15/03/2026, Compras | uma vez, com R$ 6.000,00 |
| Estorno de iFood, 12/09/2026 | não (estorno) |
| Saque do Itaú para a carteira, 05/09/2026 | não (transferência) |
| Cinema, 14/09/2026, excluído | não |
| Mercado, 24/09/2025 | não (mais de 12 meses) |
| Despesa sem descrição, 18/09/2026 | não |

Vocabulário, nesta ordem:

| Descrição | Tipo | Categoria | Conta | Meio | Último valor | Usos | Último uso |
|---|---|---|---|---|---|---|---|
| iFood | Despesa | Alimentação | Nubank | Crédito | R$ 42,90 | 2 | 20/09/2026 |
| Farmácia São João | Despesa | Saúde | Itaú | Pix | R$ 64,80 | 2 | 05/09/2026 |
| Salário | Receita | Salário | Itaú | Pix | R$ 8.200,00 | 1 | 05/09/2026 |
| Notebook | Despesa | Compras | Nubank | Crédito | R$ 6.000,00 | 1 | 15/03/2026 |

Na tela, em despesa: "if" → iFood; "sao" → Farmácia São João (início de palavra); "note" → Notebook;
"ar" → Farmácia São João (trecho). Em receita, "sa" → Salário e nada de despesa.

---

### 2.9 Fatura alinhada ao banco (etapa 2.20)

**Por quê.** O Prisma prevê em qual fatura cada compra cai, mas o banco decide. Duas coisas escapam
de qualquer previsão (evidência em `docs/validacao-premissas.md`, seção 11): o banco fecha a fatura em
dias que variam de mês para mês, e uma compra feita perto do fechamento pode ser processada dias
depois e cair na fatura seguinte (os dois lanches de sexta, 25/09/2026, à noite, foram para a fatura
de novembro do Itaú, embora a fatura de outubro só fechasse em 27/09). Hoje o Prisma não tem como se
alinhar: editar as datas de uma fatura só muda a data de caixa, e as compras nunca mudam de fatura.
Esta etapa é a base; a regra do cartão "N dias antes do vencimento" (2.21) e o vencimento em dia útil
(2.22) vêm depois e usam o mesmo recálculo.

**Regras**

1. **Quem manda, do mais forte ao mais fraco:** fatura paga (nada entra nem sai, como já é hoje);
   compra presa pela pessoa a uma fatura (regra 2); datas editadas de uma fatura; a previsão pelo
   cartão (`docs/fase-1.md`, 2.1). O recálculo nunca desfaz uma escolha de quem está acima.
2. **Mover uma compra para a fatura seguinte ou anterior.** A compra inteira anda um ciclo: à vista,
   a transação; parcelada, todas as parcelas juntas, cada uma para o ciclo seguinte (ou anterior) ao
   seu. A **data da compra não muda** (`CLAUDE.md`, regra 4); muda a fatura e, com ela, o
   `SettlementDate` (o vencimento da fatura nova). A compra fica **presa**: um recálculo posterior não
   a devolve. Abre a fatura que faltar. Recusada, com mensagem em pt-BR, se alguma parcela está em
   fatura paga ou se alguma cairia numa fatura paga. Vale só para compra no cartão: estorno,
   transferência e lançamento fora do cartão não se movem assim.
3. **Voltar para a fatura calculada solta a compra.** Se a compra movida volta ao ciclo que a
   previsão daria, deixa de estar presa (o "Desfazer" do aviso faz isso). **Mudar a data da compra
   também a solta** (decidido no checkpoint A): ela foi presa por causa da data antiga, e com a data
   nova a previsão volta a valer.
4. **Editar as datas de uma fatura move compras.** Depois da edição, o recálculo reposiciona as
   compras no cartão (e os estornos no cartão, que seguem a data do estorno) que não estão presas nem
   têm parcela em fatura paga: a compra cujo ciclo mudou anda inteira, como na regra 2. Pagamentos de
   fatura (transferências) nunca se movem.
5. **Uma fatura não atravessa as vizinhas.** O novo fechamento tem de ser depois do fechamento da
   fatura anterior e antes do da seguinte; a vizinha que ainda não existe conta com as datas que o
   cartão daria a ela, para a ordem das faturas nunca quebrar. O vencimento continua não podendo ser
   antes do fechamento, e a fatura paga continua sem mudar de datas (essa recusa vem primeiro). Mensagens: "O fechamento tem de ser depois do fechamento da fatura anterior
   (dd/mm)." e "O fechamento tem de ser antes do fechamento da fatura seguinte (dd/mm)."
   **Ordem completa (tarefa 8):** vencimento da anterior < fechamento ≤ vencimento < fechamento da
   seguinte, como todo banco faz. Protege do giro a mais na roda de data do celular: novembro fechando
   em 27/09 (levaria um mês de compras para dezembro) ou vencendo em 04/12 (levaria o caixa de novembro
   inteiro para dezembro, sem nenhuma compra mudar de fatura). Mensagens: "O fechamento tem de ser
   depois do vencimento da fatura anterior (dd/mm)." e "O vencimento tem de ser antes do fechamento da
   fatura seguinte (dd/mm)."
6. **Concorrência.** Pôr ou tirar uma compra de uma fatura conta como mudança nela (token `xmin`,
   como no pagamento): mover uma compra para uma fatura enquanto ela é paga dá 409 para um dos dois,
   e a fatura paga nunca muda de valor.
7. **O resto acompanha sozinho:** o total da fatura, a lista da fatura, o Resumo e a Análise leem o
   `StatementId` e o `SettlementDate`, então mostram a compra no mês novo sem regra própria.
8. **Fora desta etapa:** a semântica do dia do fechamento (a compra do próprio dia hoje entra na
   fatura; no Itaú vai para a seguinte) muda na 2.21, junto com a regra "N dias antes". *(Resolvido na 2.12: o `ClosingDate`
   continua sendo o último dia que entra, e a tela passa a dizer "Compras até".)*

**Exemplos (usados nos testes).** Cartão "fecha 26, vence 5", como o do dono estava cadastrado.

| Caso | Antes | Depois |
|---|---|---|
| Lanches de R$ 30,00 e R$ 25,50 em 25/09/2026, movidos para a seguinte | fatura 2026-10 (fecha 26/09, vence 05/10) | fatura 2026-11 (fecha 26/10, vence 05/11); compra em 25/09; caixa 05/11; presos; o total de 2026-10 cai R$ 55,50; o "Saiu" de outubro cai e o de novembro sobe |
| "Desfazer" de um lanche | fatura 2026-11, preso | volta à 2026-10, solto (regra 3) |
| R$ 100,00 em 3x em 20/09/2026, movida para a seguinte | 2026-10, 2026-11, 2026-12 (3334, 3333, 3333) | 2026-11, 2026-12, 2027-01, mesmos valores, soma R$ 100,00 |
| Mover para a seguinte com parcela em fatura paga | | recusado |
| Fatura 2026-11 editada para fechar em 27/10 | compra de 27/10 na 2026-12 | vai para a 2026-11 (regra 4) |
| Fatura 2026-11 editada para fechar em 24/10 | compra de 25/10 na 2026-11 | vai para a 2026-12 |
| O mesmo, com a compra de 25/10 presa na 2026-11 | | fica na 2026-11 (regra 1) |
| Fatura 2026-11 editada para fechar em 26/09 | | recusado: não é depois do fechamento da 2026-10 (regra 5) |

**Plano de implementação** (fatias verticais; cada tarefa deixa o sistema funcionando, com os testes
escritos antes, por ser regra de data e de dinheiro)

*Fase A: domínio*

- **Tarefa 1. Recálculo único da fatura de cada compra.** *(Feita: `StatementReconciliation`; a criação
  de compra passou a usar a mesma escolha de fatura da edição e do estorno.)* Uma peça do domínio que, dado o cartão, as
  faturas e as compras (com as parcelas), decide a fatura de cada uma seguindo a regra 1, e devolve o
  que mudou e as faturas a abrir. Passa a ser usada no lugar do laço repetido da criação de compra.
  *Aceite:* testes de propriedade (CsCheck): a soma das parcelas nunca muda; as parcelas de uma
  compra ficam em ciclos consecutivos; nada entra nem sai de fatura paga; sem nenhuma edição nem
  compra presa, o resultado é igual ao de hoje (os testes existentes de 1.7, 1.9 e 1.14b continuam
  verdes). *Arquivos:* `Prisma.Domain/Statements` (peça nova), `CardPurchase.cs`, testes em
  `Prisma.Domain.Tests`. *Tamanho:* M.
- **Tarefa 2. Mover compra para a fatura seguinte ou anterior** (regras 2 e 3). *(Feita:
  `CardPurchase.MoveStatement`, `StatementCalculator.ForReference` e a compra presa no recálculo. Até a
  tarefa 4, o EF ignora a compra presa: a coluna ainda não existe.)* *Aceite:* os casos 1
  a 4 da tabela; a data da compra nunca muda; mover e voltar solta a compra. *Arquivos:*
  `Transaction.cs` (compra presa), `CardPurchase.cs`, testes. *Depende de:* 1. *Tamanho:* M.
- **Tarefa 3. Editar datas movendo compras e respeitando as vizinhas** (regras 4 e 5). *(Feita:
  `StatementEditing.EditDates` com o cartão, as faturas e as transações dele, devolvendo o que mudou e
  quantas compras (a parcelada conta uma); a regra 5 em `Statement.EditDates`. O caminho antigo, só
  com o caixa, fica para o `PATCH` até a tarefa 6, que o remove.)* *Aceite:* os
  casos 5 a 8 da tabela; estorno no cartão acompanha; transferência não se move. *Arquivos:*
  `StatementEditing.cs`, `Statement.cs`, testes. *Depende de:* 1. *Tamanho:* M.

*Checkpoint A:* `dotnet test tests/Prisma.Domain.Tests` verde e a suíte inteira sem regressão;
revisão com o dono antes de mexer no banco. *(Em 2026-09-27: 337 de domínio e 504 no total, verdes;
aguardando a revisão. Os lanches só podem ser movidos em produção depois da tarefa 5.)* Achados da
revisão: (1) parcela acrescentada a uma compra presa ia para a fatura calculada, e duas parcelas
caíam na mesma fatura; corrigido: ela segue a última parcela e fica presa (teste em
`CardPurchaseMoveTests`); (2) mudar a data de uma compra presa a deixava presa na fatura da data
nova; decidido que a solta (regra 3), com teste. Checkpoint aprovado pelo dono ("continue").

*Fase B: API e banco*

- **Tarefa 4. Persistir a compra presa.** Coluna nova na tabela de transações, migration e
  configuração, trocando o `Ignore` provisório da tarefa 2 pelo mapeamento. *(Feita: coluna
  `statement_pinned`, migration `AddStatementPinned`, com a trava `ck_transactions_statement_pinned`:
  só despesa no cartão fica presa. Aplicada numa cópia do banco local, com 36 mil transações, todas
  soltas.)* *Aceite:* migration aplica num banco vazio e numa cópia do banco local; transações
  existentes nascem soltas. *Tamanho:* S.
- **Tarefa 5. Endpoint de mover compra.** Recebe a direção (seguinte ou anterior) para uma compra no
  cartão; toca as faturas envolvidas (regra 6); `.http` atualizado. *(Feita: `MoveStatement`,
  `StatementTouch` (o `UpdatedAt` da fatura vai para o UPDATE, que confere o `xmin`), evento de log
  3002, `statementPinned` na resposta das transações; testes em `StatementAlignmentTests`. Sem o toque,
  o teste de corrida falha em todas as rodadas.)* *Aceite (integração):* o cenário
  dos lanches de ponta a ponta (total da fatura 2026-10 e 2026-11, lista da fatura, Resumo de outubro
  e de novembro); parcelada anda inteira; recusa com fatura paga em pt-BR; corrida com o pagamento
  dá 409 e a fatura paga não muda; outro usuário recebe 404. *Depende de:* 2 e 4. *Tamanho:* M.
- **Tarefa 6. Editar datas da fatura com o recálculo.** O `PATCH /statements/{id}` passa a mover
  compras e a aplicar a regra 5 (carregando todas as faturas e transações ativas do cartão); a resposta
  diz quantas compras mudaram de fatura. Remove o caminho antigo do `StatementEditing`. *(Feita:
  resposta com `movedPurchases`, 409 no lugar de corromper a fatura paga, também provado com o toque
  retirado.)* *Aceite
  (integração):* casos 5 a 8 pela API, com o total das duas faturas envolvidas conferido. *Depende de:*
  3 e 4. *Tamanho:* S a M.

*Checkpoint B:* `dotnet test` inteiro verde; o cenário dos lanches conferido pelo `.http`. *(Em
2026-09-27: 518 testes verdes; migration aplicada no banco local; o endpoint conferido numa instância
local. Conferido pelo dono no Postman, na API local: os dois lanches foram para a fatura seguinte,
voltaram com "Previous" e foram de novo com "Next".)*

*Fase C: telas*

- **Tarefa 7. Mover pela tela.** *(Feita: no painel da compra, o título "Na fatura de novembro" e os
  botões "Mover para outubro" / "Mover para dezembro" (verbo + mês de destino, sem setas: "Próxima
  fatura" com seta lia como navegar, achado do dono), lado a lado com a mesma largura e um embaixo do
  outro em 320px; só as direções que a API aceitaria (`statementMove.ts`, com Vitest); aviso com
  "Desfazer" e a nota "Você moveu esta compra para esta fatura." na compra presa. Na lista de
  lançamentos (que segue a data da compra, decisão do dono: o lanche de 25/09 continua em setembro), a
  compra movida ganha uma linha "fatura de novembro", própria para caber inteira em 320px. Conferido em 320, 390 e 1280px, nos dois modos, com parcelada, "Desfazer" e fatura paga.)* Tipos da API regerados; no painel de uma compra no cartão, a fatura
  em que ela está e "Mover para a próxima fatura" / "para a anterior" (escondido se a compra ou o
  destino estiverem em fatura paga); aviso com "Desfazer"; invalidação de faturas, lançamentos,
  saldos e Resumo. *Aceite:* Vitest do que decide as ações disponíveis e dos textos; no navegador,
  os lanches mudam de fatura e o Resumo de outubro e de novembro muda junto. *Tamanho:* M.
- **Tarefa 8. Ajustar datas com retorno.** No "Ajustar datas" da fatura, as mensagens da regra 5 e o
  aviso "N compras mudaram de fatura". *(Feita: aviso com o vencimento, quantas compras mudaram e
  "Desfazer", que volta às datas de antes; a ordem completa da regra 5 no domínio, com testes antes e o
  caso do iPhone pela API; texto de ajuda novo; saldos recarregados após o ajuste. Conferido em 390px
  claro e 320px escuro.)* Achado pelo dono: com o painel aberto, o "Desfazer" do aviso precisava de
  dois toques (o primeiro caía no fundo do painel e o fechava). Corrigido no `BottomSheet` e no
  `Toaster`, para todos os avisos; conferido com toques reais do mouse, não com `element.click()`. *Aceite:* conferido no navegador. *Depende de:* 6. *Tamanho:* S.

*Checkpoint C:* telas nos dois modos e em 320, 390 e 1280px; o dono cadastra (ou já tem) os dois
lanches de 25/09 no cartão Itaú e os move para a fatura de novembro; o total de outubro no Prisma é
comparado com o total da fatura no app do Itaú (as diferenças que sobrarem são compras não lançadas ou movidas).
*(Conferido pelo dono no local em 2026-09-27 e em produção em 2026-09-28: funcionou.)*

**Riscos**

| Risco | Impacto | Como tratar |
|---|---|---|
| Mover para uma fatura sendo paga ao mesmo tempo | Alto (fatura paga mudaria de valor) | Regra 6, com teste de corrida |
| O recálculo único mudar o comportamento de hoje sem querer | Alto (compras antigas mudando de fatura) | Propriedade "sem edição nem presa, igual a hoje" e os testes antigos verdes |
| Editar datas mover mais compras do que a pessoa esperava | Médio | A resposta e o aviso dizem quantas compras mudaram; a regra 5 impede o caso absurdo |
| Compra presa confundir depois ("por que não voltou?") | Baixo | Voltar ao ciclo calculado solta a compra; o painel mostra que ela foi ajustada à mão |

### 2.10 Prévia do ajuste de datas (etapa 2.20b)

**Por quê.** Na 2.20, ajustar as datas de uma fatura move as compras cujo ciclo mudou, e a pessoa só
descobre o efeito depois (aviso com a contagem e "Desfazer" por 8 segundos). Pedido do dono: ver
**antes de salvar** quais compras mudariam de fatura.

**Regras**

1. **Ao vivo, no próprio formulário**, não num diálogo de confirmação: enquanto a pessoa mexe nas
   datas, a tela mostra as compras que mudariam de fatura, cada uma com nome, data da compra, valor e
   "de → para" (meses das faturas). A parcelada aparece uma vez, com o valor total e o mês da primeira
   parcela.
2. **A prévia e o salvar não podem discordar:** a prévia chama a mesma peça do domínio
   (`StatementEditing.EditDates`) sobre dados lidos sem rastreamento, e nada é gravado.
3. **As recusas aparecem antes de salvar:** a mesma mensagem da regra 5 da seção 2.9 surge na hora, e o
   botão de salvar fica desativado.
4. **O botão diz o que vai acontecer:** "Salvar datas" quando nada muda de fatura; "Salvar e mover 1
   compra" / "Salvar e mover N compras".
5. **A prévia é do momento:** se outra compra entrar entre a prévia e o salvar (outro aparelho), o aviso
   depois de salvar conta o que de fato mudou, e o "Desfazer" continua.

**Plano**

- **Tarefa 1. Endpoint de prévia.** *(Feita: `PreviewStatementDates`, com o carregamento compartilhado
  com o `PATCH` em `CardLedger`; a prévia lê sem rastrear. Provado com mutação: se ela copiasse o
  salvar do `PATCH`, o teste acusa a data gravada.)* `POST /statements/{id}/date-preview` com `{ closingDate, dueDate }`:
  200 com as compras que mudariam, ou 400/404 como o `PATCH`. *Aceite (integração, antes do código):* a
  prévia lista exatamente as compras que o `PATCH` depois move; nada muda no banco; recusas em pt-BR;
  fatura paga recusada; outro usuário recebe 404. `.http` atualizado.
- **Tarefa 2. Prévia na tela.** *(Feita: `statementPreview.ts` com Vitest, espera de 300 ms, lista
  esmaecida enquanto recalcula, o "de → para" com a largura toda para não cortar em 320px. Conferido
  com toques reais: a prévia disse 3 compras, e o aviso depois de salvar também.)* Tipos regerados; a lista no "Ajustar datas", com espera curta enquanto se
  digita; o erro no lugar da lista; o botão com o texto da regra 4. *Aceite:* Vitest dos textos e de
  quando pedir a prévia; conferido com toques reais em 320, 390 e 1280px, nos dois modos.

### 2.11 Catálogo de categorias que evolui (etapa 2.23)

**Por quê.** Cada usuário recebe no cadastro uma cópia das 46 categorias padrão (`DefaultCategories`),
e é dono delas: renomeia, recolore e exclui sem afetar ninguém. Modelagem mantida (análise de
2026-09-28: categorias globais exigiriam exceção no filtro de dono e mudariam o histórico de todos).
O problema é evoluir o padrão: uma categoria nova só chegava a quem se cadastrasse depois. Pedido do
pai do dono: "Faltou tipos de seguros, como de vida, residenciais, veicular."

**Regras**

1. **Catálogo versionado.** Cada categoria padrão tem uma chave estável (`expense.insurance.life`,
   nunca renomeada) e a versão em que entrou no catálogo. A versão 1 é o conjunto original; a 2 traz
   **Seguros** (raiz de despesa) com **Vida**, **Residencial** e **Veicular**.
2. **Cada usuário guarda a versão do catálogo que já recebeu.** Quem se cadastra recebe o catálogo
   inteiro e a versão atual; quem já existia está na versão 1.
3. **Sincronização uma vez por versão, para o próprio usuário.** Quando a pessoa está numa versão
   antiga, a lista de categorias acrescenta as categorias das versões novas e sobe a versão dela. Como
   cada versão é aplicada uma vez só, **o que a pessoa excluir não volta**, e o que ela renomear não
   ganha uma cópia com o nome antigo. Nenhuma leitura de outro usuário: o filtro de dono não tem exceção.
4. **Adota o que já existe.** Se a pessoa já criou uma categoria com o mesmo nome no mesmo lugar
   (mesmo tipo e mesma categoria pai), ela recebe a chave em vez de nascer uma duplicada. O mesmo vale
   para carimbar a chave nas categorias originais que ainda têm o nome padrão.
5. **Nunca mexe no que a pessoa personalizou** (nome, cor, ícone) nem nas transações já categorizadas.
6. **Duas abas ao mesmo tempo não duplicam:** índice único de chave por usuário, e a corrida perdida
   só recarrega a lista.
7. **Ícone novo só entra com o mapa do front** (`categoryIcons.ts`): um teste do backend lê o mapa e
   falha se algum ícone do catálogo não estiver nele (antes, o esquecimento só aparecia como o círculo
   tracejado na tela).

**Plano**

- **Tarefa 1. Catálogo no domínio.** *(Feita: `DefaultCategories.Sync` e `Version`, `Category.TemplateKey`,
  `DefaultCategories.Icons` para o teste do mapa; 11 testes novos e 6 mutações pegas. Até a tarefa 2, o
  EF ignora a chave: a coluna ainda não existe.)* `DefaultCategories` com chave e versão por categoria (e uma
  estrutura só por subcategoria, no lugar dos dois arrays paralelos de nome e ícone); `Category.TemplateKey`;
  a sincronização como função pura (categorias ativas + versão do usuário → o que acrescentar, o que
  carimbar, a versão nova). *Aceite (testes antes):* usuário na versão 1 recebe Seguros e as 3
  subcategorias uma vez; na versão atual, nada muda; renomeada não duplica; excluída não volta; nome
  igual é adotado; subcategoria vai para a raiz certa mesmo renomeada; cadastro novo = catálogo
  inteiro e versão atual.
- **Tarefa 2. Banco e API.** *(Feita: migration `AddCategoryCatalog` (versão 1 para quem já existia,
  conferida numa cópia do banco local: 35 usuários na versão 1, nenhuma chave), `CategoryCatalog`
  antes da lista, `CategoryCatalogTests` com 6 casos; 3 mutações pegas, inclusive a corrida.)* Migration: `categories.template_key` (índice único por usuário, entre as
  ativas) e `users.category_catalog_version` (existentes = 1). O cadastro grava a versão atual; o
  `GET /categories` sincroniza quando a versão do usuário é antiga. *Aceite (integração):* usuário
  antigo vê Seguros na primeira lista e só uma vez; exclui Seguros e ela não volta; duas listas ao
  mesmo tempo não duplicam; isolamento; migration numa cópia do banco local.
- **Tarefa 3. Ícones no front.** *(Feita: `shield-check`, `heart-handshake`, `house-plus` e `car-front` no
  mapa; `CategoryIconsTests` (arquitetura) falhou apontando os 4 antes de eles entrarem. Conferido com um
  usuário antigo do banco local: Seguros apareceu na tela de lançar e ele passou para a versão 2.)* Os 4
  ícones novos no `categoryIcons.ts` e o teste do backend que confere o mapa. *Aceite:* conferido na tela de lançar, nos dois modos.

### 2.12 Cartão que fecha N dias antes do vencimento (etapa 2.21)

> **Adiada em 2026-09-28**, por decisão do dono, antes da revisão desta especificação. O "Mover para" e
> o ajuste de datas (2.9) cobrem o erro de previsão enquanto isso. Ao retomar: revisar os textos da tela,
> a regra 5 e qual modelo vem marcado num cartão novo; o plano está em `tasks/plan.md`.
>
> **Evidência nova (2026-09-29, `docs/validacao-premissas.md`, seção 11):** um segundo cartão Itaú do
> dono fechou em 29/09 (vence 07/10, 8 dias antes). No dia 29 o banco já mostrava a fatura fechada e o
> Prisma, aberta: o fechamento do banco é o primeiro dia da fatura seguinte, como diz a regra 2 abaixo.
> Decidir também, ao retomar, se o modelo de dia fixo passa a ler o dia informado assim (exclusivo), e
> como migrar os cartões atuais sem mudar faturas passadas.

**Por quê.** O cartão guarda um dia fixo de fechamento. Os 9 PDFs do Itaú do dono mostram que isso não
representa todos os bancos (`docs/validacao-premissas.md`, seção 11): o Itaú fecha **7 dias antes do
vencimento nominal**, e por isso o dia do fechamento muda com o tamanho do mês. Com "fecha 26, vence 5",
em 7 de 10 meses uma compra do dia 27 cai na fatura errada. Nenhum dia fixo acerta todos os meses.

**O que esta etapa não resolve:** compra que o banco processa dias depois (os lanches de 25/09, seção
2.9). Nenhuma regra prevê isso. O caminho continua sendo o "Mover para".

**Regras**

1. **Dois modelos de cartão.** "Fecha num dia fixo" (como até hoje: `ClosingDay` e `DueDay`) ou
   "fecha N dias antes do vencimento" (`DueDay` e `ClosingDaysBeforeDue`). O cartão tem exatamente um
   dos dois. N vai de **1 a 20**. Quem tiver um banco fora disso poderá avisar no futuro (canal de
   contato, fora desta etapa).
2. **Datas da fatura no modelo novo.** A fatura que vence no mês M vence no **dia do vencimento nominal**
   (`DueDay`, e o dia além do fim do mês vira o último dia, como na regra 2 de `docs/fase-1.md`, 2.1). O
   **melhor dia de compra** é o vencimento nominal menos N dias: a compra desse dia já vai para a fatura
   seguinte. O `ClosingDate` gravado é o **último dia que entra**, o vencimento menos N + 1 dias. Assim a
   regra 1 da fase 1 ("a compra no dia do fechamento entra") vale para os dois modelos. Nada da 2.9 e da
   2.10 muda: recálculo, mover, ajuste de datas, ordem das vizinhas e prévia.
3. **O fechamento sai do vencimento nominal, não do dia útil** (evidência, item 2: fecha 27/09 mesmo com
   o vencimento de 04/10 caindo num domingo). O caixa em dia útil é a 2.22. Até lá, o `SettlementDate`
   é o vencimento nominal.
4. **Os cartões que já existem ficam no dia fixo.** A migration não converte nada. A pessoa escolhe o
   modelo novo editando o cartão.
5. **Mudar a regra do cartão realinha as faturas que ainda não fecharam.** Vale para os dois modelos:
   trocar de modelo, ou trocar o dia ou o N. Até hoje, editar o dia de fechamento não recalculava nada.
   Recebe as datas da regra nova toda fatura **não paga**, **não ajustada à mão** e **ainda não fechada
   pela regra antiga ou pela nova** (último dia hoje ou depois, em `IClock.Today`). Depois o recálculo da
   2.9 reposiciona as compras, na mesma ordem de força: fatura paga > compra presa > datas ajustadas à
   mão > regra do cartão. As faturas fechadas são a história que a pessoa já viu no banco, e não mudam.
   *Por que "pela antiga ou pela nova":* no dia 27/10, a fatura de novembro já está fechada pelo "fecha
   26", mas ainda aberta pelo Itaú. Sem a regra nova na conta, a compra de 27/10 ficaria em dezembro
   justo no dia da troca.
6. **Editar só o nome, o saldo inicial, o limite ou "ativa" não recalcula nada** e não toca nas faturas.
7. **Concorrência.** A troca de regra conta como mudança em cada fatura que ela altera (`StatementTouch`,
   token `xmin`). Pagar uma fatura ao mesmo tempo dá 409 para um dos dois.
8. **Desfazer.** Voltar à regra anterior no mesmo dia devolve as compras às faturas de antes (as mesmas
   faturas são recalculadas, agora pela regra antiga). É o "Desfazer" do aviso.

**A tela não compete com o banco.** Onde o Prisma mostrava "Fechamento" com uma data, passa a mostrar o
que acontece com as compras: **"Compras até 27/10"**. Isso é verdade e combina com o app do banco
("melhor dia de compra 28/10" quer dizer que até o dia 27 a compra entra). Mostrar "Fechamento 27/10" ao
lado de um banco que diz 28/10 faria a pessoa achar que o Prisma errou. Vale para os dois modelos. A
única tela nos termos do banco é o exemplo ao vivo do formulário do cartão, porque ali a comparação é o
objetivo: conferir antes de salvar.

**Exemplos (usados nos testes).**

*Datas do Itaú do dono: vence dia 4, fecha 7 dias antes.* A coluna "Melhor dia de compra" é a dos PDFs
e do app do banco (fevereiro a outubro de 2026 e a previsão de novembro), sem exceção.

| Fatura | Vencimento nominal | Melhor dia de compra | Compras até (`ClosingDate`) |
|---|---|---|---|
| 2026-02 | 04/02/2026 | 28/01/2026 | 27/01/2026 |
| 2026-03 | 04/03/2026 | 25/02/2026 | 24/02/2026 |
| 2026-04 | 04/04/2026 | 28/03/2026 | 27/03/2026 |
| 2026-05 | 04/05/2026 | 27/04/2026 | 26/04/2026 |
| 2026-06 | 04/06/2026 | 28/05/2026 | 27/05/2026 |
| 2026-07 | 04/07/2026 | 27/06/2026 | 26/06/2026 |
| 2026-08 | 04/08/2026 | 28/07/2026 | 27/07/2026 |
| 2026-09 | 04/09/2026 | 28/08/2026 | 27/08/2026 |
| 2026-10 | 04/10/2026 | 27/09/2026 | 26/09/2026 |
| 2026-11 | 04/11/2026 | 28/10/2026 | 27/10/2026 |

*Compras no mesmo cartão (vence 4, 7 dias antes).*

| Compra | Fatura | Caixa |
|---|---|---|
| 27/10/2026 | 2026-11 | 04/11/2026 |
| 28/10/2026 (melhor dia) | 2026-12 (compras até 26/11) | 04/12/2026 |
| R$ 100,00 em 3x em 28/10/2026 | 2026-12, 2027-01, 2027-02 (3334, 3333, 3333) | 04/12, 04/01, 04/02 |
| 24/02/2026 | 2026-03 | 04/03/2026 |
| 25/02/2026 (melhor dia, fevereiro de 28 dias) | 2026-04 | 04/04/2026 |
| 28/12/2026 (melhor dia, virada de ano) | 2027-02 (compras até 27/01/2027) | 04/02/2027 |

*Casos de borda.*

| Cartão | Fatura | Vencimento | Compras até |
|---|---|---|---|
| Vence 4, 7 dias antes | 2028-03 (ano bissexto) | 04/03/2028 | 25/02/2028 |
| Vence 31, 7 dias antes | 2026-02 | 28/02/2026 | 20/02/2026 |
| Vence 31, 7 dias antes | 2026-03 | 31/03/2026 | 23/03/2026 |
| Vence 31, 7 dias antes | 2026-04 (mês de 30 dias) | 30/04/2026 | 22/04/2026 |
| Vence 1, 10 dias antes | 2026-03 (fecha no mês anterior) | 01/03/2026 | 18/02/2026 |
| Vence 1, 10 dias antes | 2026-04 | 01/04/2026 | 21/03/2026 |
| Vence 5, 20 dias antes (o limite) | 2026-03 | 05/03/2026 | 12/02/2026, depois do vencimento da 2026-02 (05/02) |

*Troca de regra (regra 5).* Hoje é **27/10/2026**. O cartão do dono está "fecha 26, vence 5" e passa a
"vence 4, 7 dias antes". Antes da troca: 2026-10 (até 26/09, vence 05/10), 2026-11 (até 26/10, vence
05/11, fechada pela regra antiga) e 2026-12 (até 26/11, vence 05/12). Compras: A, R$ 50,00 em 20/10; B,
R$ 30,00 em 27/10; C, R$ 100,00 em 3x em 20/09.

| Caso | Depois |
|---|---|
| A troca | 2026-10 não muda (fechada pelas duas regras, vence 05/10); 2026-11 passa a até 27/10, vence 04/11; 2026-12 passa a até 26/11, vence 04/12 |
| Compra A | fica na 2026-11; caixa 04/11 |
| Compra B | vai para a 2026-11; caixa 04/11. **1 compra mudou de fatura** |
| Compra C | parcelas na 2026-10 (caixa 05/10), 2026-11 (04/11) e 2026-12 (04/12); não conta como movida |
| O mesmo, com a 2026-11 paga | 2026-11 não muda; B fica na 2026-12 (destino pago) |
| O mesmo, com a 2026-11 ajustada à mão | 2026-11 mantém as datas ajustadas |
| O mesmo, com B presa na 2026-12 | B fica na 2026-12, com o caixa do novo vencimento (04/12) |
| "Desfazer" (volta a "fecha 26, vence 5" no mesmo dia) | 2026-11 volta a até 26/10, vence 05/11; 2026-12 a até 26/11, vence 05/12; B volta à 2026-12 |
| No modelo antigo, "fecha 26" → "fecha 27" (mesmo vencimento) | 2026-11 passa a até 27/10; B vai para a 2026-11 |
| Só o nome muda | nada muda, nenhuma fatura é tocada |
| 21 dias antes, ou os dois modelos, ou nenhum | recusado |

**Mensagens de recusa** (pt-BR, no domínio):

- Nenhum dos dois: "Informe o dia do fechamento ou quantos dias antes do vencimento a fatura fecha."
- Os dois: "Escolha um jeito de fechar: num dia fixo ou dias antes do vencimento."
- Fora do limite: "A fatura deve fechar de 1 a 20 dias antes do vencimento."
- Conta que não é cartão: a mensagem de hoje ("Apenas cartão de crédito tem dia de fechamento e de
  vencimento.").

**Textos da tela** (aprovar antes da tarefa 6; frases curtas, sem ";")

- Formulário, escolha: "Quando a fatura fecha" com "Num dia fixo" e "Dias antes do vencimento".
- Campos do modelo novo: "Vence todo dia" e "Fecha quantos dias antes".
- Exemplo ao vivo, nos termos do banco: "Próxima fatura: melhor dia de compra 28/10 · vence 04/11".
  Logo abaixo: "Confira com o app do seu banco."
- "Como funciona?" (recolhido):
  > Cada banco fecha a fatura de um jeito. Alguns fecham num dia fixo. Outros fecham alguns dias antes
  > do vencimento, e aí a data muda de um mês para outro.
  >
  > No app do seu banco, veja o vencimento e o melhor dia de compra. O exemplo acima deve mostrar as
  > mesmas datas.
  >
  > Se uma compra cair numa fatura diferente da do banco, abra a compra e toque em "Mover para".
- Lista de contas: "Fecha 7 dias antes · vence 4" (o dia fixo continua "Fecha 26 · vence 5").
- Detalhe do cartão: "Fechamento: 7 dias antes do vencimento" (o dia fixo continua "todo dia 26").
- Fatura (painel e fatura atual no detalhe) e campo do ajuste de datas: "Compras até" no lugar de
  "Fechamento", nos dois modelos.
- Aviso depois de salvar uma troca que moveu compras: "1 compra mudou de fatura." ou "N compras mudaram
  de fatura.", com "Desfazer". Sem compra movida, o aviso de sempre.

**Plano** (detalhe e verificação em `tasks/todo.md`)

- Tarefa 0. Esta especificação.
- Tarefa 1. `BillingCycle` (a regra do cartão num lugar só), sem mudar comportamento.
- Tarefa 2. Modelo "N dias antes" no domínio (testes antes: as tabelas acima).
- Tarefa 3. Trocar a regra realinha as faturas (testes antes: a tabela da troca).
- Tarefa 4. Migration, criar e ler cartão no modelo novo.
- Tarefa 5. Editar a regra pela API, com recálculo, corrida e isolamento.
- Tarefa 6. Formulário com os dois modelos, exemplo ao vivo e "Como funciona?".
- Tarefa 7. "Compras até", exibição da regra e aviso com "Desfazer".

### 2.13 Trocar o cartão de uma compra (etapa 2.24)

**Por quê.** Um usuário lançou uma compra no cartão errado e descobriu que o único jeito de corrigir era
excluir e lançar de novo (na parcelada, redigitar tudo). O domínio recusa a troca desde a 1.9b ("Em
compra no cartão, conta, tipo e meio de pagamento não mudam"), como trava de segurança: trocar o cartão
mexe em faturas de dois cartões.

**Escopo:** só de cartão para cartão. Entre cartão e Pix ou débito a compra ganharia ou perderia fatura,
e a parcelada teria de virar à vista: fica para quando alguém pedir.

**Regras**

1. **Trocar o cartão é como trocar a data.** A compra inteira vai para as faturas do cartão novo, pela
   regra dele (`docs/fase-1.md`, 2.1), a partir da data da compra (a nova, se ela mudar junto), abrindo as
   que faltarem. O caixa (`SettlementDate`) passa a ser o vencimento da fatura nova. A data da compra não
   muda por causa da troca. As faturas do cartão antigo perdem a compra, e os totais acompanham sozinhos.
2. **A parcelada troca inteira**, pela edição da compra (`PATCH /installment-purchases/{id}`), com os
   mesmos valores de cada parcela e a mesma soma. A parcela sozinha não troca de cartão, como já não
   troca de valor nem de data: "O cartão de uma parcela muda pela compra inteira."
3. **Fatura paga manda**, dos dois lados. Recusada se alguma parcela está numa fatura paga do cartão
   antigo, ou se alguma cairia numa fatura paga do cartão novo.
4. **Compra com estorno não troca de cartão:** o estorno ficaria no cartão antigo, abatendo uma compra
   que não está mais lá. Vale para a à vista (já recusada hoje, `Refund.CheckPurchaseEdit`) e para a
   parcelada (hoje ela nem recebe a conta).
5. **A compra movida à mão deixa de estar presa** (`StatementPinned`, 2.9): foi presa a uma fatura do
   cartão antigo, que não vale no novo. Como na troca de data (2.9, regra 3).
6. **Só para cartão de crédito.** Outra conta, outro tipo ou outro meio de pagamento continuam
   recusados. Cartão de outro usuário é "Conta não encontrada." (o filtro de dono, como hoje).
7. **Concorrência.** A troca põe e tira compra de faturas: `StatementTouch` nas faturas de origem e de
   destino, e pagar uma delas ao mesmo tempo dá 409 para um dos dois (`CLAUDE.md`, seção 6).
   **Corrige junto uma falha que já existe:** a troca de data na edição da compra (`UpdateTransaction`,
   `UpdateInstallmentPurchase`) também põe e tira compra de fatura e hoje não chama o `StatementTouch`.
8. **O resto acompanha sozinho:** total das faturas, lista da fatura, "Disponível" do cartão, Resumo e
   Análise leem `AccountId`, `StatementId` e `SettlementDate`.

**Exemplos (usados nos testes).** Hoje é 28/10/2026. Visa "fecha 26, vence 5"; Master "fecha 5, vence
12". As duas faturas de destino vencem em meses diferentes, para o teste pegar o caixa.

| Caso | Antes (Visa) | Depois |
|---|---|---|
| R$ 80,00 à vista em 28/10/2026, trocada para o Master | fatura 2026-12 (fecha 26/11, vence 05/12) | Master 2026-11 (fecha 05/11, vence 12/11); caixa 12/11; compra em 28/10; o total da Visa 2026-12 cai R$ 80,00 e o da Master 2026-11 sobe; o "Saiu" de dezembro cai e o de novembro sobe |
| R$ 100,00 em 3x em 28/10/2026, trocada para o Master | 2026-12, 2027-01, 2027-02 (3334, 3333, 3333) | Master 2026-11, 2026-12, 2027-01, caixa 12/11, 12/12, 12/01, mesmos valores, soma R$ 100,00 |
| A de R$ 80,00 trocada para o Master e com a data mudada para 06/11/2026 | | Master 2026-12 (fecha 05/12, vence 12/12); compra em 06/11 |
| A de R$ 80,00, movida à mão para a Visa 2027-01 (presa), trocada para o Master | Visa 2027-01, presa | Master 2026-11, solta |
| Visa 2026-12 paga | | recusado: "Esta compra está numa fatura paga. Desfaça o pagamento para trocar o cartão." |
| Na parcelada, a parcela 1 na Visa 2026-12 paga | | recusado: "Há parcelas em fatura paga. Desfaça o pagamento para trocar o cartão." |
| Master 2026-11 paga | | recusado: "No cartão novo, a compra cairia numa fatura já paga." |
| A de R$ 80,00 com um estorno de R$ 20,00 | | recusado: "Esta despesa tem estornos; o tipo e a conta não mudam." (à vista e parcelada) |
| Trocada para a conta corrente | | recusado: "Compra no cartão só troca para outro cartão. Para usar outra conta, exclua e lance de novo." |
| Tipo ou meio de pagamento diferente | | recusado: "Em compra no cartão, tipo e meio de pagamento não mudam. Exclua e lance de novo." |
| Pela parcela 2, pedindo o Master | | recusado: "O cartão de uma parcela muda pela compra inteira." |
| Cartão de outro usuário | | recusado: "Conta não encontrada." |
| Troca para o Master enquanto a Visa 2026-12 é paga | | um dos dois recebe 409; a fatura paga não muda de valor |
| Troca de data (mesmo cartão) enquanto a fatura de destino é paga | | um dos dois recebe 409 (a falha da regra 7) |

**Tela**

- No formulário da compra à vista no cartão e no da compra parcelada, uma seção **"Cartão"** com os
  cartões ativos e o cartão atual da compra (mesmo inativo), como a seção "Conta" do lançamento fora do
  cartão.
- A nota muda de "Conta e meio de pagamento não mudam: para trocá-los, exclua e lance de novo." para
  "Mudar a data ou o cartão leva a compra para a fatura do novo ciclo."
- Depois de salvar uma troca de cartão, o aviso diz para onde a compra foi: **"Agora no Master, na
  fatura de novembro."** Sem "Desfazer": como nas outras edições, para voltar a pessoa edita de novo.

**Plano**

- Tarefa 1. *(Feita em 2026-09-28: `CardPurchaseCardChangeTests` com 15 casos, 14 falhando antes do
  código (o do estorno já valia); 10 mutações pegas; a `StatementPlacement` passou a olhar só as faturas
  do cartão, porque na troca as dos dois cartões vêm juntas e a mesma referência existe nos dois. O
  `CardPurchasesTests` da linha 195 já espera a mensagem nova.)* Domínio, testes antes: à vista (`CardPurchase.EditTransaction`) e parcelada
  (`CardPurchase.Edit`) trocam de cartão; `Transaction` e `InstallmentPurchase` ganham a troca interna
  de `AccountId`. Os dois testes que afirmam a recusa (`CardTransactionUpdateTests`,
  `CardPurchaseDateTests`) passam a recusar só tipo, meio e conta que não é cartão.
- Tarefa 2. *(Feita em 2026-09-28: `CardChangeTests` com 9 casos, inclusive 3 corridas; o
  `AccountId` da parcelada é opcional, para a tela aberta antes do deploy da 2.24 continuar editando sem
  trocar o cartão; evento de log 3003 `PurchaseMovedToCard`, só com ids; o `StatementTouch` entrou também na troca
  de data do estorno, no mesmo handler (sem teste de corrida próprio). 6 mutações pegas: faturas do cartão
  novo nas duas edições, estorno e cartão novo na parcelada, e o `StatementTouch` nas duas, com a corrida
  da troca de data falhando sem ele, o que confirma a falha antiga.)* API: `UpdateTransaction` e `UpdateInstallmentPurchase` (o `Request` ganha `AccountId`)
  carregam as faturas do cartão novo, conferem o estorno e chamam o `StatementTouch` (também na troca
  de data). Integração: os exemplos acima de ponta a ponta, as recusas, as duas corridas e o isolamento;
  `CardPurchasesTests` (linha 195) passa a esperar a recusa de conta que não é cartão. `api-types.ts`,
  `cards.http` e `transactions.http`.
- Tarefa 3. *(Feita em 2026-09-28: `cardChange.ts` com Vitest (5 casos, falhando antes do módulo); seção
  "Cartão" só quando há outro cartão para escolher; conferida com toques reais contra a API nova: à vista a
  390px claro, parcelada a 320px escuro, recusa do estorno a 1280px claro, sem rolagem lateral.)* Tela: seção "Cartão", nota e aviso, com Vitest do texto do aviso e da lista de cartões.
  Conferida em 320, 390 e 1280px, claro e escuro, com toques reais.

### 2.14 Lançamentos que se repetem (etapa 2.25)

**Por quê.** Pedido do dono (2026-09-29): cadastrar uma vez o que se repete. Dois casos reais: o Pix
de todo mês (aluguel, diarista) e a cobrança que uma empresa faz todo mês no cartão (o serviço do pet do
pai do dono: R$ 400,00 todo dia 25, como uma compra comum, paga na fatura seguinte). No Prisma nada é
pago de verdade: a recorrência é a representação financeira do que o banco vai fazer.

**Nomes.** Não é "débito recorrente": débito é meio de pagamento em conta. No cartão é uma **cobrança
recorrente** (o mercado também diz "compra recorrente" ou "assinatura"). No banco existem o Pix Agendado
Recorrente (a pessoa programa) e o Pix Automático (a empresa cobra com autorização); para o Prisma os
dois são iguais. Na tela, a série diz **"se repete"** e a frequência ("Todo mês", "Toda semana"), nunca o
verbo "Repetir" sozinho: o painel já tem "Lançar de novo" (copiar uma vez, `docs/fase-1.md`, 5.1). No
código: `Recurrence`.

**Escopo da primeira versão (decidido com o dono).** Despesa e receita fora do cartão (Pix, boleto, TED,
débito, dinheiro) e cobrança no cartão (compra à vista). Frequências: toda semana e todo mês. Fora: a
transferência recorrente entre contas da pessoa (aporte mensal), quinzenal, anual, "termina depois de N
vezes" e compra parcelada que se repete.

**Decisões de arquitetura** (recomendadas na investigação de 2026-09-29, aceitas pelo dono):

- **A1. Gerar só no dia; o futuro é previsão calculada.** Cada ocorrência vira lançamento de verdade no
  dia dela; nada futuro é gravado. Diferente da parcela, que é dívida já contraída, a recorrência é
  intenção: gerar antes poria no "Saiu" e no "Já comprometido" o que não aconteceu, e cada edição ou
  cancelamento teria de corrigir lançamentos futuros.
- **A2. Uma abstração, dois destinos.** Fora do cartão, a ocorrência é um lançamento simples
  (`Transaction.CreateSimple`); no cartão, uma compra à vista (`CardPurchase.Create`), com a fatura pela
  regra de sempre (`StatementPlacement`). Nenhuma regra nova de fatura.
- **A3. Geração que alcança o atraso.** "Gerar toda ocorrência depois de `generated_through` e até hoje":
  não importa quando nem quantas vezes roda. O lançamento e o avanço de `generated_through` gravam na
  mesma transação; o índice único (recorrência, data da ocorrência), que conta também os excluídos, é a
  segunda trava. Rodar de novo, duas execuções ao mesmo tempo ou cair no meio nunca duplicam.
- **A4. `BackgroundService` no próprio processo**, ao subir a API e de hora em hora (`PeriodicTimer`),
  além de gerar na hora ao criar ou editar a série. Sem Hangfire nem Quartz (dependência nova, tabelas
  próprias; retentativa e trava distribuída já saem da A3). Rever se surgirem várias tarefas agendadas
  diferentes (e-mail, importação).
- **A5. A tarefa age como cada usuário:** um escopo de DI por usuário, com o usuário atual fixo nele. O
  filtro de dono e a trava de gravação continuam valendo (`CLAUDE.md`, regra 6), sem exceção.
- **A6. Falha isolada.** Cada série grava sozinha; a que falha não trava as outras, vai para o log (só
  ids) e tenta de novo na próxima hora, porque `generated_through` não andou.

**Regras**

1. **A série nasce de um lançamento.** No "Novo lançamento", ou a partir de um lançamento que já existe
   (o caso do pai: abre a cobrança de 25/09 e diz que ela se repete todo mês). Esse lançamento é a
   **primeira ocorrência**: fica ligado à série, e `generated_through` é a data dele. Não há ocorrência
   retroativa: a série continua dali para frente, e o que a pessoa já lançou à mão nunca duplica.
2. **O modelo** guarda conta, tipo (despesa ou receita), valor, categoria, descrição, meio de pagamento,
   frequência, data de partida (a do primeiro lançamento) e término opcional ("nunca" ou uma data). Não
   vale para estorno, transferência nem compra parcelada.
3. **Agenda.** Toda semana: partida + 7 dias × k. Todo mês: o dia da partida em cada mês; o dia que o mês
   não tem vira o último dia (como `docs/fase-1.md`, 2.1, regra 2), sempre calculado **a partir da
   partida**, nunca da ocorrência anterior (dia 31 → 30/11 → 31/12, e não 30 para sempre). Fim de semana
   e feriado **não mudam a data**: o Pix funciona todos os dias, e a cobrança no cartão é uma compra. O que
   vai para o dia útil é o vencimento da fatura (2.22) e o débito automático (2.26, seção 2.15).
4. **"Hoje" é `IClock.Today`** (São Paulo). Uma ocorrência é gerada quando a data dela é hoje ou antes.
5. **O lançamento gerado é um lançamento comum**, com a origem (`recurrence_id`) e a data da ocorrência
   (`occurrence_date`) guardadas à parte: a pessoa edita, exclui, restaura, troca de cartão ou move de
   fatura como qualquer outro, e mudar a data dele não o desliga da série. **Excluído não volta.**
6. **Editar a série vale do próximo em diante.** Mudam valor, descrição, categoria, conta (cartão por
   cartão, conta por conta), frequência com a nova próxima data (depois de `generated_through`) e término.
   O que já foi gerado fica como está.
7. **Encerrar** põe o término no último lançamento já gerado: nada mais é gerado, nem a ocorrência de
   hoje que a tarefa ainda não gerou (decidido na tarefa 2), e a série vai para "Encerradas". O que já foi
   gerado fica, porque é histórico.
8. **Conta ou cartão inativo:** as ocorrências desse período não são lançadas nem ficam pendentes
   (`generated_through` anda); a série volta a gerar quando a conta volta a ser ativa. Categoria excluída:
   o lançamento sai sem categoria. **Conta excluída** (decisão do dono, 2026-09-29): só conta vazia pode ser
   excluída, e as séries ativas dela encerram junto, na mesma gravação (o término vai para o último gerado;
   a série que já terminou fica como está); as pendências dela saem. Sem isso, a série ficaria "ativa" sem
   conta, sem gerar nada. Encerrar, e não apagar: o que ela lançou é histórico. A confirmação avisa quantas
   ("As 2 recorrências dela serão encerradas."), e o aviso do fim também.
9. **Fatura paga é intocável** (regra 4 do `CLAUDE.md`, a mais forte). Uma ocorrência no dia certo nunca
   cai numa fatura paga: fatura só é paga depois do fechamento (`Transfer.PayStatement`), e a compra de
   hoje vai para uma fatura que fecha hoje ou depois. Só acontece quando a geração atrasa (API fora do ar)
   ou quando as datas da fatura foram ajustadas para trás. Aí a ocorrência **não é lançada e fica
   pendente**, visível, com o motivo, o cartão e o valor do dia (uma edição posterior da série não a muda,
   regra 6), e três saídas: lançar na fatura seguinte (presa, como o "Mover
   para"), lançar depois de desfazer o pagamento, ou descartar. A pendência não trava as ocorrências
   seguintes.
10. **Concorrência.** A série tem `xmin` (editar a série enquanto a tarefa gera: um dos dois recebe 409).
    A ocorrência que entra numa fatura chama o `StatementTouch` nela, como na 2.20.
11. **Previsão**, sempre calculada e nunca gravada: as ocorrências **depois de `generated_through`**
    (nem antes, para não contar duas vezes, nem a partir de amanhã, para não haver buraco entre a
    meia-noite e a próxima execução). No cartão, o mês da previsão é o do vencimento da fatura em que ela
    cairia (`StatementPlacement`, sem abrir fatura).
    - **"Daqui para frente"** (Análise): em cada um dos 6 meses, a barra ganha a parte prevista das
      despesas que se repetem, na mesma cor, mais clara, e o texto "R$ 2.400 · R$ 400 previstos". O "Já
      comprometido" continua sendo só o que existe.
    - **Fatura aberta do cartão**: abaixo do total, "Previsto até o fechamento: + R$ 400,00", com as
      cobranças que ainda cairão nela. O total nunca muda com a previsão.
    - **Resumo e saldos não mudam**: continuam mostrando só o que aconteceu (`CLAUDE.md`, 7.1). O saldo
      previsto da conta com as recorrências fica para depois.

**Tela**

- **"Novo lançamento":** logo depois de Data, uma linha compacta: "↻ Não se repete ›". Com uma
  frequência escolhida, ela diz a série: "Todo mês, no dia 25 · próxima 25/10". A data do lançamento
  define a série. Ao tocar, um painel com duas escolhas: frequência (Não se repete, Toda semana, Todo mês)
  e término (Nunca, Em uma data). O botão continua "Lançar R$ 400,00". A linha não aparece em estorno e
  transferência, e some quando a compra no cartão é parcelada.
- **Painel de um lançamento:** a ação "Se repete todo mês…" (ou semana), que abre o mesmo painel.
- **Excluir um lançamento de uma série ativa pergunta antes** (decisão do dono, 2026-09-29, depois de testar):
  "Excluir e encerrar a série" ou "Excluir só este". Excluir sozinho não para a série (regra 5), e quem
  exclui pode achar que parou tudo; um aviso que some em 8 s passaria despercebido. Exclui primeiro e só
  então encerra: se a exclusão é recusada (fatura paga), a série fica como estava. O "Desfazer" volta o
  lançamento e reabre a série com o término de antes. "Excluir só este" avisa que a série continua, com a
  próxima data.
- **"Recorrências"**, aberta pela aba Contas ("Recorrências · 3 ativas") e pela previsão do "Daqui para
  frente": cada série com valor, conta, frequência e a próxima data; editar e encerrar; as pendências no
  topo; as encerradas no fim.

**Exemplos (usados nos testes).** Visa "fecha 26, vence 5"; conta corrente Itaú.

*Agenda.*

| Série | Ocorrências |
|---|---|
| Pet: R$ 400,00 todo mês, partida 25/09/2026, no Visa | 25/10 (domingo, não muda), 25/11, 25/12… |
| Aluguel: R$ 1.000,00 todo mês, partida 10/09/2026, Pix no Itaú | 10/10 (sábado, não muda), 10/11, 10/12… |
| Diarista: R$ 500,00 toda semana, partida 02/10/2026 (sexta), Pix | 09/10, 16/10, 23/10, 30/10, 06/11… |
| Todo mês, partida 31/10/2026 | 30/11/2026, 31/12/2026, 31/01/2027, 28/02/2027, 31/03/2027 |
| Pet com término 30/11/2026 | 25/10 e 25/11; 25/12 não |

*Geração (relógio falso).*

| Caso | Resultado |
|---|---|
| Hoje 24/10 | Nada do pet |
| 25/10 às 00:30 em São Paulo (03:30 UTC) | Gera o pet de 25/10: compra de R$ 400,00 no Visa, fatura 2026-11 (fecha 26/10), caixa 05/11; `generated_through` = 25/10 |
| 24/10 às 23:30 em São Paulo (25/10, 02:30 UTC) | Nada: ainda é dia 24 |
| A mesma execução duas vezes, ou duas ao mesmo tempo | Um lançamento só |
| API fora do ar de 24/10 a 27/10, volta em 28/10 | Gera o pet de 25/10, uma vez; a diarista de 23/10 já tinha sido gerada no dia |
| A pessoa exclui o pet de 25/10 | Nenhuma execução o recria; restaurar o traz de volta |
| A pessoa muda a data do pet de 25/10 para 26/10 | Continua ligado à série; nada é gerado de novo |
| Valor da série muda para R$ 450,00 em 01/11 | 25/11 sai com R$ 450,00; o de 25/10 continua R$ 400,00 |
| Encerrada em 01/11 | Nada depois; o de 25/10 fica |
| Visa inativo de 20/10 a 30/10 | O de 25/10 não é lançado nem fica pendente; o de 25/11 é |
| Fatura 2026-11 do Visa paga em 27/10, e a geração só roda em 28/10 | O pet de 25/10 fica pendente: "Cairia na fatura de novembro, já paga." Lançar na seguinte: fatura 2026-12 (fecha 26/11, vence 05/12), presa. O de 25/11 é gerado normalmente |
| Criada a partir da cobrança de 25/09 | A de 25/09 vira a primeira ocorrência; a próxima é 25/10; nada é lançado para trás |

*Previsão.* Hoje 20/10/2026, com o pet e o aluguel.

| Onde | Mostra |
|---|---|
| "Daqui para frente", novembro | R$ 1.400 previstos: o pet de 25/10 (vence 05/11) e o aluguel de 10/11 |
| "Daqui para frente", dezembro | R$ 1.400 previstos: o pet de 25/11 (vence 05/12) e o aluguel de 10/12 |
| Fatura aberta do Visa (2026-11, fecha 26/10) | "Previsto até o fechamento: + R$ 400,00" |
| O mesmo em 25/10, depois da geração | O pet já está no total; a previsão some (nada contado duas vezes) |
| Resumo de outubro e saldo do Itaú | Só o que aconteceu |

**Plano** (detalhe em `tasks/`, ao começar; cada tarefa com testes antes, por ser regra de data e de dinheiro)

- Tarefa 1. *(Feita em 2026-09-29: 15 testes, 5 mutações pegas.)* Agenda no domínio (`RecurrenceSchedule`): as tabelas de agenda, e propriedade (CsCheck): a
  série mensal nunca escorrega de dia, nunca repete nem pula mês.
- Tarefa 2. *(Feita em 2026-09-29: 21 testes, 7 mutações pegas; resolver a pendência ficou para a
  tarefa 5.)* A série no domínio (`Recurrence`): criar a partir de um lançamento, ocorrências vencidas,
  editar dali em diante, encerrar, conta inativa, pendência de fatura paga.
- Tarefa 3. *(Feita em 2026-09-29: migration `AddRecurrences`, `RecurrenceRunner` com `ScopedUser`, 9
  testes de integração; o `xmin` da série fica provado na tarefa 5.)* Tabela, migration e o gerador (uma classe chamada direto nos testes): idempotência (duas
  vezes, ao mesmo tempo, atraso, excluído que não volta), escopo por usuário, `StatementTouch`.
- Tarefa 4. *(Feita em 2026-09-29: `RecurrenceWorker` roda ao subir e a cada hora; falha vai ao log (3009) e a
  próxima hora tenta de novo; `Recurrences:Runner:Enabled`, ligado por padrão; 4 testes, 3 mutações pegas.)*
  `BackgroundService` de hora em hora, com log (só ids), desligado nos testes de integração.
- Tarefa 5. *(Dividida em 5a e 5b. 5a feita em 2026-09-29: criar junto do lançamento e a partir de um
  existente, listar, editar, encerrar; gera na hora; o `xmin` da série provado na corrida encerrar × gerar;
  toque duplo em "se repete" dá 409 pelo `recurrence_id` como token de concorrência; 10 testes, 2 mutações
  pegas. 5b feita no mesmo dia: lançar na fatura seguinte, presa; lançar
  na própria depois de desfazer o pagamento; descartar. A pendência guarda o cartão e o valor do dia em que
  venceu, porque a edição da série vale do próximo em diante; 14 testes, 8 mutações pegas.)* API: criar junto do lançamento e a partir de um existente, listar, editar, encerrar e
  resolver pendências.
- Tarefa 6. *(Feita em 2026-09-29: a linha e o painel no Novo lançamento, a ação "Se repete todo mês…" no
  painel do lançamento, com as ocorrências que já passaram avisadas antes de salvar; 16 testes, 5 mutações
  pegas.)* Tela: a linha "se repete" no "Novo lançamento" e a ação no painel do lançamento.
- Tarefa 7a. *(Feita em 2026-09-29, pedida pelo dono depois de testar: excluir o lançamento de uma série
  ativa pergunta antes, "Excluir e encerrar a série" ou "Excluir só este"; ver "Tela".)*
- Tarefa 7. *(Feita em 2026-09-29: `/contas/recorrencias`, com as pendências no topo e as três saídas,
  editar do próximo em diante, encerrar com "Desfazer" e a linha na aba Contas.)* Tela: "Recorrências" (lista, edição, pendências).
- Depois da tarefa 8, antes do commit (2026-09-29): lacunas de teste fechadas (data mudada e restauração
  de lançamento gerado, série movida para outro cartão, previsão com datas editadas, o que o Novo lançamento
  manda) e a conta excluída encerrando as séries (regra 8). As três migrations da etapa viraram uma,
  `AddRecurrences`, antes do commit (nenhuma tinha ido para produção). Etapa concluída.
- Tarefa 8. *(Feita em 2026-09-29: `RecurrenceProjection` no domínio, `projectedExpenseCents` no "Já
  comprometido" e `projectedCents` na fatura; a linha só aparece na fatura aberta que já existe.)* Previsão no "Daqui para frente" e na fatura aberta do cartão.

### 2.15 Débito automático (etapa 2.26)

**Por quê.** Pedido do dono (2026-09-29): cadastrar as contas de consumo pagas em débito automático (luz,
água, gás, escola; qualquer empresa) e, no dia do débito, conferir o valor. A série da 2.25 tem valor
fixo, e a conta de luz muda todo mês. O débito automático é um conceito de fato, com nome próprio, e
virão análises só dele ("quanto vai em débito automático").

**Nomes.** Débito automático é o banco tirar da conta corrente, no vencimento, o valor que a empresa
cobra, sem a pessoa agir. Não é a cobrança recorrente no cartão (Netflix; isso é a 2.25) nem o pagamento
automático da fatura do cartão (pendências de produto do `PLAN.md`). Na tela: **"Débito automático"**. No
código: `RecurrenceKind.AutoDebit`, com o valor que muda em `AmountVaries`; o lançamento com valor a
conferir em `Transaction.AmountEstimated`.

**Escopo (decidido com o dono).** Só despesa, só em conta corrente. Frequência mensal. O calendário
bancário entra nesta etapa, primeiro, porque o débito cai no próximo dia útil; a 2.22 usa o mesmo
calendário nas faturas depois. A estimativa é só digitada (sem média calculada). Fora: filtro
"Débito automático" na lista de lançamentos e análises (o tipo gravado na série já permite), conferir
antes do dia, feriado estadual e municipal, aviso fora do app (push, e-mail).

**Decisões**

- **D1. Um tipo de série, não uma entidade nova.** Agenda, geração que alcança o atraso, `xmin`,
  previsão e isolamento da 2.25 servem igual (2.14, A1 a A6). Duas entidades repetiriam tudo isso e
  divergiriam com o tempo. O conceito existe no domínio, na API e na tela pelo tipo da série.
- **D2. Débito automático não é o mesmo que valor variável.** A academia em débito automático cobra
  sempre o mesmo valor e não precisa de conferência; a conta de luz muda. O aviso vem do "o valor muda",
  não do tipo.
- **D3. Lança com a estimativa e marca para conferir.** O dinheiro sai de fato no dia; só o valor é
  incerto. O lançamento existe, conta no saldo e no "Saiu", e fica visivelmente aproximado até a pessoa
  conferir. Não lançar (esperar a confirmação) faria o esquecimento sumir com a despesa inteira: um erro
  plausível e silencioso, o pior tipo (`CLAUDE.md`, seção 5).
- **D4. O aviso é o que falta conferir, não uma caixa de notificações.** Não há tabela de avisos nem
  estado de lido: o sino conta os lançamentos com `AmountEstimated`, e cada um sai quando é conferido ou
  excluído. Sem "marcar todas como lidas" nem "confirmar todos": os dois deixariam passar a estimativa
  como valor real sem ninguém olhar. Se um dia houver aviso só informativo, é a hora de uma tabela.
- **D5. Calendário bancário puro, com dois usos só.** Uma classe no domínio (`BankCalendar`), sem banco
  nem tabela de feriados: os fixos e os móveis calculados a partir da Páscoa. Só duas coisas dependem
  dele: o débito automático (esta etapa) e o vencimento da fatura (2.22). Pix, débito, boleto, TED,
  dinheiro, compra no cartão, parcela, estorno, transferência e a recorrência comum ficam na data em que
  aconteceram. Um teste de arquitetura recusa outro tipo que dependa do calendário.

**Regras do calendário**

1. **Dia útil** é o que não é sábado, domingo nem feriado nacional.
2. **Feriados fixos:** 01/01, 21/04, 01/05, 07/09, 12/10, 02/11, 15/11, 20/11 (nacional desde 2024) e
   25/12.
3. **Feriados móveis**, a partir da Páscoa (P): Carnaval na segunda e na terça (P − 48 e P − 47),
   Sexta-feira Santa (P − 2) e Corpus Christi (P + 60). A Quarta-feira de Cinzas é útil. Corpus Christi
   não é feriado em lei, mas os bancos não abrem (evidência: o Itaú o tratou como não útil em
   04/06/2026, `docs/validacao-premissas.md`, seção 11).
4. **24/12 e 31/12 são úteis** até aparecer evidência de débito adiado nesses dias.
5. **Próximo dia útil** (`BusinessDayOnOrAfter`): o próprio dia, se útil; senão o primeiro útil depois.
6. Feriado estadual e municipal (09/07 em São Paulo, por exemplo) ficam de fora: um débito nesse dia
   aparece um dia antes do real. É a mesma limitação da 2.22.

**Regras do débito automático**

1. **É uma série da 2.25 com o tipo `AutoDebit`.** Valem as regras 1 a 8, 10 e 11 da seção 2.14, com o
   que muda abaixo. A 9 (fatura paga) não se aplica: não há cartão.
2. **Só despesa, só conta corrente, só mensal.** O meio de pagamento é `Debit`, sem escolha. Recusas:
   "Débito automático só sai de conta corrente." e "Débito automático é sempre despesa." A frequência
   semanal não é oferecida.
3. **Vencimento e data do débito.** A agenda (2.14, regra 3) dá o **vencimento**, guardado como data da
   ocorrência (`OccurrenceDate`). O lançamento sai na **data do débito**: o próximo dia útil a partir do
   vencimento (calendário, regra 5), em `PurchaseDate` e em `SettlementDate`. A ocorrência é gerada
   quando a data do débito é hoje ou antes; `generated_through` continua contando vencimentos. Como o
   próximo dia útil nunca volta no tempo, a ordem das ocorrências se mantém.
4. **Vencimento da primeira ocorrência.** A série nasce de um lançamento (2.14, regra 1), cuja data pode
   já ser um dia útil adiado (Copel de 20/09/2026, domingo, debitada em 21/09). O painel pergunta o
   vencimento, preenchido com a data do lançamento; a pessoa pode escolher um dia até 7 dias **antes da
   data do lançamento** (não de hoje), a folga para o maior adiamento real, uns 4 dias (sábado antes do
   Carnaval até a Quarta de Cinzas). A série parte desse vencimento; o lançamento fica com a data dele.
   Lançamento no passado vale como na 2.25: débito do dia 10 cadastrado no dia 25 parte do dia 10, e o
   próximo é o do mês seguinte.
5. **Valor que muda** (`AmountVaries`). O valor da série é a **estimativa**. Cada ocorrência gerada sai
   com ela e com `AmountEstimated = true`. Sem "o valor muda", o débito sai como um lançamento comum,
   sem conferência.
6. **Conferir.** Confirmar mantém o valor e tira a marca. Informar outro valor grava o valor real e tira
   a marca, na mesma ação. Editar o valor do lançamento pelo painel comum também confere; editar só
   categoria ou descrição não. Conferir não muda a estimativa da série (2.14, regra 6): quem quer outra
   estimativa edita a série. O primeiro lançamento da série é o que a pessoa digitou: não precisa de
   conferência.
7. **O aviso aparece a partir do dia do débito** (é quando o lançamento nasce) e fica até ser conferido.
   Um mês sem conferir não impede o seguinte de ser gerado; cada lançamento é conferido por si. Excluir
   tira o lançamento da lista; restaurar o traz de volta ainda a conferir.
8. **Números aproximados ficam visíveis.** O lançamento a conferir mostra "≈" antes do valor e "a
   conferir"; o "Saiu" do Resumo, quando houver, "≈ R$ X a conferir". O total não muda por isso: a
   estimativa conta até ser conferida.
9. **Previsão** (2.14, regra 11): as ocorrências futuras entram no mês da **data do débito**, com a
   estimativa.
10. **Mudar o tipo da série** vale do próximo em diante (2.14, regra 6). Virar débito automático exige
    conta corrente e despesa; deixar de ser volta a gerar na data da agenda. O que já foi gerado fica como
    está, inclusive a marca de conferir.

**Tela**

- **"Novo lançamento" e o painel "se repete"** (2.25): numa despesa em conta corrente com "Todo mês", a
  opção "É débito automático". Marcada, aparecem "O valor muda a cada mês" e "Vence dia", preenchido
  com a data do lançamento (regra 4). Com "o valor muda", o rótulo do valor vira "Valor médio". Em cartão,
  dinheiro, investimento ou receita a opção não aparece.
- **O sino**, no cabeçalho de todas as telas, ao lado do tema. Com débitos a conferir, mostra a
  quantidade; sem nenhum, fica sem número, e tocar diz "Nada para conferir.". *(Depois do checkpoint, a
  pedido do dono, 2026-09-30: o número ganha o anel do espectro, o mesmo do avatar, e o sino balança uma vez,
  por 450 ms, quando a contagem cresce, nunca ao abrir a tela; sem movimento no sistema, só o número muda. A
  lista do sino volta ao servidor ao voltar para o app, se tiver mais de um minuto: antes, com o app aberto, o
  débito gerado de madrugada só aparecia depois de recarregar a página. O dono achou, testando, que o débito
  criado pelo "Novo lançamento" não balançava o sino: a tela não tem cabeçalho, e o sino voltava sem lembrar o
  número; agora o último número fica guardado enquanto a página está aberta. Consultar a cada 10 minutos com a
  tela parada foi oferecido e recusado pelo dono: o débito sai de hora em hora, quase sempre de madrugada.)*
  Tocar abre o painel
  **"Para conferir"**: um item por lançamento, o mais antigo primeiro, com marca, descrição, "≈ R$ 95,00"
  e "Saiu em 16/11 · Itaú". Dois botões: **Confirmar** (um toque, o caso comum) e **Mudou**, que abre o
  campo do valor da conta e troca o botão por **Salvar**. *(Ajustado na tarefa 5, ao conferir na tela: o
  campo aberto com o valor médio esmaecido parecia valor já preenchido, e dois botões grandes por item
  pesavam.)* O item sai da lista ao conferir; o aviso diz "Sabesp conferido: R$ 102,37.". Sem
  "Desfazer": o valor se corrige no painel do lançamento, como qualquer outro.
- **Lista e painel do lançamento:** "≈ −R$ 95,00" com "a conferir" embaixo; no painel, "Valor médio · a
  conferir" sob o valor e o mesmo bloco de conferir do sino, acima das ações. A linha "Se repete" diz
  "Débito automático, vence dia 20" e o "próximo débito", já no dia útil (a data vem da API).
- **Resumo:** abaixo dos números do mês, a linha "≈ R$ 95,00 a conferir · O "Saiu" usa o valor médio até
  você conferir.", que abre o painel do sino. (O bloco "Saiu" já é um link para a Análise; a linha fica
  à parte.)
- **Novo lançamento:** ao marcar débito automático, o meio de pagamento vira "Débito"; o vencimento é
  escolhido entre a data do lançamento e os 7 dias antes, com o dia da semana ("dom 20/09"). A tela não
  calcula dia útil: mostra o vencimento e diz que fim de semana e feriado vão para o próximo dia útil.
- **"Recorrências"** (`/contas/recorrencias`): a seção **"Débitos automáticos"** primeiro, depois das
  pendências, com vencimento, próxima data do débito e "valor médio" quando o valor muda; as outras
  séries abaixo, em "Outras recorrências". A linha da aba Contas diz "3 ativas · 2 em débito automático" (o
  mesmo separador do "1 pendente" que já existia). No painel da série: o vencimento, "valor médio", o próximo
  vencimento e, quando cai em fim de semana ou feriado, a data em que é debitado. A edição liga e desliga o
  débito automático (regra 10) e, com ele ligado, esconde frequência e pagamento, que são fixos. No painel de
  um lançamento de débito automático, a data se chama "Data do débito".

**Exemplos (usados nos testes).** Conta corrente Itaú.

*Calendário.*

| Data | Útil? | Próximo dia útil |
|---|---|---|
| 16/02/2026 e 17/02/2026 (Carnaval) | Não | 18/02 (Quarta de Cinzas, útil) |
| 03/04/2026 (Sexta-feira Santa; Páscoa em 05/04) | Não | 06/04 |
| 04/06/2026 (Corpus Christi) | Não | 05/06 |
| 31/10/2026 (sábado) | Não | 03/11: 01/11 é domingo e 02/11, Finados |
| 20/11/2026 (sexta, Consciência Negra) | Não | 23/11 |
| 25/12/2026 (sexta) | Não | 28/12 |
| 31/12/2026 (quinta) | Sim | 31/12 |
| 01/01/2027 (sexta) | Não | 04/01/2027 |
| 2025: Carnaval 03/03 e 04/03, Sexta Santa 18/04, Corpus Christi 19/06 | Não | |
| 2027: Carnaval 08/02 e 09/02, Sexta Santa 26/03, Corpus Christi 27/05 | Não | |

*Séries.*

| Série | Débitos |
|---|---|
| Sabesp: vence todo dia 15, R$ 95,00 médio, partida 15/09/2026 | 15/10 (quinta), 16/11 (15/11 é domingo), 15/12 (terça) |
| Copel: vence todo dia 20, R$ 180,00 médio, partida 20/10/2026 | 23/11 (20/11 é feriado), 21/12 (20/12 é domingo) |
| Internet: vence todo dia 31, R$ 120,00 fixo, partida 31/08/2026 | 30/09, 03/11 (vencimento 31/10: sábado, domingo e Finados), 30/11, 31/12 |
| Copel criada do lançamento de 21/09/2026 (segunda), vence dia 20 | Partida 20/09; próximo vencimento 20/10, débito 20/10 (terça) |
| Hoje 25/09/2026, lançamento com data 10/09, vence dia 10 | Partida 10/09; próximo débito 13/10 (10/10 é sábado; 12/10, feriado) |
| Lançamento de 21/09/2026 com vencimento 02/09 | Recusado: "O vencimento fica até 7 dias antes da data do lançamento." |

*Geração e conferência (relógio falso).*

| Caso | Resultado |
|---|---|
| Hoje 15/11/2026 (domingo) | Nada da Sabesp |
| 16/11 às 00:30 em São Paulo | Sabesp gerada: R$ 95,00, data 16/11, ocorrência 15/11, a conferir; o sino mostra 1 |
| A mesma execução duas vezes | Um lançamento só |
| API fora do ar de 14/11 a 17/11 | Em 18/11, a Sabesp de 15/11 é gerada uma vez, com data 16/11 |
| Hoje 20/11 (feriado) | Nada da Copel; em 23/11, gerada com data 23/11 |
| Conferir a Sabesp com R$ 102,37 | Valor 102,37, sem marca; o sino some; a série continua com R$ 95,00 médio; Resumo de novembro: "Saiu" sobe R$ 7,37 |
| Conferir sem mudar o valor | Fica R$ 95,00, sem marca |
| Sabesp de novembro sem conferir em 15/12 | Gera a de dezembro; o sino mostra 2, novembro primeiro |
| Excluir a Sabesp a conferir | Sai do sino; restaurar volta a conferir |
| Editar só a categoria da Sabesp a conferir | Continua a conferir |
| Internet (valor fixo) gerada em 03/11 | Sem marca, fora do sino; entra no "Saiu" de novembro, não de outubro |
| Outro usuário | Nunca vê o sino nem os débitos de quem não é ele |
| Débito automático no Visa ou numa receita | Recusado, com a mensagem da regra 2 |

*Previsão.* Hoje 20/10/2026, com Sabesp, Copel e Internet.

| Onde | Mostra |
|---|---|
| "Daqui para frente", novembro | R$ 515,00 previstos: Internet duas vezes, a de 31/10 (débito 03/11) e a de 30/11; Sabesp de 15/11 (16/11); Copel de 20/11 (23/11) |
| "Daqui para frente", outubro | Nada previsto da Internet: o débito cai em novembro |

**Plano** (detalhe em `tasks/2.26/`, ao começar; cada tarefa com testes antes, por ser regra de data e de
dinheiro)

- Tarefa 1. *(Feita em 2026-09-29: `BankCalendar` em `Prisma.Domain.Calendar`; 43 testes de domínio,
  entre eles 4 propriedades, com o maior adiamento provado em 4 dias de 2000 a 2099; 7 mutações pegas; o
  teste de arquitetura provado com um uso proibido em `Transactions`.)* Calendário bancário no domínio (`BankCalendar`): a tabela do calendário, os feriados de 2025
  a 2027 e propriedades (CsCheck): o próximo dia útil é útil, nunca vem antes da data e não há dia útil
  entre os dois; a função nunca volta no tempo. Teste de arquitetura: só a série e, na 2.22, a fatura
  dependem dele.
- Tarefa 2. *(Feita em 2026-09-30: `RecurrenceKind`, `AutoDebitTerms`, a data do débito na geração e na
  previsão, `Transaction.ConfirmAmount`; 38 testes de domínio, 13 mutações pegas. A migration
  `AddAutoDebits` veio da tarefa 3 para cá, porque sem ela o EF recusa subir o banco e a integração fica
  vermelha; as séries que já existem entram como `Regular`, conferido no banco local com 17 séries. O
  exemplo da previsão estava errado: a Internet é debitada duas vezes em novembro, R$ 515,00, não
  R$ 395,00.)* Débito automático no domínio: tipo e "o valor muda" na série, recusas, vencimento × data do
  débito na geração e na previsão, a primeira ocorrência com vencimento anterior, `AmountEstimated` e o
  conferir no `Transaction`, mudar o tipo da série. Glossário do `CLAUDE.md`.
- Tarefa 3. *(Feita em 2026-09-30: o gerador não mudou, porque as datas são decididas pelo domínio; 10
  testes de integração (domingo, feriado, atraso, valor fixo, vencimento da primeira ocorrência, duas
  execuções ao mesmo tempo e as 4 restrições do banco); 4 mutações pegas.)* ~~Migration~~ (feita na tarefa 2: `AddAutoDebits`, tipo e "o valor muda" na série, a marca no
  lançamento, índice parcial dos lançamentos a conferir, restrições no banco) e o gerador: fim de semana, feriado, atraso, idempotência.
- Tarefa 4. *(Feita em 2026-09-30: `autoDebit`, `amountVaries` e `dueDate` na criação; o tipo na edição,
  que sem os campos novos mantém o tipo (a tela aberta antes não o desfaz); `GET /transactions/to-confirm`,
  `POST /transactions/{id}/confirm-amount`, `amountEstimated` no lançamento, `kind`, `amountVaries` e
  `nextTransactionDate` na série, `estimatedExpenseCents` no Resumo; 12 testes de integração, 9 mutações
  pegas; `api-types.ts` só ganhou campos; `recurrences.http`.)* API: criar e editar com o tipo, conferir, a lista do sino, a marca na resposta do lançamento,
  "a conferir" no Resumo; isolamento por usuário; `api-types.ts` regerado e os `.http`.
- Tarefa 5. *(Feita em 2026-09-30: o sino no cabeçalho, com o painel carregado só no primeiro toque (o
  pacote principal ficou em 411,7 kB, antes 410,7 kB); a marca na lista e no painel; a linha no Resumo;
  débito automático no "se repete" do Novo lançamento e do painel; 15 testes no Vitest, 4 mutações
  pegas; conferido com toques reais em 320, 390 e 1280px, claro e escuro, numa API paralela com dados de
  teste.)* Tela: a opção no "se repete", o sino e o painel "Para conferir", a marca na lista e no painel
  do lançamento, a linha no Resumo, invalidação das chaves (`invalidateMoney`).
- Tarefa 6. *(Feita em 2026-09-30: a seção "Débitos automáticos", "Outras recorrências", o painel e a
  edição da série com o tipo, a contagem na aba Contas e "Data do débito" no lançamento; 7 testes no Vitest,
  3 mutações pegas e uma equivalente (ordenar pelo vencimento ou pelo débito dá a mesma ordem, e o código
  ficou com uma só); conferido com toques reais em 320, 390 e 1280px, claro e escuro, inclusive desligar o
  débito automático de uma série.)* Tela "Recorrências": a seção "Débitos automáticos" e a linha da aba Contas.

## 3. Endpoints

```
GET /dashboard/summary?month=2026-10      → receitas, despesas, sobra e investido do mês
GET /dashboard/categories?month=2026-10   → despesas por categoria raiz, maior primeiro
GET /dashboard/history?month=2026-10      → os 6 meses que terminam no mês, mais antigo primeiro
GET /dashboard/upcoming-statements        → a próxima fatura não paga de cada cartão ativo
GET /dashboard/inherited?month=2026-10    → parcelas de compras anteriores no mês (2.6)
GET /dashboard/committed                  → os 6 meses seguintes ao de hoje e a última parcela (2.6)
GET /dashboard/variation?month=2026-09    → diferença do "Saiu" para o mês anterior e os motivos (2.7)
```

Autocompletar (etapa 2.15, seção 2.8):

```
GET /transactions/descriptions   → descrições dos últimos 12 meses, agrupadas, mais usadas primeiro
```

A resposta da variação é estruturada (mês, mês anterior, os dois "Saiu", a diferença e a lista de
motivos com tipo, categoria, variação e detalhe), não um texto pronto: a tela monta as frases.

`month` no formato `aaaa-mm`; sem ele, o mês de hoje (`IClock.Today`). Mês inválido: 400.

A lista ganha três filtros opcionais para o detalhamento (etapa 2.2):

```
GET /transactions?from=&to=&dateBasis=Settlement&type=Expense&categoryId=   (ou &uncategorized=true)
```

`dateBasis` escolhe a data do período: `Purchase` (padrão, a lista de sempre) ou `Settlement`
(data de caixa, a do dashboard).

Estorno (etapa 2.5) usa os endpoints de lançamento:

```
POST  /transactions        { type: "Refund", accountId, amountCents, purchaseDate, method,
                             categoryId?, description?, refundedTransactionId? }   → 201, lista com 1 item
PATCH /transactions/{id}   valor, data, categoria, descrição e meio (conta e vínculo não mudam)
GET   /transactions?type=Expense&type=Refund&...   type aceita repetição (detalhamento, regra 13)
```

A resposta de lançamento ganha `refundedTransactionId` e, nas despesas, `refundableCents` (quanto
ainda pode ser estornado; na parcelada, sobre o total da compra), para a tela preencher "Estornar".
`installments` precisa ser 1 ou ausente no estorno.

---

### Etapa 2.20 (seção 2.9)

- `POST /transactions/{id}/move-statement` com `{ "direction": "Next" | "Previous" }`: move a compra no
  cartão (a transação à vista ou a compra parcelada inteira, a partir de qualquer parcela). Devolve as
  transações movidas. 400 (recusa da regra 2), 404, 409 (corrida com o pagamento).
- `PATCH /statements/{id}` (já existe) passa a mover compras (regra 4) e a recusar o fechamento que
  atravessa as vizinhas (regra 5); a resposta ganha a quantidade de compras que mudaram de fatura.

### Etapa 2.20b (seção 2.10)

- `POST /statements/{id}/date-preview` com `{ "closingDate", "dueDate" }`: as compras que mudariam de
  fatura com essas datas, sem gravar nada. 400 (as mesmas recusas do `PATCH`), 404.

### Etapa 2.23 (seção 2.11)

- `GET /categories` (já existe) passa a entregar as categorias das versões novas do catálogo ao usuário
  que ainda não as recebeu, uma vez por versão. É a única consulta que grava (`CLAUDE.md`, seção 3).

### Etapa 2.21 (seção 2.12)

- `POST /accounts` e `PATCH /accounts/{id}` (já existem) aceitam `closingDaysBeforeDue` (1 a 20) no
  lugar de `closingDay`; a resposta da conta traz os dois (um deles nulo).
- `PATCH /accounts/{id}` que muda a regra do cartão realinha as faturas (regra 5); a resposta ganha
  `movedPurchases`. 400 (recusas), 404, 409 (corrida com o pagamento).

### Etapa 2.24 (seção 2.13)

- `PATCH /transactions/{id}` (já existe): na compra à vista no cartão, `accountId` de outro cartão troca
  o cartão (regras 1 a 7). 400 (recusas), 409 (corrida com o pagamento).
- `PATCH /installment-purchases/{id}` (já existe): o corpo ganha `accountId`; outro cartão troca o
  cartão da compra inteira. A resposta já traz o `accountId`.

### Etapa 2.25 (seção 2.14)

- `POST /transactions` (já existe): corpo ganha `recurrence` opcional (`frequency`: `Weekly` |
  `Monthly`, `endDate?`); o lançamento criado é a primeira ocorrência. Recusado para estorno,
  transferência e compra parcelada.
- `POST /transactions/{id}/recurrence` com `{ frequency, endDate? }`: a série a partir de um lançamento
  existente (ele vira a primeira ocorrência). 400, 404, 409 se ele já é de uma série.
- `GET /recurrences`: as séries (ativas, encerradas) com a próxima data e as pendências.
- `PATCH /recurrences/{id}`: valor, descrição, categoria, conta, frequência com a próxima data, término;
  vale do próximo em diante. 400, 404, 409 (`xmin`).
- `POST /recurrences/{id}/end`: encerra hoje.
- `POST /recurrences/{id}/pending/{date}` com `{ action: "NextStatement" | "Launch" | "Discard" }`.
- `GET /dashboard/committed` (já existe): cada mês ganha `projectedCents`. A fatura aberta ganha
  `projectedCents` (em `GET /accounts/{id}/statements` e `GET /dashboard/upcoming-statements`).

### Etapa 2.26 (seção 2.15)

Tudo acrescentado como opcional: sem os campos novos, o comportamento é o da 2.25 (`CLAUDE.md`, seção 6).

- `POST /transactions` e `POST /transactions/{id}/recurrence` (já existem): `recurrence` ganha
  `autoDebit?`, `amountVaries?` e `dueDate?` (vencimento da primeira ocorrência: da data do lançamento,
  não de hoje, até 7 dias antes dela; padrão, a data dele). 400 (regra 2, vencimento fora do intervalo).
- `PATCH /recurrences/{id}` (já existe): `autoDebit?` e `amountVaries?`, do próximo em diante.
- `GET /recurrences` (já existe): cada série traz `kind`, `amountVaries` e a próxima data do débito.
- `GET /transactions/to-confirm`: os lançamentos a conferir do usuário, o mais antigo primeiro (id,
  descrição, conta, data, valor estimado). O sino conta os itens.
- `POST /transactions/{id}/confirm-amount` com `{ amountCents? }`: confere; sem valor, mantém a
  estimativa. 400 se o lançamento não está a conferir ("Este lançamento já foi conferido.") ou valor
  inválido; 404.
- `PATCH /transactions/{id}` (já existe): mudar o valor de um lançamento a conferir também confere.
- Lançamento (`GET /transactions` e as demais respostas) ganha `amountEstimated`.
- `GET /dashboard/summary` (já existe) ganha `estimatedExpenseCents`: quanto do "Saiu" ainda é estimativa.

## 4. Tela

- `/` é o Resumo, com o mês na URL (`/?mes=2026-10`) e setas para navegar, como a lista de
  lançamentos. A lista vai para `/lancamentos?mes=`.
- **Topo:** sobra do mês em destaque (`font-display`), com ⓘ que explica a visão de caixa; "Entrou",
  "Saiu" e "Investido" abaixo. Receita em `text-spectrum`, despesa em tinta (`CLAUDE.md`, 7.1); o
  rótulo já diz a direção, então só sobra e investido levam "−" quando negativos.
- **Sem receita no mês**, o destaque mostra os **gastos** ("Gastos de outubro R$ 4.482,86"), não uma
  sobra negativa que só repetiria o "Saiu" em tom de alarme; embaixo, "Lance suas receitas para
  ver quanto sobra". Com receita: "Sobra de …", ou "Faltou em …" se o gasto passar da receita.
- **"Saiu"** mostra, quando houver, "R$ X em faturas de cartão".
- **Por categoria:** barras horizontais na cor de cada categoria, com ícone, valor e %.
- **Comparativo:** gráfico de barras (Recharts, carregado sob demanda) com receitas e despesas dos
  6 meses; o mês escolhido em destaque; tocar numa barra abre aquele mês.
- **Próximas faturas** e **Em contas:** cartões pequenos que levam ao detalhe da conta.
- Tocar numa categoria abre a lista de lançamentos filtrada por ela, pela data de caixa
  (`/lancamentos?mes=2026-10&categoria=<id>`, ou `&categoria=sem`), com um aviso do filtro e um
  botão para limpá-lo.
- **Estilo do dashboard** (ajustado com o usuário na 2.1; na 2.12 passou a valer para todas as telas):
  blocos são superfícies (`.surface`: tom e sombra suave, sem contorno de 1px); serifada só no
  nome do mês e no valor principal, os outros valores em Geist seminegrito; um espectro por tela
  (o filete sob o mês; o destaque usa o halo frio, não o anel); rótulos na cor do texto, cinza só
  para o secundário.
- **Responsivo:** no celular, blocos em duas colunas; a partir de 640px, em três. Nada de rolagem
  lateral em 320px; no computador, a coluna fica centralizada sem esticar os blocos.
- Conferido nos modos claro e escuro, no celular (320 e 390px) e no computador.

**Estorno (etapa 2.6):**

- **Lançar:** "Estorno" entra como opção de tipo em `/lancar`, ao lado de despesa, receita e
  transferência: conta ou cartão, valor, data, categoria (de despesa) e descrição; sem parcelas.
  Um texto curto explica: "Abate uma despesa. No cartão, entra na fatura aberta na data do estorno."
- **Estornar uma compra:** no painel de uma despesa, a ação "Estornar" abre o mesmo formulário já
  preenchido (`/lancar?estorno=<id>`: valor que resta, data de hoje, categoria e "Estorno:
  <descrição>"), com a conta fixa e "Já estornado R$ X de R$ Y" quando houver estorno anterior. O
  painel da despesa mostra "Estornado R$ X de R$ Y"; compra estornada por inteiro não oferece mais a
  ação.
- **Lista e painel:** estorno com ícone de desfazer (`Undo2`), valor com "+" em tinta, não no
  espectro (não é receita), e rótulo "Estorno" ou "Estorno de Jantar" quando ligado; no painel, o
  link para a compra original. Na despesa estornada, "Estornado R$ X".
- **Fatura:** estornos na lista da fatura com "+"; total negativo aparece como "Saldo a favor
  R$ X", com selo próprio no lugar de Fechada, sem botão de pagar.
- **Resumo:** o ⓘ ganha a frase "Estornos abatem as despesas do mês em que caem." A lista por
  categoria mostra o aviso da regra 12 quando esconder algo. "Saiu" negativo leva a nota "Os
  estornos passaram dos gastos do mês", e o comparativo conta o mês negativo como zero (no máximo
  "100% a menos").
- No seletor de tipo de `/lancar`, as quatro opções (despesa, receita, estorno e transferência) viram
  uma grade 2 × 2 abaixo de 640px, para caber em 320px.
- Conferido nos modos claro e escuro, em 320, 390 e 1280px.

**Compromissos herdados (etapa 2.8):**

- **"Parcelas de compras anteriores"**, logo abaixo de "Entrou / Saiu / Investido", porque explica o
  "Saiu": o herdado em destaque, a frase "R$ 1.100,00 dos R$ 1.550,00 que saíram em outubro vieram
  de compras anteriores", uma barra dividida (herdado em tinta, "Decidido no mês" em tinta clara) e a
  lista com ladrilho da categoria, "4/10" com uma barrinha de progresso da compra e o valor. Até 4
  linhas; o resto num "Ver todas (N)". Sem parcela herdada no mês, o bloco não aparece.
- **"Já comprometido"**, junto do bloco "Hoje" (os dois olham para hoje), só no Resumo do mês atual:
  uma linha por mês ("nov/26"), com barra em tinta relativa ao maior mês e o valor inteiro à direita
  (barras verticais com valor não cabem seis em 320px), e "A última parcela vence em abril de 2027".
  Tocar num mês abre o Resumo dele. Sem nada lançado nos 6 meses, o bloco não aparece.
- Sem biblioteca de gráfico (barras em CSS) e sem espectro novo: o da tela continua sendo o filete
  sob o mês. Carregado sob demanda, como as categorias.
- Conferido nos modos claro e escuro, em 320, 390 e 1280px.

**Resumo e Análise (etapa 2.9):**

Pedida pelo usuário depois da 2.8: o Resumo acumulou oito blocos, mistura o que é de relance com o
que é análise, e no computador ocupa uma coluna estreita. Cada tela passa a responder uma pergunta.

- **Resumo (`/`), "como estou?":** o destaque do mês com o ⓘ, "Entrou / Saiu / Investido", o bloco
  "Hoje" (em contas e próximas faturas) e dois atalhos: "Ver análise de setembro" e "Ver lançamentos
  de setembro". Tocar em "Saiu" também abre a Análise do mês. Com a 2.10, ganha uma linha do "por que
  mudou".
- **Análise (`/analise?mes=2026-09`), "para onde foi e por quê?":** o mesmo seletor de mês, e nesta
  ordem: (2.10: por que o gasto mudou), "Onde foi o dinheiro", "Parcelas de compras anteriores",
  "Últimos 6 meses" e, só no mês atual, "Daqui para frente" com o "Já comprometido". Mês sem nada
  lançado: "Nada lançado para setembro" e o botão de lançar, como no Resumo.
- **O mês é o mesmo nas duas:** o `?mes=` vai junto nos atalhos e na barra, então trocar de tela não
  perde o mês escolhido.
- **Barra de navegação:** Resumo, Lançamentos, "+", Análise e Contas, cinco colunas iguais (a Análise
  ocupa o espaço que sobrava à direita do "+"). Em 320px os rótulos continuam cabendo.
- **Computador (a partir de 1024px):** a Análise alarga para até 1024px de conteúdo e usa duas
  colunas: à esquerda "Onde foi o dinheiro" e "Últimos 6 meses"; à direita "Parcelas de compras
  anteriores" e "Daqui para frente". O cabeçalho acompanha a largura da tela aberta. O Resumo continua
  numa coluna: é de relance.
- **Desempenho:** a Análise é carregada sob demanda. O Resumo deixa de trazer categorias, parcelas e
  o comparativo, então o Recharts sai da tela inicial.
- Os blocos não mudam por dentro; só mudam de lugar. Nenhum número aparece em duas telas, a não ser o
  "Saiu", que liga as duas.
- Conferido nos modos claro e escuro, em 320, 390 e 1280px.

**Por que o gasto mudou na tela (etapa 2.10):**

- **Na Análise, no topo:** o cabeçalho "Gastou 6% a mais que em agosto (+R$ 150,00)" e os motivos,
  um por linha: nome (ladrilho da categoria, ou o das parcelas), variação com sinal ("+R$ 600,00",
  "−R$ 450,00", em tinta, sem verde e vermelho) e o detalhe embaixo. Linha de categoria abre a lista
  dela no mês, como em "Onde foi o dinheiro".
- **No Resumo, uma linha:** o cabeçalho e o maior motivo **na direção da diferença** ("Principalmente
  parcelas de compras anteriores, +R$ 600,00"; um motivo contrário não explica o "a mais"), levando à
  Análise. Ela ocupa o lugar do atalho "Ver análise de setembro", que volta enquanto carrega ou sem nada
  a comparar.
- Embaixo do cabeçalho, os dois "Saiu" ("R$ 2.600,00 em agosto → R$ 2.750,00 em setembro"). O valor
  fica na linha do nome e o detalhe embaixo, na largura toda, para caber em 320px. Parcelas e "Outras
  categorias" levam ladrilho neutro e não abrem lista. A partir de 1024px, cabeçalho à esquerda e
  motivos à direita.


**Autocompletar da descrição (etapa 2.15):**

- Em `/lancar`, o campo Descrição vira um *combobox* (padrão WAI-ARIA: `role="combobox"`, `listbox`,
  `aria-activedescendant`). Setas navegam (com a lista fechada, abrem já na primeira), Enter escolhe,
  Esc fecha mantendo o texto, Tab e tocar fora fecham. Com a lista aberta, Enter nunca lança o
  formulário: escolhe a destacada ou só fecha a lista. No celular, ao abrir, o campo sobe para a lista
  não ficar sob o teclado.
- Campo vazio e focado: "Recentes" em chips com o ladrilho (logo da marca ou categoria). Digitando: até
  5 linhas com ladrilho, a descrição com o trecho encontrado sublinhado em violeta, "categoria · conta"
  e "última R$ X" à direita.
- Um aviso para o leitor de tela diz quantas sugestões há, só quando a pessoa para de digitar; a opção
  ativa é lida pelo próprio `aria-activedescendant`; ao escolher, diz o que foi preenchido e o que foi
  mantido.
- O que a sugestão preencheu ganha o rótulo "sugerida" ao lado do título da seção, até a pessoa mudar.
- A lista aparece com opacidade e 4px de deslocamento em 150 ms; filtrar a cada tecla não anima; com
  menos movimento, nada anima (`CLAUDE.md`, 7.1).
- Conferido nos modos claro e escuro, em 320, 390 e 1280px, e pelo teclado.

---

## 5. Testes obrigatórios da fase

### Unitários de domínio (antes da implementação)

- Resumo: sinal de cada tipo; investido com aporte, resgate e transferência entre duas contas de
  investimento; transferência comum e pagamento de fatura fora de receita e despesa; sobra negativa.
- Comparativo: 6 meses com zeros nos meses vazios, virada de ano (fevereiro volta até setembro do
  ano anterior).

### Integração

- O exemplo da seção 2, pelos endpoints: resumo, categorias e novembro com o sapato.
- A lista filtrada pela categoria e pela data de caixa soma o mesmo valor da categoria no resumo.
- Pagamento de fatura não duplica o gasto do mês (`CLAUDE.md`, casos obrigatórios).
- Despesa excluída some do resumo; restaurada, volta.
- Dois usuários: cada um vê só os próprios números.

### Estorno (etapas 2.5 e 2.6)

- **Domínio, antes da implementação:** fatura pela data do estorno (inclusive estorno de compra de
  fatura paga caindo na seguinte); recusa em fatura paga, em conta de investimento e acima do limite
  do vínculo (compra simples e parcelada); total da fatura com saldo a favor; "falta pagar" contando
  fatura negativa como zero; resumo, categorias (líquido, escondidas, participação sobre as
  exibidas) e saldos com o sinal certo.
- **Integração:** o exemplo da seção 2.5 inteiro pelos endpoints; detalhamento da categoria soma o
  valor exibido; editar mudando de fatura; excluir e restaurar (restaurar acima do limite é
  recusado); excluir a compra mantém o estorno; isolamento entre usuários.
- **Frontend:** "Estornar" preenchido com o que resta; estado "Saldo a favor" da fatura.

### Compromissos herdados (etapa 2.8)

- **Domínio, antes da implementação:** o exemplo da seção 2.6 (herdado, ordem, decidido no mês);
  parcela 1 fora do herdado; "Saiu" abaixo do herdado sem divisão; os 6 meses com zero nos vazios e
  virada de ano; estorno abatendo o mês futuro; última parcela além dos 6 meses e ausente sem
  parcelas.
- **Integração:** o exemplo inteiro pelos endpoints, com o pagamento de fatura e a compra excluída
  de fora; isolamento entre usuários.
- **Frontend:** participação arredondada e ausente quando o "Saiu" não cobre o herdado; largura
  relativa das barras; rótulo do mês e texto da última parcela.

### Por que o gasto mudou (etapa 2.10)

- **Domínio, antes da implementação:** o exemplo da seção 2.7 (ordem, detalhes e soma); a soma
  exata também com "Outras categorias" (5 categorias variando), com estorno numa categoria e com
  "Sem categoria"; propriedade (CsCheck): para quaisquer dois meses, Σ motivos = diferença.
- **Integração:** o exemplo pelos endpoints; isolamento entre usuários; mês inválido é 400.
- **Frontend:** as frases de cada motivo e do cabeçalho, com e sem porcentagem.


### Autocompletar da descrição (etapa 2.15)

- **Domínio, antes da implementação:** o exemplo da seção 2.8 (agrupamento sem acento e sem espaços,
  o mais recente vence, parcela contada uma vez com o total, ordem); despesa e receita separadas; limite
  de 300.
- **Integração:** o exemplo pelos endpoints, com estorno, transferência, excluído e o mês de 12 meses
  atrás de fora; isolamento entre usuários.
- **Frontend:** correspondência (início, palavra, trecho, acento), desempate pelo uso, Recentes, filtro
  por tipo; o preenchimento não desfaz conta nem categoria tocadas e ignora as que não existem mais.

### Fatura alinhada ao banco (etapa 2.20)

- Domínio, antes da implementação: os 8 casos da tabela da seção 2.9; propriedades do recálculo
  (soma, ciclos consecutivos, fatura paga intocada, sem edição nem presa igual a hoje).
- Integração: o cenário dos lanches de ponta a ponta (faturas, lista, Resumo de dois meses), a
  parcelada, as recusas, a corrida com o pagamento, a edição de datas movendo compras e o isolamento.
- Vitest: ações disponíveis no painel (próxima, anterior, nenhuma) e os textos dos avisos.

### Prévia do ajuste de datas (etapa 2.20b)

- Integração: a prévia lista exatamente o que o salvar move e não grava nada (com mutação provando);
  compra entrando; compra presa fora da lista; recusas iguais às do salvar; fatura paga; isolamento.
- Vitest: quando pedir a prévia, título da lista, "de → para", valor da parcelada e do estorno, texto do
  botão.

### Catálogo de categorias que evolui (etapa 2.23)

- Domínio, antes da implementação: usuário antigo recebe as categorias novas uma vez; renomeada não
  duplica; excluída não volta; nome igual no mesmo lugar é adotado, em outro lugar não; duas
  sincronizações não duplicam; chaves únicas e estáveis.
- Integração: usuário antigo vê Seguros uma vez; exclusão depois de receber não volta; categoria criada à
  mão é adotada; duas listas ao mesmo tempo não duplicam; isolamento.
- Arquitetura: todo ícone do catálogo está no mapa do front.

### Cartão que fecha N dias antes do vencimento (etapa 2.21)

- Domínio, antes da implementação: as três tabelas de datas da seção 2.12; a tabela da troca de regra;
  as recusas; propriedade (CsCheck): para todo vencimento de 1 a 31 e todo N de 1 a 20, faturas seguidas
  respeitam a ordem da regra 5 da 2.9, e toda data de compra cai em exatamente uma fatura.
- Integração: criar cartão no modelo novo e lançar em 27/10 e 28/10; trocar a regra move a compra e o
  caixa; editar só o nome não toca faturas; restrição do banco recusa os dois modelos; corrida com o
  pagamento; isolamento entre usuários e entre cartões.
- Vitest: o exemplo ao vivo com as datas do Itaú; os textos da regra na lista e no detalhe; o aviso.

### Trocar o cartão de uma compra (etapa 2.24)

- Domínio, antes da implementação: os casos da tabela da seção 2.13 que não dependem do banco (à
  vista, parcelada com a soma igual, troca junto com a data, presa que solta, as recusas).
- Integração: a troca à vista e a parcelada de ponta a ponta (faturas e totais dos dois cartões, Resumo
  de novembro e dezembro); estorno recusado nas duas; cartão de outro usuário; as duas corridas com o
  pagamento (troca de cartão e troca de data).
- Vitest: o texto do aviso e os cartões oferecidos (ativos e o atual).

### Lançamentos que se repetem (etapa 2.25)

- Domínio, antes da implementação: as tabelas de agenda, geração e previsão da seção 2.14; propriedade
  da agenda mensal (dia 29, 30 e 31 em todos os meses de 4 anos, inclusive bissexto).
- Integração: gerar duas vezes e duas ao mesmo tempo sem duplicar; atraso de dias; excluído que não
  volta; edição dali em diante; encerrar; conta inativa; fatura paga que vira pendência e as três saídas;
  corrida com o pagamento da fatura; um usuário nunca gera nem vê a série de outro.
- Vitest: o texto da linha "se repete" ("Todo mês, no dia 25 · próxima 25/10"), o dia 31, a semana, e o
  texto da previsão.

### Frontend (Vitest)

- Participação por categoria arredondada; variação percentual das despesas, com mês anterior zero.
