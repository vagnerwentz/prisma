# Fase 1 — Núcleo

Detalhamento da Fase 1 do `PLAN.md`. Regras permanentes estão em `CLAUDE.md`; aqui fica
só o que é específico desta fase.

Ao final da fase, o sistema permite cadastrar-se, criar contas, lançar uma despesa
parcelada no cartão pelo celular e ver a parcela cair na fatura correta.

> **Idioma:** código em inglês; textos de interface e mensagens de erro em pt-BR.

---

## 1. Modelo de dados

Toda entidade tem `Id` (Guid), `UserId` (Guid), `CreatedAt` e `UpdatedAt` (DateTime
UTC) e `DeletedAt` (DateTime?, soft delete). Omitidos abaixo por brevidade.

```csharp
public enum AccountType   { Checking, CreditCard, Cash, Investment }
public enum TransactionType { Income, Expense, Transfer }
public enum PaymentMethod { Pix, Debit, Credit, Boleto, Cash, Ted }
public enum TransactionSource { Manual, OfxImport, PdfImport, Nfce }
```

### Account

| Campo | Tipo | Observação |
|---|---|---|
| `Name` | string | "Itaú Personnalité", "Cartão Itaú Visa" |
| `Type` | AccountType | |
| `InitialBalanceCents` | long | pode ser negativo |
| `ClosingDay` | int? | 1–31, obrigatório se `CreditCard` |
| `DueDay` | int? | 1–31, obrigatório se `CreditCard` |
| `CreditLimitCents` | long? | só cartão |
| `IsActive` | bool | conta inativa não aparece no lançamento rápido |

**Invariantes:** `CreditCard` exige `ClosingDay` e `DueDay`; os demais tipos exigem que
ambos sejam nulos. Mensagem de erro em pt-BR. Conta com transações ativas não pode ser
excluída (409, sugerindo mover as transações ou marcar a conta como inativa).

### Statement (fatura do cartão)

| Campo | Tipo | Observação |
|---|---|---|
| `AccountId` | Guid | sempre um `CreditCard` |
| `Reference` | string | `"2026-03"`, identifica o ciclo pelo **mês do vencimento** |
| `ClosingDate` | DateOnly | gerada pelo calculador, **editável** |
| `DueDate` | DateOnly | gerada pelo calculador, **editável** |
| `IsPaid` | bool | |

Editar as datas **recalcula o `SettlementDate`** de todas as transações do statement,
porque o Itaú antecipa ou adia o fechamento em fim de semana e feriado.

Índice único em (`AccountId`, `Reference`).

### Category

| Campo | Tipo | Observação |
|---|---|---|
| `Name` | string | |
| `Type` | TransactionType | Income ou Expense |
| `ParentCategoryId` | Guid? | nulo = categoria; preenchido = subcategoria |
| `Icon`, `Color` | string? | |

Apenas **dois níveis**. Subcategoria não pode ter filha.

### InstallmentPurchase

Agrupa as parcelas de uma mesma compra.

| Campo | Tipo |
|---|---|
| `Description` | string |
| `TotalAmountCents` | long |
| `InstallmentCount` | int |
| `PurchaseDate` | DateOnly |
| `AccountId` | Guid |

### Transaction

| Campo | Tipo | Observação |
|---|---|---|
| `AccountId` | Guid | |
| `Type` | TransactionType | |
| `AmountCents` | long | **sempre positivo**; o `Type` define o sinal |
| `PurchaseDate` | DateOnly | |
| `SettlementDate` | DateOnly | base de todo agregado do dashboard |
| `StatementId` | Guid? | preenchido só em compra no cartão |
| `CategoryId` | Guid? | |
| `Method` | PaymentMethod | |
| `Description` | string | editável pelo usuário |
| `RawDescription` | string? | texto cru de importação, **imutável** |
| `InstallmentPurchaseId` | Guid? | |
| `InstallmentNumber` | int? | 1-based |
| `TransferPairId` | Guid? | a outra ponta |
| `Source` | TransactionSource | |

Índices: (`UserId`, `SettlementDate`), (`UserId`, `AccountId`, `SettlementDate`),
(`UserId`, `CategoryId`).

---

## 2. Regras desta fase

### 2.1 Cálculo do statement

Dada uma compra em `PurchaseDate` num cartão com `ClosingDay` e `DueDay`:

1. O ciclo que recebe a compra é o primeiro cujo `ClosingDate` seja **igual ou
   posterior** à `PurchaseDate`. Compra exatamente no dia do fechamento entra no
   statement **daquele** ciclo.
2. `ClosingDay` ou `DueDay` maior que o número de dias do mês é **ajustado para o
   último dia do mês**. Cartão que fecha dia 31 fecha dia 30 em abril e dia 28 ou 29 em
   fevereiro.
3. Se o `DueDay` for menor ou igual ao `ClosingDay`, o vencimento cai no **mês
   seguinte** ao do fechamento.
