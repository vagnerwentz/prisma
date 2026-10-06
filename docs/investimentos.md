# Investimentos — Fase I

> **Status (2026-10-05):** etapas 1 a 5 feitas (seção 8), na branch `feat/brapi-asset-list`, ainda sem commit; as
> etapas estão no `PLAN.md` (Fase I). Este é o documento da fase: as seções 1 a 7 guardam a conversa que levou ao
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
  senha. Falta levar à produção, só com o "pode" do dono: **não por dump** (o
  banco local tem contas de teste, e restaurá-lo apagaria os dados reais da produção). Caminho: publicar a branch (o
  catálogo se preenche ao subir), o dono cria a Íon pela tela e o importador roda contra a produção, com a mesma trava
  (recusa se a Íon já tiver lançamentos). A tabela nunca entra no repositório.
- **Em espera (dono, 2026-10-06), para estudar junto com as operações (I.7):**
  - O mesmo ativo em duas corretoras (BBAS3 na Íon e no BTG). Hoje a carteira (`Holding`) é uma por ativo, sem
    conta; a quantidade e o preço médio vão precisar saber de qual corretora é cada posição.
  - Proventos por ano e por mês. Hoje o valor de cada ativo em "Meus ativos" é a soma de todos os anos ("total
    recebido"), e o topo mostra só o ano corrente; com proventos de 2025, os dois deixam de bater. A ideia é ver os
    proventos separados por ano ou mês e ter outra tela para a soma de todos os anos.
  - Teste da corrida ao lançar provento (dois pedidos que põem o mesmo ativo novo na carteira ao mesmo tempo). O
    código já trata (refaz uma vez); o teste fica para quando a carteira mudar com as corretoras.
- Licença dos ícones da brapi: o dono decidiu seguir sem conferir (2026-10-05).
- Licença de dados de mercado: mostrar cotação a terceiros é redistribuição. Conferir antes de abrir
  cadastro público, junto com a LGPD.
- Token da brapi: a lista não precisa; a cotação (I.7) vai precisar. Fica em User Secrets (`Brapi:Token`) no
  desenvolvimento e em `Brapi__Token` no Railway, nunca no repositório (`docs/operacao.md`, seção 4.1). A brapi já está
  na tabela de stack do `CLAUDE.md`.
