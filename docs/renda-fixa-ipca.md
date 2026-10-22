# Renda fixa indexada ao IPCA (IPCA e IPCA+): investigação e plano

> **Estado (2026-10-19): investigação concluída, nada implementado, e a implementação está EM ESPERA por decisão do dono** (ele vai buscar
> dados melhores, extratos reais de CDB IPCA+ e do Tesouro IPCA+, antes de seguir; **não começar a R.17 em diante sem ele pedir**). A Fase 1
> (admin dos dados de mercado, R.15 e R.16) **está feita**: `docs/admin-mercado.md`. Este arquivo guarda o que foi verificado, as decisões e as fases para o IPCA, para a retomada não
> depender da conversa. O CDI, o CDI+, o pré-fixado e as letras (LCI/LCA) estão em `docs/renda-fixa.md`.

Legenda: **[V]** verificado (código ou fonte, com data); **[H]** hipótese a confirmar; **[REC]** recomendação minha, aguardando o dono.

---

## 1. Fatos verificados

### 1.1 Fontes do IPCA

| Fonte | O que traz | Estado |
|---|---|---|
| **BCB SGS 433** (`api.bcb.gov.br/dados/serie/bcdata.sgs.433/dados?formato=json&dataInicial=dd/MM/aaaa&dataFinal=dd/MM/aaaa`) | **Variação mensal do IPCA, em %**, valor como texto (`"0.82"`). A data é o **primeiro dia do mês de referência** (`01/09/2026` = IPCA de setembro/2026) | **[V] 2026-10-19**, consulta direta |
| **IBGE SIDRA, tabela 1737** (`servicodados.ibge.gov.br/api/v3/agregados/1737/periodos/-4/variaveis/63\|2266?localidades=N1[all]`) | Variável **63** = variação mensal (%); variável **2266** = **número-índice, base dezembro/1993 = 100**, 2 casas | **[V] 2026-10-19** (lido por WebFetch; o `curl` daqui não alcançou o IBGE) |
| **BCB SGS 1737** | Provavelmente o mesmo número-índice do IBGE, pelo mesmo cliente do CDI | **[H]**: a rede caiu na hora de testar; **confirmar na Fase 0** |
| **BCB Olinda, Expectativas de Mercado Mensais** (`olinda.bcb.gov.br/olinda/servico/Expectativas/versao/v1/odata/ExpectativaMercadoMensais`, filtro `Indicador eq 'IPCA'`) | Mediana e média da expectativa do IPCA por mês de referência (`DataReferencia` = `10/2026`), com a data da pesquisa; gratuito | **[V] 2026-10-19**: mediana de 0,32% para 10/2026 na pesquisa de 16/10. O campo `baseCalculo` (0 ou 1) não está explicado no que li |

### 1.2 Comportamento da SGS 433 [V 2026-10-19]
- Consulta do dono (18/10/2023 a 18/10/2026): HTTP 200, `application/json`, **36 linhas** (10/2023 a 09/2026); `10/2023` = 0,24, `11/2023` = 0,28, `12/2023` = 0,56, `01/2024` = 0,42.
- **Último mês publicado: `01/09/2026` = 0,82.** Outubro/2026 ainda não existe (o IBGE divulga por volta do dia 10 do mês seguinte).
- **Valores negativos existem:** 08/2024 = −0,02; 08/2025 = −0,11; 08/2026 = −0,32.
- **Histórico inteiro** (desde 01/1994) respondeu numa requisição só (HTTP 200, ~14 KB): **não vimos o limite de 10 anos** do CDI diário. O início de 1994 tem inflação de 40% ao mês (hiperinflação), irrelevante: a carga começaria em 2000.
- Faixa sem dado (mês futuro): **HTTP 404** com `{"erro":{"statusCode":404,"detail":"…Value(s) not found"}}`, o mesmo marcador do CDI.
- `…/dados/ultimos/3?formato=json` funciona.
- **O firewall do BCB é intermitente** (mesmo cenário do CDI, `docs/admin-mercado.md`, seção 1): a SGS 12 devolveu a página "Requisição inválida" com código de erro numa janela pequena, e depois todas as requisições do `curl` deram timeout. Tudo que o cliente do IPCA faz precisa tratar HTML, JSON de erro com status 200, 404 e timeout.

### 1.3 O número-índice e a cadeia de percentuais [V]
IBGE, 2026: número-índice jun = 7652,37; jul = 7657,73; ago = 7633,23; set = 7695,82. A razão entre eles confere com a variação mensal (0,07%, −0,32%, 0,82%).