4. O `SettlementDate` da transação é o `DueDate` do statement.
5. Se o statement já existe com datas editadas manualmente, as datas editadas
   prevalecem sobre o cálculo.

### 2.2 Parcelamento

- De 1 a 24 parcelas, e o valor total precisa ter ao menos 1 centavo por parcela.
- O cartão aceita só despesa nesta fase; estorno está nas pendências do `PLAN.md`.
- `Money.SplitInto(n)` distribui o resto nas primeiras parcelas.
- A parcela *i* entra no statement *i-1* ciclos depois do statement da compra.
- `PurchaseDate` é o mesmo em todas as parcelas; o que muda é o `SettlementDate`.
- Editar uma parcela isolada altera só aquela transação, e só **descrição e categoria**: o
  valor e a data mudam pela compra inteira, para a soma continuar igual ao total. Compra à
  vista no cartão (sem `InstallmentPurchase`) aceita também o valor e a data. Em nenhum caso a
  conta, o tipo ou o meio de pagamento mudam: para isso, exclua e lance de novo.
- **Mudar a data da compra no cartão** (etapa 1.14b) recalcula a fatura de cada parcela como na
  criação: a parcela *i* vai para o statement *i-1* ciclos depois do statement da nova data,
  reaproveitando faturas existentes (e as datas editadas delas) ou abrindo as que faltam. O
  `SettlementDate` passa a ser o vencimento da nova fatura e os valores não mudam. É recusado se
  alguma parcela estiver em fatura paga, ou se a nova data levar alguma parcela para uma fatura
  paga. Exemplo (fecha dia 5, vence dia 12): compra à vista de
  10/03/2026 movida para 04/03/2026 sai da fatura de abril (vence 12/04) para a de março (vence
  12/03); compra em 3x movida de 10/03 para 10/05 passa a vencer 12/06, 12/07 e 12/08.
- Editar o `InstallmentPurchase` (valor total ou número de parcelas) **redistribui**
  todas as parcelas ainda não pagas e mantém a soma exata. Parcela paga é a que está em
  fatura paga: mantém o valor e não pode ser removida. Mais parcelas entram nos ciclos
  seguintes; menos parcelas removem as últimas. Descrição e categoria também mudam, nas
  parcelas não pagas. A data da compra também muda, pela regra acima. De 1 a 24 parcelas.
- Excluir o `InstallmentPurchase` faz soft delete em todas as parcelas.
- `POST /installment-purchases/{id}/restore` desfaz a exclusão (o "Desfazer" da tela): volta a
  compra com as parcelas excluídas **junto com ela**. Parcelas removidas antes, por uma edição que
  reduziu o número de parcelas, ficam de fora. Se as parcelas não forem exatamente 1..N somando o
  total, a restauração é recusada (409). Parcela cuja categoria foi excluída volta sem categoria.

A API deixa explícito o escopo da edição: `PATCH /transactions/{id}` altera uma parcela;
`PATCH /installment-purchases/{id}` altera o conjunto.

### 2.3 Transferências

Uma transferência cria **duas** transações em uma única operação atômica:

- saída na conta de origem e entrada na de destino, ambas com `Type = Transfer`;
- ligadas entre si por `TransferPairId`;
- excluídas de todo cálculo de receita e despesa;
- excluir uma ponta faz soft delete nas duas.

Três casos usam esse mesmo mecanismo:

| Caso | Origem | Destino |
|---|---|---|
| Pagamento de fatura | Checking | CreditCard |
| Aporte em investimento | Checking | Investment |
| Movimentação entre contas | qualquer | qualquer |

Pagar a fatura marca o `Statement` como `IsPaid`. O gasto do mês já está representado
pelas transações individuais do cartão, então o pagamento **não** é despesa.

### 2.4 Categorias padrão (seed do novo usuário)

Nomes gravados em **pt-BR**, porque são dados exibidos ao usuário, não código.

**Despesas:** Moradia (Aluguel, Condomínio, Energia, Água, Internet, Gás); Alimentação
(Mercado, Restaurante, Delivery, Padaria); Transporte (Combustível, App de transporte,
Estacionamento, Manutenção, Transporte público); Saúde (Plano de saúde, Farmácia,
Consultas, Academia); Educação (Cursos, Livros, Mensalidade); Lazer (Streaming, Viagem,
Bares, Cinema); Compras (Roupas, Eletrônicos, Casa); Serviços (Assinaturas, Telefonia);
Impostos e Tarifas; Outros.

**Receitas:** Salário, Freelance, Rendimentos, Reembolso, Outros.

Todas editáveis e excluíveis. Excluir categoria com transações vinculadas é bloqueado,
com mensagem sugerindo realocar antes.

Cada categoria padrão nasce com ícone (nome do Lucide) e cor: cada categoria é uma faixa
do espectro e as subcategorias herdam a cor da categoria pai (etapa 1.13).

---

## 3. Endpoints

