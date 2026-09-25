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

### 2.2 Gastos por categoria

- Só despesas do mês, agrupadas pela **categoria raiz**: a despesa numa subcategoria (Mercado)
  soma na categoria pai (Alimentação).
- Despesa sem categoria vai para "Sem categoria".
- Ordem: do maior valor para o menor. A participação (%) é calculada na tela sobre o total de
  despesas do mês, arredondada para inteiro.

### 2.3 Comparativo mensal

- Os **6 meses** que terminam no mês escolhido, do mais antigo para o mais recente, cada um com
  receitas, despesas, sobra e investido (regra 2.1). Mês sem lançamentos aparece com zeros.
- A tela destaca a variação das despesas em relação ao mês anterior ("12% a mais que setembro").
  Mês anterior com despesa zero: sem porcentagem.

### 2.4 Próximas faturas e saldo em contas

- **Próxima fatura de cada cartão ativo:** a primeira não paga que vence hoje ou depois, com total
  maior que zero. Mostra cartão, mês, vencimento e total; no topo, a soma de todas.
- **Em contas:** soma dos saldos atuais das contas ativas que não são cartão (regra 2.5 da Fase 1).
  Reaproveita `GET /accounts/balances`.

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

- Receitas **R$ 8.000,00**; despesas **R$ 4.000,00** (2.500 + 800 + 600 + 100); sobra
  **R$ 4.000,00**; investido **R$ 1.000,00** (1.500 − 500).
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

---

## 4. Tela

- `/` é o Resumo, com o mês na URL (`/?mes=2026-10`) e setas para navegar, como a lista de
  lançamentos. A lista vai para `/lancamentos?mes=`.
- **Topo:** sobra do mês em destaque (`font-display`), com ⓘ que explica a visão de caixa; "Entrou",
  "Saiu" e "Investido" abaixo. Receita em `text-spectrum`, despesa em tinta (`CLAUDE.md`, 7.1); o
  rótulo já diz a direção, então só sobra e investido levam "−" quando negativos. Sem receita no
  mês, o título é "Saldo de …" e a barra de gasto some.
- **Por categoria:** barras horizontais na cor de cada categoria, com ícone, valor e %.
- **Comparativo:** gráfico de barras (Recharts, carregado sob demanda) com receitas e despesas dos
  6 meses.
- **Próximas faturas** e **Em contas:** cartões pequenos que levam ao detalhe da conta.
- Tocar numa categoria abre a lista de lançamentos do mês filtrada por ela.
- **Estilo do dashboard** (ajustado com o usuário na 2.1; as demais telas seguem como estão):
  blocos são superfícies (`.surface`: tom e sombra suave, sem contorno de 1px); serifada só no
  nome do mês e no valor principal, os outros valores em Geist seminegrito; um espectro por tela
  (o filete sob o mês; o destaque usa o halo frio, não o anel); rótulos na cor do texto, cinza só
  para o secundário.
- **Responsivo:** no celular, blocos em duas colunas; a partir de 640px, em três. Nada de rolagem
  lateral em 320px; no computador, a coluna fica centralizada sem esticar os blocos.
- Conferido nos modos claro e escuro, no celular (320 e 390px) e no computador.

---

## 5. Testes obrigatórios da fase

### Unitários de domínio (antes da implementação)

- Resumo: sinal de cada tipo; investido com aporte, resgate e transferência entre duas contas de
  investimento; transferência comum e pagamento de fatura fora de receita e despesa; sobra negativa.
- Comparativo: 6 meses com zeros nos meses vazios, virada de ano (fevereiro volta até setembro do
  ano anterior).

### Integração

- O exemplo da seção 2, pelos endpoints: resumo, categorias e novembro com o sapato.
- Pagamento de fatura não duplica o gasto do mês (`CLAUDE.md`, casos obrigatórios).
- Despesa excluída some do resumo; restaurada, volta.
- Dois usuários: cada um vê só os próprios números.

### Frontend (Vitest)

- Participação por categoria arredondada; variação percentual das despesas, com mês anterior zero.