De 06 a 09/2026, a **cadeia dos percentuais** arredondados em 2 casas dá **1,00728633**; a **razão dos números-índice** dá **1,00728706**. A diferença é 7×10⁻⁷ (R$ 0,007 em R$ 10 mil), mas **cresce com o capital, com os anos e com o erro de arredondamento de cada mês**. O número-índice é a base oficial do VNA do Tesouro. Por isso a recomendação T1 abaixo.

### 1.4 Convenção do Tesouro (VNA) [V em fonte secundária; texto oficial da ANBIMA não abriu]
- VNA = valor nominal atualizado; data-base **15/07/2000 = R$ 1.000,00**; atualizado **todo dia 15** pelo IPCA acumulado desde a data-base.
- Entre dois dias 15, o VNA cresce pelo **IPCA do mês projetado** (e depois pelo oficial) **pro rata por dias úteis**: `VNAproj = VNA × (1 + IPCAprojetado)^(du decorridos / du do período)`.
- O preço do título é `VNA × cotação`, com a cotação vinda da **taxa real** (base **252 dias úteis**). Venda antes do vencimento vale o **preço de mercado**, que muda com a taxa real: **não é** o valor na curva.
- **Isto é a convenção do Tesouro.** O CDB IPCA+ de banco pode usar outra (aniversário na data do aporte, degraus mensais, base 365) **[H]**.

### 1.5 O que o código atual faz e não faz [V]
- `reference_rates` (`ReferenceRate`, `ReferenceRateConfiguration`) **não comporta o IPCA**: a coluna é `daily_rate_e8` com `CHECK >= 1 AND < 1_000_000` (proíbe valor **negativo** e é fração diária), `ReferenceRateSeries` só tem `Cdi`, e `RateHistory` e `ReferenceRateCoverage` assumem dias úteis.
- O motor (`FixedIncomeEngine.Balance/Series`) recebe `Remuneration` e um `RateHistory` (diário). O pré-fixado usa o `BankCalendar` (namespace `Prefixed`).
- `AnnualRate` aceita de 0,01% a 100%: **não aceita 0%**.
- `RateBook`/`RatesStatus` (aviso de taxas) e a amostragem de datas da evolução são só do CDI.

## 2. Decisões

### 2.1 Financeiras e de produto (precisam do dono ou de extrato)

| # | Pergunta | Opções | [REC] |
|---|---|---|---|
| **F1** | **Como a inflação entra no saldo** | **(A)** degraus: o IPCA do mês entra de uma vez no aniversário. **(B)** pro rata dos dias úteis entre os dias 15 com o IPCA **oficial** (o mês corrente, ainda sem IPCA, fica sem inflação até a divulgação). **(C)** B mais **projeção** do mês corrente (Expectativas do BCB) | **Decidir pelo extrato (Fase 0).** Sem extrato, o ponto de partida é (B), por ser o método do VNA sem a projeção |
| **F2** | Dia de aniversário | dia 15 (Tesouro) ou data do aporte | idem: extrato |
| **F3** | Base do juro real | 252 dias úteis (Tesouro) ou 365 corridos | 252, **a confirmar** |
| **F4** | Produtos da primeira entrega | CDB, LCI e LCA IPCA+ (o modelo atual comporta); **Tesouro IPCA+** (preço de mercado, VNA, cupom semestral, taxa de custódia da B3) | CDB, LCI e LCA; Tesouro **depois** |
| **F5** | **IPCA+0** ("IPCA puro") | aceitar ou exigir taxa real mínima de 0,01% | aceitar (IPCA+0 não é raro em letras) |
| **F6** | Mostrar a rentabilidade real | só o saldo, ou nominal e real | saldo e rendimento nominal primeiro |
| **F7** | Imposto | O IR incide sobre o **rendimento nominal** (inclusive a correção), pela mesma tabela do CDB; as letras seguem isentas | **[H]** confirmar o texto legal antes de virar teste |
| **F8** | Inflação negativa | o fator pode cair num mês (08/2026 = −0,32%); contrato com piso? | sem piso (reflete o índice) |