```
POST   /auth/register
POST   /auth/login
POST   /auth/logout
GET    /auth/google               → redirect (adiado)
GET    /auth/google/callback      (adiado)
POST   /auth/forgot-password
POST   /auth/reset-password
GET    /auth/me

GET    /accounts
POST   /accounts
PATCH  /accounts/{id}
DELETE /accounts/{id}

GET    /categories
POST   /categories
PATCH  /categories/{id}
DELETE /categories/{id}

GET    /accounts/{id}/statements
PATCH  /statements/{id}           → editar datas, recalcula SettlementDate
POST   /statements/{id}/pay       → cria a transferência

GET    /transactions              ?from=&to=&accountId=&categoryId=&search=&statementId=
                                  (período pela PurchaseDate; statementId = compras de uma fatura)
POST   /transactions              → aceita Installments >= 1
GET    /transactions/{id}
PATCH  /transactions/{id}
DELETE /transactions/{id}         → soft delete
POST   /transactions/{id}/restore

PATCH  /installment-purchases/{id}
DELETE /installment-purchases/{id}
POST   /installment-purchases/{id}/restore

POST   /transfers
```

Recurso de outro usuário retorna **404**, nunca 403: 403 confirmaria a existência do
recurso.

Erros de negócio em `ProblemDetails`, com `detail` em pt-BR.

### Faturas na tela (etapa 1.15)

- **Fatura atual** é a primeira não paga que fecha hoje ou depois: a compra feita no dia do
  fechamento ainda entra nela.
- **Status:** "Paga"; "Fechada" (fechamento já passou); "Aberta" (a atual); "Futura". Sem pagamento
  de fatura (etapa 1.10), fatura fechada não aparece como vencida.
- Faturas com total zero (abertas por uma edição, por exemplo) ficam escondidas, exceto a atual e
  as que tiveram as datas ajustadas.

---

## 4. Testes obrigatórios da fase

Escritos **junto** com a implementação; os de dinheiro e data, **antes** dela.

### Unitários de domínio

- `Money.SplitInto`: propriedade (CsCheck) de que a soma das partes é igual ao total,
  para qualquer valor de 0 a 10 milhões de centavos e qualquer n de 1 a 360.
- `Money.SplitInto`: exemplos R$ 100,00/3x → 3334+3333+3333; R$ 0,01/2x → 1+0;
  R$ 0,00/5x → cinco zeros.
- `SplitInto(0)` e `SplitInto(-1)` lançam.
- `StatementCalculator`: compra na véspera, no dia e no dia seguinte ao fechamento.
- `StatementCalculator`: fechamento dia 31 em abril, em fevereiro comum e em bissexto.
- `StatementCalculator`: `DueDay` anterior ao `ClosingDay` cai no mês seguinte.
- `StatementCalculator`: parcela 10 de 10 cai na 10ª fatura, 9 ciclos após a da compra (regra 2.2).
- `IClock`: compra às 23h30 em `America/Sao_Paulo` permanece no mesmo dia.
- `Account` do tipo `CreditCard` sem `ClosingDay` não pode ser construída.
- Transferência sempre produz exatamente duas transações ligadas entre si.

### Integração (Testcontainers)

- Cadastro, login com cookie, login com senha errada, rate limit disparando.
- Novo usuário nasce com as categorias padrão.
- Compra em 10x gera 10 transações com `SettlementDate` correto em cada mês.
- `PATCH` em uma parcela não altera as demais.
- `PATCH` no `InstallmentPurchase` redistribui e mantém a soma exata.
- Editar datas do statement recalcula o `SettlementDate` das transações dele.
- Transferência não entra em receita nem despesa em nenhuma consulta.
- Pagar fatura não duplica o gasto do mês.
- Excluir uma ponta da transferência faz soft delete nas duas.
- Soft delete some da listagem; restaurar traz de volta.
- **Dois usuários:** nenhum endpoint vaza dado do outro; acesso por id direto retorna 404.

### Arquitetura

- `Prisma.Domain` não referencia EF Core, ASP.NET Core nem Npgsql.
- Nenhum tipo, exceto a implementação de `IClock`, usa `DateTime.Now` ou
  `DateTime.UtcNow`.

---

## 5. Frontend (etapa 1.12)

Escopo mínimo, mobile-first. Código em inglês, textos em pt-BR.

- Login e cadastro por e-mail e senha (login com Google adiado).
- Lista de transações agrupada por dia, com valor em destaque.
- Formulário de lançamento rápido: valor primeiro, depois conta, categoria e data já
  preenchida com hoje. Meta de **menos de 10 segundos** para lançar um gasto.
- Seleção de parcelas quando a conta for cartão.
- Tipos TypeScript gerados do OpenAPI (`npm run gen:api`); nada de tipo escrito à mão
  duplicando DTO do backend.

Dashboard e gráficos ficam para a Fase 2.

---

## 6. Fora de escopo nesta fase

Recorrência de transações, orçamentos, metas, importação de arquivo, NFC-e, merchants
normalizados, exportação em CSV, PWA instalável, rentabilidade de investimento.
