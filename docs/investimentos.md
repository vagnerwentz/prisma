# Investimentos — Fase I

> **Status (2026-10-07):** I.1 a I.6 feitas (branch `feat/brapi-asset-list`); da I.7, a, b1, b2 e c1 feitas na
> `feat/investment-trades`, sem commit; abertas I.7c2, I.7c3 e I.7d. As etapas estão no `PLAN.md` (Fase I). Este é o documento da fase: as seções 1 a 7 guardam a conversa que levou ao
> desenho; a seção 8, a especificação e o que foi feito em cada etapa; a seção 9, o que segue em aberto. As
> decisões do dono estão marcadas com data.

Responde, em parte, à pergunta em aberto do `PLAN.md`: "Acompanhar rentabilidade de investimento ou só
aporte? (Fase 5 ou depois)".

---

## 1. A ideia

Começar pelos **rendimentos** (dividendos, JCP, rendimentos de FII) sem fechar a porta para, no futuro,
saber "quanto já ganhei com BBAS3": rendimentos, lucro de vendas e valorização.

O risco de começar só pelos rendimentos é cadastrá-los como lançamento solto ("Dividendo BBAS3" na
descrição): daqui a um ano seriam centenas de linhas de texto, sem ligação com compra e venda. A saída é
o rendimento nascer ligado a um **ativo** (`Asset`), mesmo antes de existirem operações.

### Três camadas, cada uma útil sozinha

1. **Rendimentos** (primeiro): ativo, tipo (dividendo, JCP, rendimento de FII), data com, data de
   pagamento, valor bruto, IR retido (JCP tem 15%), valor líquido e conta. Responde "quanto BBAS3 me
   pagou na vida" e "quanto de renda passiva entrou este mês".
2. **Operações** (depois): compra e venda no mesmo ativo, com quantidade, preço e corretagem. Daí saem
   posição, preço médio e lucro realizado. Os rendimentos já cadastrados continuam valendo, sem migrar.
3. **Cotação** (por último): ganho não realizado e rentabilidade.

### O que faz o Prisma diferente

Acompanhar carteira sozinho não atrai: Status Invest, Investidor10, Kinvo e Gorila fazem isso de graça. O
que só o Prisma pode fazer é **ligar a renda dos investimentos ao gasto do dia a dia**, com o que o app já
sabe (despesas por categoria, débito automático):

> "Seus proventos de setembro pagaram 38% da conta de luz e da internet."
> "Renda passiva cobre 6% das suas despesas fixas — há um ano era 2%."

Lugar provável: a Análise (`/analise`), não o Resumo.

### Encaixe com o que já existe

- **Duas datas:** data com e data de pagamento seguem o padrão `PurchaseDate`/`SettlementDate`. O caixa
  usa a data de pagamento.
- **Conta de investimento** já existe (`AccountType.Investment`), e o aporte já é transferência. O
  rendimento é a contraparte: dinheiro que o investimento devolve.
- **Dinheiro em centavos**, como tudo.

---

## 2. Fonte dos dados: brapi

A B3 só oferece API em contratos B2B. O arquivo público do boletim diário existe, mas tem layout próprio e
frágil. Escolha em discussão: **brapi.dev**.