### 2.2 Técnicas (minhas, com recomendação)
- **T1. Guardar o número-índice e a variação**, e calcular pela razão dos índices (não pela cadeia de percentuais). Aceitar a variação como conferência.
- **T2. Fonte:** SGS 433 (variação) e SGS 1737 (número-índice) pelo cliente do BCB **se a 1737 for confirmada** [H]; senão, IBGE SIDRA como segunda fonte.
- **T3. Carga inicial desde 2000** (`Rates:Sync`-like, configurável).
- **T4. Janela de sincronização:** do último mês gravado menos 2 meses até hoje (uma requisição pequena); o 404 "Value(s) not found" é lista vazia, não falha.
- **T5. Mês ausente é "desconhecido", nunca zero.** O mês esperado é o anterior depois do dia ~12 (o IBGE divulga por volta do dia 10), senão o retrasado.
- **T6. A expectativa de mercado (Olinda) só entra se o dono escolher a convenção (C)**; nunca grava como se fosse dado observado.

## 3. Arquitetura proposta (alternativa A: estender o modelo atual)

| | A: estender o atual | B: indexadores separados | C: estratégias por modalidade |
|---|---|---|---|
| Banco | 1 tabela nova (mensal) | idem | idem |
| Complexidade | Baixa | Média | Alta |
| CDI atual | Intacto | Intacto | Exige refatorar o motor |
| Novo indexador depois | Enum + fonte | Fácil | Mais fácil, abstração prematura |

**Recomendação: A.** A C não traz benefício concreto (o motor já separa as fórmulas por índice e os testes cobrem isso). **Não há motivo para substituir nada do CDI.**

Peças (**nomes são propostas**, ainda não existem):
- **Domínio:** `FixedIncomeIndexKind` ganha `IpcaPlus`; entidade global mensal (sem dono, em `GlobalTables`) com série, mês (primeiro dia), variação em E4 (**com sinal**), número-índice, `source`, `fetched_at`, `revised_at`, índice único `(série, mês)`, checks de faixa que aceitem negativo; um histórico mensal no domínio (`MonthlyIndexHistory`); o motor recebe um objeto com os históricos (diário e mensal) em vez de mais um parâmetro; o juro real reaproveita a conta do `PrefixedYield` (dias úteis, 252); `AnnualRate` passa a aceitar 0 só para o IPCA (F5).
- **Sincronização:** fonte mensal (`IMonthlyIndexSource`) e sincronização irmã da do CDI, no mesmo worker, com o mesmo padrão (lock, reconciliação, nunca apaga, valor diferente é revisão, `market_sync_runs` da Fase 1).
- **API:** `GetBalances` e `GetEvolution` ganham o estado do IPCA ao lado de `rates` (só acrescenta); `SaveDeposit` recusa data antes do primeiro índice conhecido.
- **Front:** um 4º chip "IPCA +" em "Rende" (campo "% ao ano, mais o IPCA"), o aviso "inflação até 09/2026" e a nota de que é estimativa pelo contrato, **nunca "valor de venda"**.

## 4. Estratégia de cálculo (candidata, a confirmar na Fase 0)

`Saldo(t) = P × I(t) × R(t)`
- `R(t) = (1 + r)^(du/252)`: o juro real, por dias úteis desde o aporte (a mesma conta do pré-fixado).
- `I(t)`: o fator da inflação, pela convenção F1. A taxa real **não** se soma à inflação: o fator é um produto.
- Convenção (B), em palavras: entre o dia 15 de um mês e o do seguinte, o fator cresce `(1 + IPCA do mês)^(du decorridos / du do período)`, com o IPCA oficial e, para o mês ainda sem IPCA, fator 1.

**Exemplo verificável** (Decimal, IPCA oficial 2026: jun 0,16; jul 0,07; ago −0,32; set 0,82; feriados nacionais e bancários escritos à parte), R$ 10.000 em **IPCA + 6% a.a.**, aporte em 15/06/2026:

| Data | Inflação | Juro real | Saldo |
|---|---|---|---|
| 15/10/2026 (4 meses cheios) | 1,00728633 (cadeia) | `1,06^(86/252)` = 1,02008445 (86 du) | **R$ 10.275,17** |
| 19/10/2026, só IPCA oficial (outubro ainda não saiu) | idem | `1,06^(88/252)` = 1,02055630 | **R$ 10.279,92** |
| 19/10/2026 com projeção de outubro de 0,30% (2/21 du) | idem × 1,00028 | idem | **R$ 10.282,86** (com 0,50%: R$ 10.284,81; a mediana de mercado, 0,32%, dá ≈ R$ 10.283) |

A diferença "só oficial × projetado" é de uns R$ 3 neste caso: cresce com capital alto e com inflação alta. A **razão dos números-índice** no lugar da cadeia muda o primeiro valor em ~R$ 0,007.

