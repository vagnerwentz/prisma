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

---

## 3. Endpoints

```
GET /dashboard/summary?month=2026-10      → receitas, despesas, sobra e investido do mês
GET /dashboard/categories?month=2026-10   → despesas por categoria raiz, maior primeiro
GET /dashboard/history?month=2026-10      → os 6 meses que terminam no mês, mais antigo primeiro
GET /dashboard/upcoming-statements        → a próxima fatura não paga de cada cartão ativo
```

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
- **Estilo do dashboard** (ajustado com o usuário na 2.1; as demais telas seguem como estão):
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

### Frontend (Vitest)

- Participação por categoria arredondada; variação percentual das despesas, com mês anterior zero.