- Lista de ativos: `GET https://brapi.dev/api/v2/tickers` ([documentação](https://brapi.dev/docs/tickers)).
  Não exige token. Traz `symbol`, `name`, `longName`, `assetType`, `subType`, `sector`, `subsector`,
  `isActive`, `logoUrl` e um resumo da cotação. **Não traz ISIN nem CNPJ.**
- Cotação: `/api/quote/`, no plano gratuito (15 mil requisições por mês, um ativo por requisição,
  histórico de 3 meses). Os dados detalhados de FII (`/api/v2/fii/*`) são do plano Pro.

### Medido em 2026-10-03

| Tipo | Quantidade |
|---|---|
| `stock` (ações e units) | 781 |
| `fund` (FIIs, ETFs e outros fundos) | 679 |
| `bdr` | 877 |
| **Total** | **2.337** |

- `limit=1000` funcionou: **3 páginas trazem a lista inteira**. O limite máximo não é documentado; o
  código segue `hasNextPage`, sem contar com 1.000.
- Nenhum dos primeiros 1.000 veio com `isActive` falso: a lista parece trazer só ativos em negociação.
  O sinal real de "saiu da bolsa" é **sumir da lista**.
- `assetType` e `subType` podem vir nulos (há `fund` sem subtipo).
- Num FII, o `name` é o próprio código ("MXRF11"); o nome de verdade está no `longName` ("Maxi Renda
  Fundo de Investimento Imobiliario Cotas"). Nas ações, o `name` é o nome em português, em maiúsculas
  ("BCO BRADESCO S.A.").
- `sector` e `subsector` são fracos: faltam em 75 dos primeiros 1.000, FII vem quase sempre como
  "Miscellaneous", MXRF11 aparece como "Logística" (é fundo de papel), e há grafias duplicadas ("Aluguel
  de carros" e "Aluguel de Carros"). **Não guardar.**
- A documentação diz que o `search` encontra também pelo "ticker antigo": a brapi conhece o histórico de
  códigos, mas não o devolve na lista.

### Logos

- Ações e units têm SVG próprio e pequeno (BBAS3: 502 bytes; ITSA4: 2,4 KB).
- Fundos vêm com `logoUrl` apontando para `BRAPI.svg` (o logo da própria brapi); `MXRF11.svg` dá 404.
- Os ícones parecem estar no GitHub Pages. **Conferir a licença do repositório antes de copiar.**

---

## 3. Modelo proposto

### Lista de ativos global

A lista de ativos e as cotações são dados de mercado, iguais para todo mundo: tabelas **sem `UserId`**,
só leitura para o usuário. É a exceção à regra 6 do `CLAUDE.md` aprovada pelo dono em 2026-10-04: a tabela
global fica numa lista fechada (`AppDbContext.GlobalTables`), e o teste de arquitetura `OwnershipTests`
recusa tabela sem dono fora dela. Rendimentos e operações continuam por
usuário e apontam para o ativo global. De quebra, a cotação de um ativo que três pessoas têm é buscada
uma vez só.

### Retorno da brapi (só o que se lê)

```csharp
// Infrastructure/Market/Brapi — formato da brapi, não sai daqui
internal sealed record BrapiTickerPage(
    IReadOnlyList<BrapiTicker> Results,
    BrapiPagination Pagination);

internal sealed record BrapiTicker(
    string Symbol,
    string Name,
    string? LongName,
    string? AssetType,
    string? SubType,
    bool IsActive,
    string? LogoUrl);

internal sealed record BrapiPagination(int Page, bool HasNextPage);
```

### Tipo do ativo no domínio

```csharp
public enum AssetKind
{
    Stock, Unit,                         // ações: têm logo
    Fii, Etf, FiInfra, FiAgro, Fip, Fidc,
    OtherFund,                           // fund sem subType
    Bdr,
    Unknown                              // valor novo da brapi: log Warning
}
```

A tradução das strings da brapi fica no adaptador. O resto do app não vê texto da brapi, e trocar de
fornecedor mexe só nessa tradução.

### Tabelas

**`assets`**

| Coluna | Tipo | Observação |
|---|---|---|
| `id` | `uuid` PK | UUID v7, como as demais entidades (`Entity`) |
| `symbol` | `varchar(12)`, índice único | em maiúsculas; **não** é a chave |
| `name` | `text` não nulo | |
| `long_name` | `text` nulo | nome de verdade do FII |
| `kind` | `text` (enum) | `AssetKind` |
| `inactive_since` | `timestamptz` nulo | quando sumiu da lista; nulo = ativo (`Asset.IsActive`) |
| `created_at`, `updated_at` | `timestamptz` | |

**`asset_logos`**

| Coluna | Tipo | Observação |
|---|---|---|
| `asset_id` | `uuid` PK, FK → `assets` | |
| `svg` | `text` | já limpo |
| `sha256` | `bytea` | não regravar o que não mudou |
| `source_url` | `text` | de onde veio |
| `fetched_at` | `timestamptz` | |

### Por que assim

- **Id nosso, não o código.** O código muda (VIIA3 virou BHIA3). Como chave, mudaria em todos os
  registros de todos os usuários; como campo, muda numa linha. O código é índice único: é por ele que a
  sincronização encontra o ativo.
- **UUID v7, não inteiro.** O v7 já é ordenado pelo tempo (sem a fragmentação do v4). A diferença de
  tamanho (16 contra 4 bytes) é irrelevante nessa escala, e o inteiro seria a única exceção do projeto.
- **Logo em tabela separada.** A sincronização diária carrega os 2.337 ativos rastreados para
  atualizar; com o SVG junto, levaria 1,5 MB de logos todo dia. O logo também tem ciclo próprio (só 781
  têm, baixado depois, pode falhar sozinho) e rota própria com cache longo.
- **ITUB3 e ITUB4** são ativos diferentes (ordinária e preferencial): duas linhas.
- **Fracionário** (ITUB4F) não vira linha: código terminado em "F" é traduzido para o código sem o "F"
  na entrada.
- **Fora por ora:** ISIN (a brapi não dá; acrescentar depois é uma migration de uma coluna), setor
  (dado fraco), índice de busca no nome (2.337 linhas: `ILIKE` basta).

---

## 4. Sincronização diária

Tarefa em segundo plano no molde do `RecurrenceRunner`, **uma vez por dia**, de madrugada (fora do
pregão). Ativo novo na bolsa é raro e ninguém precisa dele na hora. Cerca de 90 requisições por mês.

1. Busca todas as páginas; só depois de **todas** chegarem, grava numa transação só.
2. Para cada item: se o `symbol` ainda não existe na tabela, insere. Se já existe, atualiza nome, nome
   longo e tipo, e volta a marcar como ativo. Ativo sem mudança não é regravado.
3. Quem não veio é marcado inativo, e só se a sincronização foi completa. **Nunca apaga**: pode haver
   rendimento apontando para ele.
4. **Trava de sanidade:** lista com muito menos ativos que o normal (ex.: menos da metade dos ativos
   atuais) não grava nada e registra Warning.
5. Na primeira subida, com a tabela vazia, roda logo ao iniciar.
6. Se a brapi falhar, fica a lista anterior e a próxima execução tenta de novo, com aviso no log.
7. Logos: baixa só das ações e units sem logo, ignorando `BRAPI.svg`. Remove `<script>`,
   `<foreignObject>`, atributos `on...` e referências externas antes de guardar. Uma falha não derruba as
   outras.

### Testes

Sem chamar a brapi de verdade: um JSON de exemplo versionado, como as fixtures de parser da Fase 4.

- Rodar duas vezes não duplica.
- Ativo que some da lista fica inativo e não é apagado.
- Sincronização incompleta (falha numa página) não inativa ninguém.
- Lista muito menor que o normal não grava nada.
- Mudança de nome atualiza a linha existente.
- `subType` desconhecido vira `Unknown`.
- SVG com script é limpo antes de guardar.

---

## 5. Na tela

- **Ação e unit:** logo servido pela API do Prisma (`GET /api/assets/{id}/logo`, cache longo, cabeçalhos
  que impedem o SVG de executar), mostrado como `<img>`, nunca inserido no HTML. A brapi não aparece no
  navegador: ela não fica sabendo o que a pessoa olha, e o logo sobrevive se ela sair do ar.
- **Fundo, BDR e o resto:** o código no lugar do logo, no espírito do monograma que o app já tem: "MXRF"
  em destaque e "11" menor, numa faixa do espectro derivada do código (sempre a mesma cor para o mesmo
  fundo). A regra olha o **tipo**, não o "11" no fim: TAEE11 e SANB11 são units (ações, com logo) e
  BOVA11 é ETF.
- **Nome exibido:** `name` para ação, `long_name` para fundo. Os dois vêm em maiúsculas ou sem acento;
  o ajuste de exibição fica para a tela.

---

## 6. Cotação (camada 3)

- Tabela global, um preço por ativo por dia, só dos ativos que alguém tem.
- A brapi fica atrás de `IQuoteProvider`: trocar de fornecedor é um arquivo.
- A tela nunca chama a brapi diretamente. Com 30 ativos e uma atualização por dia de pregão, são cerca de
  660 requisições por mês.

---

## 7. Casos difíceis anotados

- **Eventos da empresa** (camada 2): desdobramento, grupamento, bonificação (a ITSA4 bonifica com
  frequência) e troca de código mudam quantidade e preço médio sem compra. Preço médio que os ignora
  erra de forma plausível.
- **Troca de código:** a brapi mostra o código novo como ativo novo e o antigo some. É um evento da
  empresa, tratado na camada 2. Na camada 1, cada provento fica no código que valia quando foi pago.
- **Código reaproveitado:** um código que saiu da bolsa pode voltar anos depois para outra empresa; a
  linha antiga seria reativada com nome novo. Raro, visível no log. Não modelado.
- **Imposto de renda** (isenção de R$ 20 mil em vendas, DARF, day trade): produto à parte, fora.

---

## 8. Sequência de etapas proposta

| # | Etapa | Entrega |
|---|---|---|
| 1 | **Cliente da brapi** | Busca a lista inteira e a traduz para o nosso formato (`AssetKind`). Sem banco |
| 2 | **Tabela `assets` + sincronização diária** | A lista na nossa base, com as regras da seção 4 |
| 3 | **Busca de ativos** | `GET /assets?search=` e o campo de escolher ativo na tela |
| 4 | **Logos** | Download, limpeza do SVG, `asset_logos` e a rota do logo |
| 5a | **Carteira** | Tela Investimentos (dentro de Contas): adicionar e tirar os ativos que se tem |
| 5b | **Proventos** | Cadastrar e ver os proventos, ligados ao ativo e à conta onde o dinheiro caiu |

- **Os logos (4) podem esperar:** com a 3, todos os ativos já aparecem com o código no lugar do logo.
  Dá para ir da 3 direto para a 5 e deixar o logo como acabamento.
- **As etapas 1 a 3 são infraestrutura;** o valor visível chega na 5.

### Etapa 1: cliente da brapi — feita (2026-10-03)

Código em `src/Prisma.Api/Infrastructure/Market` (`IAssetListSource`, `MarketDataException`,
`MarketDataSetup`) e `Market/Brapi` (`BrapiAssetListSource`, DTOs, `BrapiAssetKinds`, `BrapiOptions`); no
domínio, `Prisma.Domain/Market` (`AssetKind`, `ListedAsset`). Logs 4000 a 4002 no `AppLog`. Testes em
`tests/Prisma.Api.Tests/Market` e `tests/Prisma.Domain.Tests/Market`, mais o contrato com a brapi real
(`BrapiLiveTests`, só com `PRISMA_LIVE_TESTS=1`).

Achados ao implementar:

- A brapi corta o `limit` em **2.000** sem avisar (pedir 3.000 devolve 2.000 e `totalPages` 2). A
  validação de `Brapi:PageSize` recusa acima disso.
- `sortBy` aceita `symbol`, `name`, `close`, `change`, `volume` e `marketCap`; o cliente pede `symbol`,
  que não muda entre páginas.
- Há código que começa com dígito (`03BK11`) e código sem número (`BDOM`, fundo sem subtipo): a regra do
  código é só "letras e dígitos, até 12".
- Contagem real por tipo, da lista inteira (2.337, gravada pela primeira sincronização em 2026-10-04): BDR
  878, ação 768, FII 336, ETF 257, FI-Agro 37, FI-Infra 18, fundo sem subtipo 16, FIP 14, unit 12, FIDC 1.
- O token vai em `Authorization: Bearer` (documentação da brapi); a lista não exige token, a cotação sim
  (`/api/quote/BBAS3` sem token dá 401).

Planejado antes:

É a **primeira chamada HTTP de saída da API** (hoje o `Prisma.Api` não fala com serviço externo), então
define o padrão para as próximas (cotação, CNPJ da Fase 3).

- **`HttpClient` tipado pelo `IHttpClientFactory`**, que já vem no ASP.NET Core: sem pacote novo.
- **Configuração** em `Brapi:BaseUrl` e `Brapi:Token`. Token em User Secrets e nas variáveis do Railway,
  nunca no repositório. A lista funciona sem token, mas o cliente já nasce preparado para mandar.
- **Timeout curto e explícito**, para uma brapi lenta não prender a tarefa.
- **Paginação segura:** segue `hasNextPage`, com teto de páginas. `hasNextPage: true` para sempre para o
  laço e acusa erro.
- **Tradução isolada:** os DTOs da brapi ficam `internal` na pasta dela; só sai `AssetKind` e o nosso
  formato.
- **Erros:** a tarefa registra Warning e tenta no dia seguinte. Sem Polly ou biblioteca de retentativa:
  rodando uma vez por dia, a próxima execução é a retentativa, e evita dependência nova.
- **Logs** declarados no `AppLog` (seção 6 do `CLAUDE.md`): início, páginas, total, falha.

**Testes**, sem chamar a brapi de verdade (handler HTTP falso com JSONs de exemplo versionados):

- Três páginas viram uma lista só.
- `subType` nulo ou desconhecido vira `OtherFund` ou `Unknown`.
- Teto de páginas atingido vira erro.
- Erro 500 ou JSON inválido vira falha clara, sem lista pela metade.

**Antes de começar:** só o aval do dono para a brapi entrar na tabela de dependências do `CLAUDE.md`
(mesmo sem pacote NuGet, o app passa a depender dela). A exceção à regra 6 só aparece na etapa 2; as
perguntas do "Entrou" e das operações, só na 5.

### Etapa 2: catálogo e sincronização diária — feita (2026-10-04)

- **Domínio** (`Prisma.Domain/Market`): `Asset` (não herda `Entity`: sem dono nem soft delete) e
  `AssetListReconciliation`, que confronta a lista com o catálogo e aplica a trava de sanidade.
- **Banco:** migration `AddAssets`, com índice único `ux_assets_symbol` e duas checagens: `kind` só aceita os
  valores do `AssetKind`, e `symbol` só letras maiúsculas e dígitos.
- **Tarefa** (`Features/Assets`): `AssetListSync` (busca, deixa o domínio decidir, grava numa transação) e
  `AssetListWorker` (4h de São Paulo, `AssetSyncSchedule`; logo ao subir se o catálogo estiver vazio). A
  brapi fora do ar é aviso (`AssetListSyncUnavailable`); o resto é erro. Logs 4003 a 4007. Configuração em
  `Assets:Sync` (`Enabled`, `HourOfDay`). Roteiro de operação em `docs/operacao.md`, seção 4.1.
- **Regra 6:** `AppDbContext.GlobalTables` e o teste `OwnershipTests` (seção 3).
- **Testes:** domínio (reconciliação, com teste de propriedade: aceita a lista, ficam ativos exatamente os
  códigos listados, e nada some do catálogo), integração com Postgres (sincronização, índice e checagens, a
  tarefa ao subir), tarefa e agenda sem banco, arquitetura. Todos provados com o código quebrado de propósito.

Mudança em relação ao planejado: **`inactive_since` no lugar de `is_active` + `last_seen_at`.** Atualizar
`last_seen_at` regravaria os 2.337 ativos todo dia sem nada ter mudado; `inactive_since` só é escrito quando o
ativo some, guarda o mesmo "desde quando" e deixa uma fonte só para "está ativo".

Conferido de ponta a ponta com a API rodando contra a brapi real: tabela vazia, 3 páginas em 1,2 s, 2.337
ativos gravados numa transação, próxima execução agendada para 07:00 UTC (4h em São Paulo). (Na etapa 3, com o
fracionário juntado, passaram a ser 1.962.)

### Etapa 3: busca e campo de escolher ativo — feita (2026-10-04)

- **`GET /assets?q=`** (`Features/Assets/SearchAssets.cs`, `Http/assets.http`): até 20 ativos, por pedaços do
  código ou do nome, sem diferenciar maiúsculas nem acentos, cada palavra em qualquer ordem ("eletrica
  alianca" acha TAEE11). Ordem: código exato, códigos que começam pelo termo, o resto; em cada grupo os
  negociados antes dos que saíram da bolsa, e o código mais curto. Termo vazio devolve lista vazia. O nome
  devolvido é o `Asset.DisplayName`: o da empresa na ação e na unit, o nome longo no fundo e no BDR.
- **Busca sem acento sem extensão do Postgres:** o ativo guarda `search_text` (código e nomes em minúsculas e
  sem acento, `AssetSearch`), refeito quando o nome muda; o termo é normalizado igual. Coluna entrou na
  própria migration `AddAssets` (refeita, porque ainda não tinha ido para a produção).
- **Fracionário (achado ao testar):** a brapi **lista** o fracionário: 375 códigos com o lote padrão junto
  ("ITSA3F" ao lado de ITSA3) e 28 só no fracionário ("BPAR3F", sem BPAR3), inclusive de ação classe B
  ("EQMA3BF"). A reconciliação junta cada um ao lote padrão (`AssetSymbol.BaseOf`): um ativo só, com o código
  do lote padrão, e vale o que o fornecedor diz do lote padrão quando os dois vêm. Catálogo: 1.962 ativos
  (BDR 878, ação 394, FII 336, ETF 257, Fiagro 37, FI-Infra 18, fundo sem subtipo 15, FIP 14, unit 12, FIDC 1).
- **Tela:** `AssetPicker` (`features/investments`), combobox com a lista logo abaixo do campo, setas e Enter
  (Enter sem destaque escolhe o primeiro), resultado anterior esmaecido enquanto o novo chega, "Nenhum ativo
  com …" e, escolhido, o ativo com "Trocar". Ladrilho `AssetTile` com o código (sem logo até a etapa 4). Como a
  tela dos rendimentos só vem na etapa 5, o campo está numa prévia de desenvolvimento, `/dev/ativos`, fora do
  build de produção. Conferido em 320, 390 e 1280 px, claro e escuro, com teclado.
- **Testes:** domínio (normalização, texto de busca que acompanha o nome, nome mostrado por tipo,
  fracionário), integração (ordem, acento, palavras fora de ordem, curinga do LIKE como texto, limite, mesmo
  catálogo para todos, login exigido, fracionário), Vitest (`splitSymbol`). Provados com o código quebrado de
  propósito: 10 mutações, todas pegas.
- **Ponto a observar:** termo curto que casa com o meio de outro nome pode deixar o ativo esperado mais abaixo
  ("maxi" traz MYPK3, Iochpe-Maxion, antes de MXRF11, Maxi Renda); com mais uma palavra ("maxi renda") ele
  fica sozinho. Se incomodar no uso real, dar preferência a nome que começa pelo termo.

---

### Etapa 5: decisões do dono (2026-10-05)

- **Provento é um lançamento de receita na conta onde o dinheiro caiu**, escolhida pela pessoa: conta corrente
  (ex.: Íon, que paga no Itaú) ou conta de investimento (corretora). Não obriga a criar conta de investimento.
  O form lembra a última conta usada. O que o diferencia de uma receita comum é o detalhe do provento: o
  ativo e o tipo. Saldo, "Entrou" e caixa continuam com uma fonte só de verdade.
- **Conta no "Entrou"**, como toda receita (já é assim hoje).
- **Campos:** ativo, tipo (dividendo, JCP, rendimento de FII…), data e valor líquido; mais a conta. Bruto, IR
  e data com ficam de fora.
- **Nos Lançamentos, agrupado:** proventos do mesmo dia viram uma linha ("Proventos · 3 ativos · R$ 154,20"),
  que abre os itens. Não são escondidos: o saldo da conta tem que bater com o banco.
- **Form próprio**, fora do "Novo lançamento" (feito para gasto rápido).
- **Onde fica:** opção A, uma área **Investimentos dentro de Contas** (`/contas/investimentos`), como as
  Recorrências. Pensada já como tela completa; vira aba própria se o uso mostrar que merece.
- **Carteira:** "ativo que eu tenho" é um registro do usuário (`Holding`), separado do catálogo global. Entra
  quando a pessoa adiciona o ativo ou registra um provento dele. **Sem quantidade nem preço médio por
  enquanto:** chegam com as operações (compra e venda), a única fonte confiável do preço médio.
- **Divisão:** 5a (carteira) e 5b (proventos), cada uma revisada sozinha.

Esboço da tela (5a entrega "Meus ativos"; 5b, o total do ano, a soma por ativo e os últimos proventos):

```
Contas › Investimentos
 Proventos em 2026            R$ 1.284,50        (5b)
 Meus ativos                  [+ Adicionar]
 [BBAS 3] BBAS3 · Ação   BCO BRASIL      R$ 412,00 em proventos (5b)
 [MXRF 11] MXRF11 · FII  Maxi Renda      R$ 98,30 em proventos  (5b)
 Últimos proventos            [+ Novo provento]   (5b)
 30/09 · 3 ativos             R$ 154,20
```

### Etapa 5a: carteira — especificação

- **`Holding`** (domínio, herda `Entity`: tem dono, filtro e soft delete): `AssetId`. Um por ativo por usuário
  (índice único `(user_id, asset_id)`, contando os excluídos).
- **`GET /holdings`**: os ativos da carteira, com código, nome mostrado, tipo e se ainda é negociado, em
  ordem de código.
- **`POST /holdings { assetId }`**: põe o ativo na carteira. Se já está, não faz nada; se tinha sido tirado,
  volta o mesmo registro (é o "Desfazer"). Ativo inexistente: 404. Ativo que saiu da bolsa pode entrar
  (pode haver provento antigo dele).
- **`DELETE /holdings/{id}`**: tira da carteira (soft delete). Na 5b, os proventos do ativo continuam no
  histórico.
- **Tela:** cartão "Investimentos" em Contas (sempre visível: é como a pessoa descobre a área); a página
  `/contas/investimentos` com "Meus ativos", "Adicionar" (painel com o `AssetPicker`) e, ao tocar num ativo,
  o painel com "Tirar da carteira" e o aviso com "Desfazer". A prévia `/dev/ativos` sai: o campo passa a morar
  na tela de verdade.
- **Testes:** integração (adicionar, adicionar de novo sem duplicar, tirar, desfazer volta o mesmo
  registro, ativo inexistente, isolamento entre usuários, login exigido, ordem), arquitetura (a tabela nova
  tem dono, pelo `OwnershipTests`).

### Etapa 5a: carteira — feita (2026-10-05)

- **Backend:** `Holding` (`Prisma.Domain/Investments`), migration `AddHoldings` (índice único
  `ux_holdings_user_asset`, chave estrangeira para `assets` e `users`), `Features/Investments` (`ListHoldings`,
  `AddHolding`, `RemoveHolding`), `Http/holdings.http`, logs 4100 e 4101. Pôr de novo um ativo tirado devolve o
  mesmo registro (ignora só o filtro de soft delete); quem perde a corrida do índice único devolve o que o
  outro criou.
- **Tela:** cartão "Investimentos" em Contas (sempre à vista; "N ativos" ou "Ações, FIIs e proventos"), página
  `/contas/investimentos` (pacote próprio, 8 kB) com "Meus ativos", painel "Adicionar ativo" com o
  `AssetPicker` e painel do ativo com "Tirar da carteira" e o aviso com "Desfazer". A prévia `/dev/ativos`
  saiu. Conferido com toque real em 320 e 390 px, claro e escuro: adicionar (digitar e Enter), tirar e desfazer.
- **Testes:** integração (ordem, não duplicar nem no log, corrida determinística com um interceptador que grava
  a mesma carteira por outra conexão antes do `SaveChanges`, tirar e desfazer com o mesmo registro e a mesma
  data, ativo fora da bolsa, ativo inexistente, validação, isolamento entre usuários, login). Provados com 4
  mutações; a da corrida só passou a ser pega sempre com o teste determinístico (com 20 pedidos simultâneos,
  pegava 1 vez em 3).
- **Pacote principal:** +2,25 kB comprimido (128,21 → 130,46 kB), sem código novo: o empacotador juntou ao
  principal `button`, `react-dom` e `clsx`, que antes eram pedaços à parte. Nada da tela nova entrou nele.

### Etapa 5b: proventos — especificação

- **O provento é o próprio lançamento de receita** (`Transaction`, tipo `Income`), com duas colunas a mais:
  `AssetId` (o ativo) e `PayoutKind` (dividendo, JCP, rendimento de fundo). As duas vêm juntas, só em receita
  (checagem no banco). Sem tabela à parte: saldo, "Entrou", caixa, excluir e restaurar seguem como em qualquer
  lançamento. Data da compra = data de caixa = data do pagamento.
- **Conta:** qualquer uma, menos cartão de crédito. **Categoria:** a "Rendimentos" do catálogo padrão
  (`investment-income`), se a pessoa a tiver; senão, sem categoria. **Meio:** TED (é como a corretora paga).
  **Descrição:** vazia; a tela mostra o tipo e o ativo.
- **Carteira:** registrar provento põe o ativo na carteira (ou o devolve, se tinha sido tirado).
- **Endpoints:** `GET /payouts` (todos, do mais recente ao mais antigo, com código, nome e tipo do ativo),
  `POST /payouts` e `PUT /payouts/{id}` (`accountId`, `assetId`, `kind`, `amountCents`, `date`). Excluir e
  desfazer usam os de lançamento (`DELETE /transactions/{id}`, `POST /transactions/{id}/restore`).
- **Proteções:** a edição comum de lançamento recusa provento ("Provento usa a edição de provento"); provento
  não vira série que se repete (o valor muda a cada mês).
- **Lista de lançamentos:** a resposta ganha `assetId`, `payoutKind` e `assetSymbol`. Os proventos do mesmo dia
  na mesma conta viram uma linha ("Proventos · 3 ativos"); tocar abre os itens, e cada item abre a edição.
- **Investimentos:** "Proventos em 2026" (soma do ano pela data), "Novo provento", em cada ativo da carteira o
  total recebido, no painel do ativo a lista dos proventos dele e "Novo provento" já com o ativo, e "Últimos
  proventos" agrupados por dia. Ajuste de 2026-10-06: o valor de cada ativo leva o rótulo "total recebido" (é a soma
  de todos os anos, não só do ano do topo), e o botão "Novo provento" desce para a linha de baixo quando o total do
  ano não cabe ao lado (cortava em 320 px). Testes acrescentados: editar trocando o ativo põe o novo na carteira, e
  provento de ativo que saiu da bolsa é aceito (`PayoutsTests`, provados por mutação).
- **Form do provento:** ativo, tipo (sugerido pelo ativo: fundo → rendimento, ação → dividendo), valor, data
  (hoje, ontem, outra) e conta (a do último provento; senão a primeira conta de investimento; senão a primeira).

### Etapa 5b: proventos — feita (2026-10-05)

- **Backend:** `Transaction.CreatePayout`/`UpdatePayout` e a recusa na edição comum e na série (domínio),
  migration `AddPayouts` (`asset_id`, `payout_kind`, checagem `ck_transactions_payout`), `Features/Investments`
  (`ListPayouts`, `SavePayout`, `Portfolio.Ensure`, compartilhado com `AddHolding`), `PayoutAssets.Fill` na lista
  e no detalhe de lançamentos, `Http/payouts.http`, logs 4102 e 4103. A categoria "Rendimentos" é achada pela
  chave do catálogo (`DefaultCategories.InvestmentIncomeKey`, `income.investment-income`): a pessoa a recebe ao
  abrir o app (`GET /categories`); sem ela, o provento fica sem categoria.
- **Tela:** em Investimentos, "Proventos em 2026", "Novo provento", o total de cada ativo, o painel do ativo
  com os proventos dele e "Novo provento" já com o ativo, e "Últimos proventos" por dia. `PayoutSheet` é um
  painel só para lançar, ver o dia e editar (com "Excluir" e "Desfazer"). Nos Lançamentos, os proventos do dia
  na mesma conta viram uma linha ("Proventos · 3 ativos"), e um só mostra "JCP · BBAS3". `invalidateMoney`
  passou a recarregar proventos e carteira.
- **Achado no teste pela tela:** excluir um provento aberto a partir do dia deixava o painel aberto e sem aviso.
  A exclusão recarrega a lista, o grupo do dia muda, o painel trocava de visão e o form saía da tela, levando
  junto os callbacks do `mutate`. Corrigido guardando a visão ao tocar (não recalculada) e esperando a mutação
  (`mutateAsync`) antes de avisar e fechar.
- **Testes:** domínio (12), integração (15: receita na conta, saldo, "Entrou", categoria, carteira, edição,
  recusas, excluir e desfazer, série, isolamento, login), Vitest (somas, agrupamento por dia e por conta, conta
  sugerida, tipo sugerido). Provados com 11 mutações, todas pegas. Conferido com toque real em 320 e 390 px,
  claro e escuro: lançar, agrupar, abrir o dia, editar, excluir e desfazer.
- **Pacote principal:** +0,25 kB (130,54 kB comprimido); o painel do provento é um pacote à parte (3,4 kB).

### Etapa 4: logos — feita (2026-10-05)

O que mudou em relação à especificação logo abaixo, ao rodar contra a brapi real no mesmo dia:

- **A brapi passou a dar um endereço de logo para todo código** (`MXRF11.svg`, `AAPL34.svg`), e o `BRAPI.svg` sumiu.
  Dos 1.974 ativos, 1.915 vieram com logo. Para fundos, o logo é o **da gestora** (iShares em 152 ETFs e BDRs de ETF,
  BTG em 74 fundos, Itaú, BB, XP…), não o do fundo. O filtro do `BRAPI.svg` continua, para o caso de ele voltar.
- **Decisão do dono:** ação, unit e BDR mostram o logo; **fundo mostra o código**, mesmo com o logo da gestora
  guardado (`logoSrc` no front; a API diz a verdade em `hasLogo`). Os logos das gestoras ficam guardados, se um dia
  forem úteis.
- **Feito:** `Asset.LogoUrl` (só https) e `AssetLogo` (domínio), migration `AddAssetLogos`, `SvgSanitizer`,
  `HttpAssetLogoDownloader` (10 s, até 64 KB, lido em partes), `AssetLogoSync` (4 downloads ao mesmo tempo, depois da
  lista; o que perdeu o logo no fornecedor perde o guardado), `GET /assets/{id}/logo` (CSP, nosniff, ETag, 304), e
  `hasLogo`/`assetHasLogo`/`assetKind` nas respostas. `AssetTile` mostra o logo como `<img>` e volta ao código se a
  imagem falhar. Logs 4008 a 4010.
- **Primeira rodada real:** 1.915 logos, 3 MB, nenhum recusado pela limpeza. Na tela, o navegador só falou com o
  Prisma.
- **Testes:** limpeza do SVG (18: script, foreignObject, image, iframe, animate, set, `on…` até na raiz, link e
  `url()` externos, `javascript:`, outro namespace, comentários, DTD com entidade, HTML no lugar de SVG, tamanho),
  domínio (endereço só https, reconciliação do endereço, fracionário mantém o logo), adaptador (BRAPI.svg vira "sem
  logo"), integração (baixar, limpar e servir; 304; sem logo é 404; falha isolada e tentada de novo; SVG sujo não
  guardado; endereço novo baixa de novo; logo perdido sai; login), tarefa (roda ao subir sem logos; espera a
  madrugada com logos) e Vitest (`logoSrc`). Provados com 12 mutações, todas pegas.

### Etapa 4: logos — especificação (2026-10-05)

- **Licença:** o dono decidiu seguir sem conferir a licença dos ícones da brapi (2026-10-05).
- **Regra de exibição, pelos dados:** tem logo, mostra o logo; não tem, mostra o código (`AssetTile`). Medido em
  2026-10-05: a brapi tem logo para ações (247 códigos, contando fracionários), 62 FIIs, 7 units, 2 ETFs, 2
  Fiagros e 1 BDR; o resto aponta para o `BRAPI.svg` (o logo dela), tratado como "sem logo". Os SVGs são
  quadrados de 56 px com o fundo da marca.
- **Catálogo:** o ativo guarda o endereço do logo (`Asset.LogoUrl`), atualizado pela sincronização diária.
- **Logo guardado:** tabela global `asset_logos` (`asset_id`, `svg` limpo, `sha256`, `source_url`, `fetched_at`),
  na lista de tabelas sem dono (`GlobalTables`). Baixado depois do catálogo, só o que falta ou mudou de
  endereço, e cada falha isolada. Ao subir a API sem nenhum logo guardado, a sincronização roda na hora.
- **Limpeza do SVG** antes de guardar: raiz `<svg>`; fora `<script>`, `<foreignObject>`, `<iframe>`,
  `<object>`, `<embed>`, `<image>` e `<use>` externos; fora atributos `on…` e referências que não sejam internas
  (`#id`); sem DTD; até 64 KB. SVG que não passa é recusado e o ativo segue com o código.
- **Servido pela API:** `GET /assets/{id}/logo`, `image/svg+xml`, `X-Content-Type-Options: nosniff`,
  `Content-Security-Policy` que não deixa o SVG executar nada, `ETag` (o `sha256`) e cache de um dia. Na tela,
  sempre como `<img>`; se a imagem falhar, volta o código. A brapi não aparece no navegador.
- **As respostas** de busca, carteira, proventos e lançamentos dizem se o ativo tem logo, para a tela não pedir
  imagem que não existe.

### I.7 Operações: decisões do dono (2026-10-06)

Desenho conversado a partir de um plano sobre custos e preço médio. As notas de corretagem reais serviram só para
entender os custos: **o Prisma não importa nota** (nada de PDF, OCR ou upload). A pessoa digita a nota.

- **A unidade da entrada é a nota** (`TradeNote`): corretora, data do pregão, as operações (ativo, compra ou venda,
  quantidade, preço) e, embaixo, os custos. É o envelope que diz quais operações dividem quais custos.
- **Custos em dois totais:** taxas (`FeesCents`: liquidação, emolumentos, registro, ISS, transferência, "outros",
  qualquer custo novo) e corretagem (`BrokerageFeeCents`). Pedir cada taxa num campo é fricção sem ganho: para o preço
  médio só importa como cada total se divide. **IRRF não é custo** (é IR antecipado, o "dedo-duro"); fica fora do
  cálculo.
- **"Clearing" não é corretagem.** Na nota da B3, a seção "Clearing" traz a taxa de liquidação e a de registro, que
  se dividem por valor como as outras taxas. A exceção é a **corretagem** (a "taxa operacional"), cobrada por ordem.
  Nome no código: `BrokerageFee`.
- **Corretagem só em ações (ação e unit), salvo marcação na nota inteira:** "esta corretagem vale também para FII,
  ETF e outros" (`BrokerageFeeOnAllKinds`). Marcar por linha foi descartado: fica ruim no celular. Configurar na
  corretora também: a regra muda com o tempo (em 2020 quase todas cobravam, hoje quase nenhuma).
- **Ordem = mesmo ativo + mesma direção + mesma nota.** A nota lista negócios, não ordens; preço diferente
  (`50 × 20,00` e `30 × 20,10`) não separa. Fracionário não separa (no catálogo é o mesmo ativo). As linhas digitadas
  ficam gravadas como estão: a junção existe só no cálculo.
- **A carteira não mexe em dinheiro.** Comprar e vender não alteram saldo de conta nenhuma e não entram em "Entrou"
  nem "Saiu"; lucro de venda não é receita. O dinheiro que vai para a corretora segue como hoje (transferência, o
  "Investido"). Sem isso, as notas desde 2020 exigiriam saldos de contas que não existiam no Prisma. **Sem D+2:** vale
  a data da nota, e o calendário bancário continua só na série e na fatura.
- **Corretora da nota:** escolhida de uma **lista de corretoras** (com o ícone da instituição; "Outra" para digitar),
  não das contas: a nota não mexe em dinheiro e a corretora de 2020 pode não existir mais como conta. Vem preenchida
  com a última usada. O dono lançará o histórico todo como Íon; a corretora só serve para ver "quanto tenho em cada
  uma" no futuro.
- **Preço médio único por pessoa e ativo**, somando as corretoras (é a regra da Receita).
- **A tela mostra a carteira atual e o histórico** de cada ativo, com **dois preços médios**: só o preço (o que se
  compara com a cotação) e com os custos de aquisição (o da Receita e do lucro).
- **Posição calculada, não guardada:** gravam-se as notas; quantidade, preços médios e lucro saem de percorrer as
  notas em ordem de data, como o saldo da conta sai dos lançamentos (`ListAccountBalances`). Corrigir ou excluir uma
  nota antiga refaz tudo o que vem depois, sem código de sincronização. Medir com um usuário de 5.000 operações na
  I.7b; se um dia ficar lento (medido), uma tabela entra como cópia refeita pelo mesmo cálculo, nunca atualizada
  aos poucos.
- **Começa zerado:** o dono digita as notas desde 2020 (cerca de 70). Sem posição inicial.
- **Eventos da empresa um de cada vez**, quando uma nota real esbarrar neles (a venda de mais do que o Prisma acha
  que se tem é recusada, e isso aponta o evento que falta). Ordem provável: bonificação (ITSA4), desdobramento e
  grupamento, subscrição.
- **Day trade ignorado:** compra e venda do mesmo ativo na mesma nota entram como operações comuns, na ordem digitada.
- **Fora, por análise do plano:** banco novo, NoSQL, eventos de domínio, mensageria, Redis e tarefa em segundo plano.
  São poucas pessoas e dezenas de operações por ano; o cálculo é síncrono, numa requisição, sem estado incremental
  para duplicar ou reprocessar.

### I.7a Cálculo: especificação

Só domínio (`Prisma.Domain/Investments/Trades`), sem banco nem tela. Valores em centavos (`long`); conta
intermediária em `Int128`, para `custo × valor` não estourar.

**Nota válida** (`TradeNote.Create`): ao menos uma operação; quantidade e preço maiores que zero; taxas e corretagem
não negativas. Corretagem maior que zero sem nenhuma ordem que a receba é recusada: "A corretagem desta nota não
tem ação para receber. Marque que ela vale também para FII, ETF e outros." Custo maior que o valor da operação é
aceito (operação pequena).

**Valor da operação:** quantidade × preço.

**Rateio das taxas:** entre todas as operações da nota, compras e vendas, proporcional ao valor. Centavo que sobra:
maior resto (`Money.Allocate`), empate para a operação que vem antes. A soma das partes é sempre o total.

| Exemplo | Valores | Taxas | Partes |
|---|---|---|---|
| Proporcional | ITUB3 R$ 8.000, BBSE3 R$ 2.000 | R$ 10,00 | R$ 8,00 e R$ 2,00 |
| Maior resto | R$ 100, R$ 200, R$ 700 | R$ 0,11 | 1, 2 e **8** centavos (exatos: 1,1 · 2,2 · 7,7) |
| Empate | três de R$ 1.000 | R$ 1,00 | 34, 33 e 33 centavos |
| Compra e venda | compra R$ 2.000, venda R$ 3.000 | R$ 1,00 | 40 e 60 centavos |

**Corretagem:** dividida igualmente entre as ordens que a recebem (`Money.SplitInto`, centavo extra para a primeira
ordem na nota); dentro da ordem, entre as linhas, proporcional ao valor (maior resto). Recebem: ação e unit; com a
marcação, todas.

| Exemplo | Nota | Corretagem | Partes |
|---|---|---|---|
| Ordem com duas linhas | ITUB3 C 50 × 20,00 · ITUB3 C 30 × 20,10 · BBSE3 C 20 × 32,00 | R$ 10,00 | ITUB3 R$ 5,00 (3,12 + 1,88), BBSE3 R$ 5,00 |
| FII sem marcação | MXRF11 C 100 × 10,00 · ITUB3 C 10 × 20,00 | R$ 4,90 | MXRF11 zero, ITUB3 R$ 4,90 |
| FII com marcação | a mesma | R$ 4,90 | R$ 2,45 cada |
| Três ordens | três ativos | R$ 10,00 | 3,34 · 3,33 · 3,33 |
| Compra e venda do mesmo ativo | ITUB3 C e ITUB3 V | R$ 10,00 | duas ordens, R$ 5,00 cada |

**Posição** (`PositionCalculator`), por ativo, somando as corretoras: quantidade, **custo bruto** (só preço) e **custo
de aquisição** (preço + custos). Notas em ordem de data (mesma data: a ordem recebida); dentro da nota, ordem a ordem,
na ordem em que aparecem.

- **Compra:** quantidade + q; custo bruto + valor; custo de aquisição + valor + custos.
- **Venda:** recusada se q passa da quantidade ("Venda de 110 em 10/03/2024, mas a carteira tinha 100."). Sai a
  fração q/quantidade de cada custo, arredondada (meio centavo para cima); a venda que zera leva o resto inteiro, e
  a soma do que saiu é sempre o que entrou. **Valor líquido** = valor − custos da venda (pode ser negativo).
  **Resultado** = valor líquido − custo de aquisição que saiu (pode ser prejuízo). Os preços médios do que fica não
  mudam (salvo fração de centavo do arredondamento).
- **Preço médio** = custo bruto ÷ quantidade; **com custos** = custo de aquisição ÷ quantidade; em centavos, meio
  centavo para cima. Sem quantidade, sem preço médio. Zerar e comprar de novo começa do zero.
- **Histórico:** um passo por ordem, com a posição depois dela (quantidade, custos, preços médios) e, na venda, o
  resultado.

| Exemplo | Operações | Esperado |
|---|---|---|
| Custo na compra | C 100 × 20,00, custos R$ 10,00 | aquisição R$ 2.010,00; PM R$ 20,00; com custos R$ 20,10 |
| Ordem com duas linhas | a nota da corretagem acima (taxas zero) | 80 ITUB3; bruto R$ 1.603,00 → PM R$ 20,04 (20,0375); aquisição R$ 1.608,00 → R$ 20,10 |
| Venda total | C 100 × 20,00 sem custos; V 100 × 25,00, custos R$ 10,00 | líquido R$ 2.490,00; resultado R$ 490,00; posição zerada |
| Venda parcial | C 100 × 20,00, custos R$ 10,00; V 30 × 25,00 sem custos | sai R$ 603,00; resultado R$ 147,00; ficam 70, PM R$ 20,00 e R$ 20,10 |
| Arredondamento | C 3 × 10,00, custos R$ 0,01; três vendas de 1 | saem 10,00 · 10,01 · 10,00 (soma R$ 30,01) |
| Duas compras | C 100 × 20,00; C 100 × 22,00 | PM R$ 21,00 |
| Custo maior que o valor | C 1 × 1,00, custos R$ 5,00; V 1 × 1,00, custos R$ 5,00 | com custos R$ 6,00; líquido −R$ 4,00; resultado −R$ 10,00 |
| Zerar e recomprar | C 10 × 20,00; V 10 × 25,00; C 10 × 30,00 | PM R$ 30,00 |
| Venda acima | C 100; V 110 | recusada |

**Testes de propriedade** (CsCheck): `Allocate` soma o total e cada parte fica a menos de um centavo da parte exata;
pesos iguais dão o mesmo que `SplitInto`; o rateio da nota soma taxas + corretagem; comprar e vender tudo em partes
aleatórias zera os dois custos, e a soma dos resultados é o líquido das vendas menos o custo de aquisição.

### I.7a Cálculo: feita (2026-10-06)

- **Código:** `Money.Allocate` (maior resto, em `Int128`) e `Prisma.Domain/Investments/Trades`: `Trade` (linha da
  nota, com código e tipo do ativo), `TradeSide`, `TradeNote` (validação, `Orders`, `AllocateCosts`),
  `PositionCalculator` e `AssetPosition`/`PositionTotals`/`PositionStep`. Sem banco, sem tela, sem pacote novo.
- **A nota é cálculo, ainda não entidade:** a I.7b decide como gravá-la (dono, corretora, soft delete) e entrega ao
  `PositionCalculator` as notas do usuário. O tipo do ativo vem do catálogo na hora do cálculo: se um dia o
  fornecedor reclassificar um ativo, a corretagem de notas antigas pode mudar de lado. Raro; a I.7b decide se guarda
  o tipo na linha.
- **A venda que zera não precisa de caso especial:** `custo × q ÷ q` é exato, então a fração dela já é o custo
  inteiro.
- **Testes (I.7a):** `MoneyTests` (Allocate: exemplos da tabela, 3 propriedades, entradas inválidas), `TradeNoteTests`
  (validação, taxas, corretagem por ordem e por tipo, propriedade da soma), `PositionCalculatorTests` (os exemplos
  da tabela, ordem das notas, histórico, propriedade de vender tudo). Provados com 19 mutações, todas pegas.

### I.7b Notas e carteira: decisões do dono (2026-10-06)

- **Digitada, nunca importada:** nada de PDF ou leitura de nota; a pessoa copia a nota olhando para ela.
- **"Meus ativos":** duas linhas por ativo: código e quantidade em cima; embaixo os dois preços médios ("PM R$ 20,04 ·
  com custos R$ 20,20"); depois o total recebido em proventos.
- **Onde ficam as notas:** no painel do ativo, o histórico (cada passo abre a nota para editar ou excluir); em
  Investimentos, "Últimas notas", como os "Últimos proventos".
- **Tirar da carteira** só com a posição zerada. Zerado, pode tirar, e notas e resultado continuam no histórico.
- **Divisão:** I.7b1 (API) e I.7b2 (tela).

### I.7b1 API: especificação

- **Tabelas:** `trade_notes` (`TradeNote`, herda `Entity`: dono, filtro e exclusão que se desfaz): `broker` (texto, até
  60), `trade_date`, `fees_cents`, `brokerage_fee_cents`, `brokerage_fee_on_all_kinds`. `trades` (linhas, dentro da
  nota: gravadas e trocadas com ela): `asset_id` (chave para `assets`), `asset_kind`, `side`, `quantity`,
  `unit_price_cents`, `line_number`. Checagens: quantidade e preço maiores que zero, custos não negativos.
- **O tipo do ativo fica na linha**, copiado do catálogo ao gravar (e ao editar): a corretagem é dividida pelo que a
  nota era quando foi lançada, e uma reclassificação do fornecedor não muda notas antigas.
- **A corretora é o nome** ("XP", "BTG Pactual", "Íon"); a tela mostra o ícone pelo reconhecimento de marca que já
  existe (`findBrand`). Obrigatória.
- **Toda gravação confere a carteira inteira antes de salvar:** criar, editar, excluir e desfazer a exclusão
  calculam a posição de todos os ativos com a mudança (`PositionCalculator`); se alguma venda passar do que se tinha,
  nada é gravado (400 com a mensagem do domínio). Notas do mesmo dia: na ordem em que foram lançadas.
- **Carteira:** gravar (ou desfazer a exclusão de) uma nota põe os ativos dela na carteira (`Portfolio.Ensure`).
  `DELETE /holdings/{id}` com posição aberta é recusado (409).
- **Endpoints:** `GET /trade-notes` (mais recentes primeiro; `take` opcional), `GET /trade-notes/{id}`, `POST`, `PUT
  /trade-notes/{id}`, `DELETE /trade-notes/{id}`, `POST /trade-notes/{id}/restore`. A nota volta com as linhas (código,
  nome, tipo, logo do ativo) e o custo de cada linha. `GET /positions`: um item por ativo com notas (quantidade, custo
  bruto, custo de aquisição, os dois preços médios, resultado das vendas). `GET /positions/{assetId}`: o histórico.
- **Corrida conhecida:** duas gravações ao mesmo tempo conferem a carteira cada uma sem a outra (uma venda pode passar
  junto com a exclusão da compra de que ela depende). Uma pessoa digitando sozinha: aceito, sem trava.
- **Medição:** usuário com 5.000 operações, tempo de `GET /positions` e de gravar uma nota.

### I.7b1 API: feita (2026-10-06)

- **Código:** `TradeNote` virou entidade (dono, corretora, `Update`, `Restore`) e `Trade` a linha guardada nela, com o
  tipo do ativo; `PositionCalculator.Build` recebe o código de cada ativo para a mensagem. Banco: migration
  `AddTradeNotes` (`trade_notes`, `trades` dentro da nota, checagens de quantidade, preço, custos, tipo e direção).
  API em `Features/Investments`: `SaveTradeNote`, `DeleteTradeNote`, `RestoreTradeNote`, `ListTradeNotes`,
  `ListPositions`, `TradeBook` (as notas e a conferência), `TradeNoteResponse`; `RemoveHolding` recusa com posição
  aberta. Logs 4104 a 4107. `Http/trade-notes.http`. Tipos do front gerados (`api-types.ts`).
- **Achado: o soft delete apagava as linhas.** O EF apaga junto com o dono o que vive dentro dele, e o
  `AppDbContext` só trocava o dono para "excluído": desfazer devolvia a nota vazia. Agora o soft delete mantém as
  partes da entidade (`KeepOwnedParts`). A nota é a única entidade com partes assim.
- **Achado: as linhas voltam do banco em qualquer ordem;** a nota as ordena pelo número antes de usar.
- **Leitura com `GET /positions` em conflito (409)** só se duas gravações ao mesmo tempo passarem uma sem a outra (a
  corrida aceita): a mensagem diz qual venda corrigir.
- **Medição** (Release, Postgres local, 1.667 notas com 5.000 operações, mediana de 15 chamadas depois de aquecer):
  `GET /positions` 48 ms, histórico de um ativo 46 ms, `GET /trade-notes?take=20` 3,5 ms, todas as notas 51 ms,
  gravar uma nota 56 ms. Gravar começou em 226 ms porque acompanhava todas as notas no EF só para conferir; lidas
  sem acompanhamento (só a nota que muda é acompanhada), caiu para 56 ms, com as leituras iguais nas duas rodadas.
  Conclusão: calcular sempre basta; sem tabela de resultado.
- **Testes:** domínio (a nota com corretora, editar troca tudo, editar recusado não muda nada, linhas numeradas),
  integração (`TradeNotesTests`, 22: os dois preços médios, nota não mexe em saldo nem no mês, venda acima recusada sem
  gravar, excluir e desfazer conferindo, editar nota antiga, histórico, ordem no mesmo dia e ao mudar de dia, carteira,
  tirar só zerado, tipo guardado na linha, lista e `take`, validação, 404, isolamento, login). Provados com 14 mutações
  na API, todas pegas; duas só depois de testes novos (ordem no mesmo dia com o relógio avançando, nota que muda de
  dia).


### I.7b2 Tela: especificação (2026-10-06)

Decisão do dono: **operação entra por um mini-form dentro da própria nota** (não num segundo painel).

- **Painel "Nova nota"** (e "Editar nota", com "Excluir" e o aviso com "Desfazer"): corretora em chips (a última
  usada primeiro e já escolhida; "Outra" abre um campo de nome), data do pregão (hoje, ontem, outra), a lista das
  operações incluídas (tocar reabre para corrigir ou tirar), "Adicionar operação" (ativo, compra ou venda,
  quantidade, preço, "Incluir"), taxas e corretagem. A marcação "Vale também para FII, ETF e outros" só aparece com
  corretagem e alguma operação que não seja ação. No fim, o total de compras e de vendas. Recusa da API (venda acima
  da carteira) aparece no próprio painel. O custo de cada linha só é mostrado depois de gravar (vem da API): a tela
  não refaz o rateio.
- **Corretoras da lista:** Íon, XP, BTG Pactual, Rico, Clear, Nubank, Inter, Toro, Ágora, Itaú, Banco do Brasil,
  Bradesco, Santander, Caixa, Safra, Modalmais, Genial, C6 Bank; ícone pelo reconhecimento de marca.
- **"Meus ativos":** com notas, a linha mostra o código e a quantidade ("80 ações", "10 cotas", "5 BDRs"), embaixo
  "PM R$ 20,04 · com custos R$ 20,20" e, se houver, "R$ 412,00 em proventos". Sem notas, como antes (nome e total
  recebido). Zerado: "zerado" no lugar da quantidade.
- **Painel do ativo:** quantidade, os dois preços médios, custo total, resultado das vendas (se houver) e o histórico
  (data, compra ou venda, quantidade, corretora; a posição depois). Tocar num passo abre a nota. "Nova nota" já com o
  ativo e "Novo provento". "Tirar da carteira" só zerado.
- **"Últimas notas"** em Investimentos (as 6 mais recentes), com "Nova nota". Sem notas, um convite curto.
- **Cache:** gravar, excluir e desfazer recarregam notas, posições e carteira (nunca saldos: a nota não é dinheiro).

### I.7b2 Tela: feita (2026-10-06)

- **Código:** `TradeNoteSheet` (lançar e editar a nota, com o mini-form `LineEditor` e o campo de valor pequeno
  `MoneyInput`), `trades.ts` (corretoras, "80 ações"/"10 cotas"/"5 BDRs", os dois preços médios em duas partes, total
  de compras e vendas, título da nota, validação da operação), consultas e mutações em `queries.ts` (`tradeNotesKey`,
  `positionsKey`: gravar recarrega notas, carteira calculada e lista de ativos, nunca saldos). `InvestmentsPage`:
  "Meus ativos" com quantidade e os dois preços médios, painel do ativo com posição, notas e proventos ("Tirar da
  carteira" só zerado), "Últimas notas" e o convite quando não há nota.
- **Última corretora:** guardada no navegador (`prisma.lastBroker`, preferência de tela como o tema); sem ela, a da
  nota mais recente. A lista da API vem por data do pregão, e quem lança o histórico de 2020 em diante não quer a
  corretora da nota mais nova, e sim a da última que digitou.
- **Achado em 320 px:** "com custos R$ 29,35" quebrava no meio do valor; os dois preços médios viraram blocos que
  não se partem, e a linha quebra no "·". O nome do ativo zerado voltou a ser cortado em uma linha.
- **Conferido com toque real** em 390 px (claro e escuro) e 320 px: lançar (incluir operação, "Lançar nota",
  aviso), excluir com "Desfazer" (a nota volta e a quantidade também), venda acima da carteira e exclusão de uma
  compra de que uma venda depende (as duas recusas aparecem no próprio painel). Sem rolagem lateral.
- **Testes:** Vitest (`trades.test.ts`, 21), provados com 7 mutações, todas pegas. Pacote principal sem mudança
  (130,52 kB comprimido); a tela de Investimentos é um pacote à parte (6,5 kB).

### I.7c Eventos da empresa: decisões do dono (2026-10-07)

Ponto de partida: BCFF11, comprado desde 2020, virou BTHF11; o catálogo (brapi, montado em outubro de 2026) só tem o
BTHF11. O mesmo com CVBI11 → PCIP11.

- **O Prisma sabe o que aconteceu; a pessoa não escolhe.** Os eventos (desdobramento, grupamento, conversão; depois
  bonificação e subscrição) formam um **catálogo comum**, como o de ativos: a regra da empresa (proporção, data-com),
  nunca a quantidade de alguém. Cada evento vale sozinho na carteira de quem tinha o ativo na data-com. Sem
  "Recusar" (o Status Invest tem: recusar um desdobramento real deixa a quantidade 8 vezes menor, e o "pendente até
  aceitar" travaria quem lança as notas em ordem, com a venda depois do evento recusada).
- **Pendência só para o que é da pessoa** (I.7c2): conferir na primeira vez que um evento a afeta ("Confere / Não
  confere"; "Não confere" vira aviso para revisar o evento, não o desliga só para ela), dinheiro de frações,
  subscrição exercida ou não.
- **Ativo antigo nasce com o evento, por curadoria.** Quem registra "BCFF11 virou BTHF11" registra o ativo antigo
  junto, já como "saiu da bolsa". Nada de ativo digitado pelo usuário (risco de ativo inventado, levantado pelo
  dono). O trabalho cresce com os eventos da bolsa, não com o número de usuários: cada evento vale para todos.
- **Na busca, o código antigo aparece como ele mesmo,** com o que virou ("BCFF11 · virou BTHF11 em 16/12/2024"). A
  pessoa lança a nota como está no papel; a conversão é do Prisma. Mostrar BTHF11 no lugar (ideia do "placeholder")
  confundiria quantidade e preço.
- **Ativo que falta:** "Não encontrou? Avise a gente" vira um pedido numa fila (I.7c3); a curadoria registra o
  evento e avisa quem pediu.
- **Curadoria, primeiro passo:** um arquivo de eventos versionado no repositório, aplicado ao subir a API (como o
  catálogo). Tela de administração e papel de administrador depois, quando a fila pedir.
- **Pensar para crescer** (correção do dono): os caminhos se comparam pelo comportamento com muitos usuários, não
  pelo tamanho de hoje.
- **Investigado e deixado de lado, por ora: a COTAHIST da B3** (séries históricas, um arquivo por ano, público,
  colunas fixas, com ISIN e o último pregão de cada código). 764 códigos negociados em 2023–2024 não estão no
  catálogo; a extração de 2015, 2023, 2024 e 2026 deu 1.168 códigos em 69 KB (os arquivos brutos, ~2 GB, nunca iriam
  ao banco). Fica como complemento se a fila de ativos faltando crescer; o dono acha que o público começa a investir
  a partir de 2020. Termos de uso da B3 não conferidos.
- **Referências:** o Status Invest mostra o BCFF11 com desdobramento 1:8 (data-com 28/11/2023) e conversão 100 → 80
  BTHF11 (16/12/2024), e aceitou uma compra de BCFF11 em 2026, impossível (o último pregão foi 18/10/2024, pela
  COTAHIST). O TCC do dono (Python, OCR) tinha os mesmos quatro tipos de evento numa linha do tempo recalculada; não
  levar dele: evento com a quantidade da pessoa, bonificação a custo zero (vale o custo atribuído pela empresa),
  incorporação com preço médio fixo e que sobrescrevia a posição do destino, fração em ponto flutuante, venda sem os
  custos dela, corretagem por linha.

### I.7c1 Eventos e ativos antigos: especificação

- **Arquivo** `Infrastructure/Market/corporate-events.json` (dentro da imagem): ativos antigos (código, nome, tipo,
  último pregão) e eventos (chave estável, tipo, código, data, proporção `from:to`, código de destino na conversão,
  fonte). Começa com BCFF11: desdobramento 1:8 com data-com 28/11/2023 e conversão 100:80 em BTHF11 em 16/12/2024
  (números do Status Invest; conferir com o fato relevante).
- **Tabela global** `corporate_events` (na lista `GlobalTables`): chave única, tipo (`Split`, `Conversion`), ativo,
  ativo de destino, data, `from_quantity`, `to_quantity`, fonte. O ativo antigo entra em `assets` já inativo
  (`inactive_since` no dia seguinte ao último pregão).
- **Sincronização** ao subir a API e depois de cada sincronização do catálogo: entra o que é novo, muda o que mudou,
  sai o que saiu do arquivo; evento com ativo desconhecido fica de fora com aviso no log (o catálogo pode ainda não
  ter o destino). Rodar duas vezes não muda nada.
- **Cálculo:** o evento vale no fim do dia da data (quem compra na data-com tem direito), depois das notas do dia.
  - **Desdobramento e grupamento** (`Split`): quantidade × to ÷ from, para baixo; os custos não mudam (o preço médio
    acompanha). A fração que sobra fica sem custo próprio por ora (o dinheiro do leilão é da I.7c2).
  - **Conversão:** a origem zera; o destino soma a quantidade convertida (para baixo) e os dois custos inteiros, sem
    resultado (não é venda). Soma a uma posição que já exista no destino.
  - Sem posição na data, o evento não aparece.
- **Nota de ativo que já virou outro** (data depois da conversão) é recusada: "BCFF11 virou BTHF11 em 16/12/2024.
  Desde então, lance como BTHF11."
- **Histórico:** passos de evento sem nota ("Desdobramento 1:8", "Virou BTHF11", "Veio de BCFF11"), com a posição
  depois.
- **API** (só acrescenta): o passo do histórico ganha `kind`, `relatedSymbol`, `fromQuantity`, `toQuantity`, e
  `noteId`, `broker` e `side` ficam nulos nos eventos; a busca ganha `convertedToSymbol` e `convertedOn`; a carteira
  calculada ganha `isActive`.
- **Tela:** a busca mostra "virou BTHF11 em 16/12/2024"; "Meus ativos" mostra também ativo com posição e fora da
  carteira (o destino da conversão); o histórico mostra os passos de evento.

### I.7c1 Eventos e ativos antigos: feita (2026-10-07)

- **Domínio:** `CorporateEvent` (global, sem dono, como o `Asset`), `CorporateEventKind` (`Split`, `Conversion`),
  `CorporateEventReconciliation` (arquivo × tabela), `Asset.FromHistory`, `SaoPauloTime.StartOf`. O
  `PositionCalculator` recebe os eventos: valem no fim do dia, depois das notas; no mesmo dia, desdobramento antes da
  conversão (ao contrário, o desdobramento cairia no ativo já zerado). Passos de evento no histórico
  (`PositionStepKind`).
- **API:** `corporate-events.json` (recurso dentro da imagem, `EmbeddedCorporateEventSource`), migration
  `AddCorporateEvents` (tabela global em `GlobalTables`, chave única, checagens de tipo, proporção e destino),
  `CorporateEventSync` (ao subir, `CorporateEventStartup`, e depois de cada sincronização do catálogo, no
  `AssetListWorker`), logs 4011 a 4014. Quem perde a corrida no índice único ao subir encontra o trabalho feito. A
  busca diz o que o ativo virou; o histórico traz `kind`, `relatedSymbol` e a proporção; a carteira calculada traz
  `isActive`. Roteiro da curadoria em `docs/operacao.md`, seção 4.2.
- **Tela:** a busca mostra "virou BTHF11 em 16/12/2024" numa linha própria (ao lado do tipo, a data era cortada em
  320 px); "Meus ativos" mostra o ativo recebido numa conversão mesmo fora da carteira (`withPositions`, sem "Tirar da
  carteira"); o histórico do ativo virou "Histórico", com "Desdobramento 1:8 · 110 → 880 cotas", "Virou BTHF11" e
  "Veio de BCFF11", sem seta (não há nota para abrir).
- **O caso do dono, conferido pela tela:** compras de BCFF11 em 30/09/2020 (10 × 90,28) e 02/10/2023 (100 × 72,19)
  terminam em 704 BTHF11, custo R$ 8.121,80, PM R$ 11,54; a compra de BCFF11 em 2026 é recusada no painel com
  "BCFF11 virou BTHF11 em 16/12/2024. Desde então, lance como BTHF11."
- **Números do BCFF11 vieram do Status Invest:** conferir com o fato relevante antes da produção (a `source` no arquivo
  diz isso).
- **Testes:** domínio (`CorporateEventsInPositionTests`, `CorporateEventTests`), integração (`CorporateEventsTests`:
  sincronização, caso do dono, recusa, correção, idempotência, evento pulado, tarefa diária, o arquivo de verdade),
  Vitest (`stepTitle`, `assetStatus`, `withPositions`). Provados com 24 mutações, todas pegas; 3 só depois de testes
  novos (nota no dia da conversão, desdobramento e conversão no mesmo dia, ativo da carteira com posição não repete) e
  1 refeita porque a expressão não tinha casado. Os dois testes do catálogo que contam ativos ganharam uma fonte de eventos vazia: a tarefa
  diária agora também traz os ativos antigos.
- **Incidente na sessão:** ao gerar os tipos do front, a API do dono estava na porta 5151; a minha não subiu e o
  `kill` derrubou a dele. Daqui em diante: conferir a porta antes e derrubar só o processo que eu subi.

## 9. Em aberto

- Operações (compra e venda, quantidade, preço médio): depois da 5b. Depende também de saber se o pai e a
  noiva investem em bolsa (`docs/validacao-premissas.md`).
- **Importar o histórico de proventos (CSV do dono, I.6).** Decidido pelo dono (2026-10-05): todos os proventos do
  CSV, passados e futuros, entram como provento comum na conta **Íon**, na data em que o dinheiro caiu. O dinheiro dos
  antigos já saiu da conta (reinvestido ou gasto) sem gasto lançado; para o saldo continuar igual ao do banco, a soma
  dos proventos de antes da data de corte é **descontada do saldo inicial da Íon**. Nada de saída para zerar: inventaria
  um gasto no mês em que fosse lançada. O "Entrou" de cada mês passado mostra o provento daquele mês, que é verdade.
  Os futuros ficam no saldo previsto até o dia em que caem. Sem código novo; testes: `PayoutsTests`
  (`Future_payout_is_only_in_the_projected_balance_until_its_day` e
  `Past_payouts_offset_by_the_initial_balance_keep_the_bank_balance`). Importação local, pela API, com ensaio sem
  gravar e relatório por ativo e por ano contra o CSV; a produção recebe depois, pelo mesmo importador (ver abaixo). Sem data de corte: a Íon foi
  criada no Prisma em 2026-10-05, sem lançamentos, e todos os proventos são anteriores. **Feito no banco local
  (2026-10-05):** 142 proventos (JCP, rendimentos de fundo e dividendos), conferidos linha a linha contra a tabela do
  dono; o saldo inicial da Íon ficou negativo na soma deles, e o saldo, zero. Valores nunca no repositório. "Rend. Tributado" (correção de
  dividendo pago com atraso) e a venda de fração de ITSA4 entraram como dividendo (decisão do dono). Gravado por um
  importador descartável, fora do repositório, pelo `SavePayout.Handler` agindo como o usuário (`ScopedUser`), sem
  senha. **Na produção desde 2026-10-06**, com o "pode" do dono: não por dump nem pelo importador, mas junto com todos
  os dados do usuário dele (contas, faturas, lançamentos, carteira), copiados do banco local numa transação só e
  conferidos nas duas pontas. A tabela nunca entra no repositório.
- **Em espera (dono, 2026-10-06), para estudar junto com as operações (I.7):**
  - ~~O mesmo ativo em duas corretoras~~ resolvido na I.7 (2026-10-06): a nota tem a corretora; o preço médio é um
    só por pessoa e ativo (regra da Receita), somando as corretoras. Ver "quanto tenho em cada corretora" fica para
    quando alguém pedir; os dados já existem.
  - Proventos por ano e por mês. Hoje o valor de cada ativo em "Meus ativos" é a soma de todos os anos ("total
    recebido"), e o topo mostra só o ano corrente; com proventos de 2025, os dois deixam de bater. A ideia é ver os
    proventos separados por ano ou mês e ter outra tela para a soma de todos os anos.
  - Teste da corrida ao lançar provento (dois pedidos que põem o mesmo ativo novo na carteira ao mesmo tempo). O
    código já trata (refaz uma vez); o teste continua em aberto (a carteira não mudou com as corretoras).
- Licença dos ícones da brapi: o dono decidiu seguir sem conferir (2026-10-05).
- Licença de dados de mercado: mostrar cotação a terceiros é redistribuição. Conferir antes de abrir
  cadastro público, junto com a LGPD.
- Token da brapi: a lista não precisa; a cotação (I.7) vai precisar. Fica em User Secrets (`Brapi:Token`) no
  desenvolvimento e em `Brapi__Token` no Railway, nunca no repositório (`docs/operacao.md`, seção 4.1). A brapi já está
  na tabela de stack do `CLAUDE.md`.