**Armadilhas que viram teste:** usar o IPCA acumulado em 12 meses; somar as taxas; aplicar o mês duas vezes (degrau **e** projeção); capitalizar a taxa anual como se fosse mensal; mês ausente lido como zero; arredondamento da cadeia de percentuais; deflação.

**Limite que fica explícito na tela:** o saldo é a **remuneração contratual**, não o preço de mercado (um Tesouro IPCA+ vendido antes do vencimento vale outra coisa). A tela diz "saldo estimado pelo contrato".

## 5. Fases

| Etapa (`PLAN.md`) | O que entrega | Arquivos prováveis | Pronto quando |
|---|---|---|---|
| **R.15, R.16** (Fase 1) | Admin dos dados de mercado (`docs/admin-mercado.md`) | `Features/Admin`, `AuthSetup`, `Me`, `AppLog`, front | Testes e tela conferidos |
| **R.17** (Fase 0, **sem código**) | Extrato real de um CDB IPCA+ (e do Tesouro, se houver) para decidir F1 a F3; confirmar SGS 1737 contra IBGE, F7 no texto legal e o comportamento do BCB (janela, firewall) | este arquivo | F1 a F3 decididas e dois vetores de referência calculados fora do app |
| **R.18** | IPCA persistido: tabela mensal, fonte, sincronização, worker, cobertura, logs, série `ipca` no admin | `Domain/FixedIncome/*`, `Infrastructure/Market/Bcb`, `Features/Rates`, migration, `GlobalTables`, `docs/operacao.md` | Carga e atualização idempotentes provadas com fixtures reais (mês negativo, 404, HTML) |
| **R.19** | Domínio do cálculo: `IpcaPlus`, fator de inflação, IPCA+0, `Series` | `FixedIncomeEngine`, `Remuneration`, `AnnualRate`, novo tipo no namespace do calendário, teste de arquitetura | Vetores independentes, oráculo em BigInteger, mutações provadas e o CDI inalterado |
| **R.20** | API: saldo, evolução, Carteira, estado do IPCA | `GetBalances`, `GetEvolution`, `SaveDeposit`, `Responses`, migration (constraint) | Testes de API com o Postgres real; regressão do CDI |
| **R.21** | Tela e documentos: chip, campo, avisos, `gen:api` | `DepositSheet`, `fixedIncome.ts`, `GoalsTab` | Conferida em 320/390/1280, claro e escuro |
| **Depois** | Tesouro IPCA+, projeção (os resgates já foram feitos: `docs/renda-fixa-resgate.md`) | — | — |

Cada etapa segue o protocolo (um estágio por vez, teste junto, mutação provada, documentação em dia, sem commit sem pedido).

## 6. Riscos

| Risco | Prioridade | Mitigação |
|---|---|---|
| Convenção de apropriação diferente do banco | **Alta** | Fase 0 com extrato; tolerância de R$ 0,01 |
| Cadeia de percentuais arredonda errado | Média | Número-índice (T1) |
| BCB instável (HTML, firewall, timeout) | Alta | Fase 1: disparo manual, histórico com o código do erro; retentativa curta (M5) |
| Inflação aplicada duas vezes ou mês sem dado lido como zero | Alta | Mês ausente é "desconhecido"; testes dedicados |
| Quebrar o CDI | Alta | Regressão com os vetores atuais; a API só acrescenta |
| Migration em produção | Média | Só acrescenta tabela e valor de enum; o constraint do produto já foi tratado na R.13 |

## 7. Critérios de pronto
- A tabela mensal sincroniza do zero e de forma incremental, sem duplicar, e revisão de valor fica anotada (`revised_at`).
- Mês negativo, ausente e não publicado têm teste.
- O saldo bate com **dois extratos reais** (R$ 0,01).
- Os testes de CDI, CDI+, pré e letras continuam iguais.
- A tela mostra "IPCA até <mês>" e o aviso de estimativa pelo contrato, nos dois temas.
- O erro do BCB aparece em pt-BR, com o código.

## 8. Fontes consultadas
BCB SGS 433 (consulta direta); IBGE SIDRA 1737; BCB Olinda Expectativas de Mercado; Suno, "VNA: o que é o Valor Nominal Atualizado e como ele é calculado" (`suno.com.br/artigos/vna`); Investing.com, "Tesouro Direto: Precificação da NTN-B e NTN-B Principal". A página do VNA na ANBIMA Data não carregou o texto.
