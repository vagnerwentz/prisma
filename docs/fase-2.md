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

### Frontend (Vitest)

- Participação por categoria arredondada; variação percentual das despesas, com mês anterior zero.
