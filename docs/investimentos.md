# Investimentos — Fase I

> **Status (2026-10-12):** I.1 a I.6 feitas e na `main`; da I.7, a, b1, b2 e c1 feitas na `feat/investment-trades`
> (commits sem push); c2 (conferir eventos), c3, b3 (corretagem por tipo), e (visão da carteira) e c4 (liquidação paga em
> cotas) feitas e com commit (2026-10-12), sem push. O dono lançou todas as notas que tem (2026-10-12). Aberta a I.7d. As etapas estão no `PLAN.md` (Fase I). Este é o documento da fase: as seções 1 a 7 guardam a conversa que levou ao
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
  ETF e outros" (`BrokerageFeeOnAllKinds`). *Substituída em 2026-10-08 pela escolha por tipo ("I.7b3").* Marcar por linha foi descartado: fica ruim no celular. Configurar na
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
- **Números do BCFF11 vieram do Status Invest;** o dono os aceitou por ora (2026-10-08).
- **CVBI11 → PCIP11, acrescentado pelo dono (2026-10-08):** conversão 1:1 em 24/09/2025, último pregão na mesma data.
  Foi só troca de nome e código do mesmo fundo, sem mudar a carteira (conferido em notícias da Suno e do FIIs.com.br).
- **Testes:** domínio (`CorporateEventsInPositionTests`, `CorporateEventTests`), integração (`CorporateEventsTests`:
  sincronização, caso do dono, recusa, correção, idempotência, evento pulado, tarefa diária, o arquivo de verdade),
  Vitest (`stepTitle`, `assetStatus`, `withPositions`). Provados com 24 mutações, todas pegas; 3 só depois de testes
  novos (nota no dia da conversão, desdobramento e conversão no mesmo dia, ativo da carteira com posição não repete) e
  1 refeita porque a expressão não tinha casado. Os dois testes do catálogo que contam ativos ganharam uma fonte de eventos vazia: a tarefa
  diária agora também traz os ativos antigos.
- **Incidente na sessão:** ao gerar os tipos do front, a API do dono estava na porta 5151; a minha não subiu e o
  `kill` derrubou a dele. Daqui em diante: conferir a porta antes e derrubar só o processo que eu subi.

### I.7c3 Ativo faltando: decisões do dono (2026-10-08)

Ponto de partida: a pessoa busca um código antigo (CVBI11, antes de a curadoria o trazer) e lê só "Nenhum ativo com
"CVBI11"."; não há o que fazer. As oito recomendações abaixo foram aceitas pelo dono.

1. **O pedido é só o código.** Sem texto livre: a curadoria pesquisa de qualquer jeito, e texto livre atrai dado
   pessoal e pede moderação com muitos usuários.
2. **Um pedido por pessoa; a fila agrupa por código** ("CVBI11 · 37 pedidos · primeiro em 02/10"). O pedido de cada um
   é o que permite avisar cada um; o trabalho da curadoria cresce com os códigos diferentes, não com os usuários, e a
   contagem diz o que fazer primeiro.
3. **Limites:** código no formato da bolsa; um pedido aberto por pessoa e código; até 20 esperando por pessoa; limite
   de envios por minuto; código que já está no catálogo não vira pedido.
4. **"Chegou" é calculado, nunca gravado:** o pedido está resolvido quando o código existe no catálogo, venha da brapi
   ou do arquivo da curadoria. Sem tarefa em segundo plano (como a posição, I.7a).
5. **O aviso fica na tela de Investimentos** ("CVBI11 chegou · Lançar nota"), não no sino: o sino é o que falta
   conferir (o "Confere / Não confere" da I.7c2 vai para lá). E-mail e notificação no celular, depois.
6. **A fila, por ora, é uma consulta pronta** no `docs/operacao.md` e uma linha de log a cada pedido (só o código). A
   tela de administração vem quando a fila pedir; ela exige papel de administrador, que não existe.
7. **Código que nunca vai entrar** (erro de digitação, ativo de fora da bolsa) fica esperando, e a pessoa pode
   cancelar. Recusa com motivo vem com a tela de administração.
8. **O "Não confere" da I.7c2 tem tabela própria** (fala de um evento, não de um código) e entra na mesma consulta da
   curadoria.

### I.7c3 Ativo faltando: especificação

- **Domínio:** `AssetRequest` (herda `Entity`: dono, filtro, soft delete), com `Symbol` e `CreatedAt`.
  `AssetRequest.Create(userId, typed)` normaliza (sem espaços, maiúsculas, sem o "F" do fracionário,
  `AssetSymbol.BaseOf`) e recusa fora do formato da bolsa (raiz de 4 caracteres, 1 ou 2 dígitos, letra da classe
  opcional: `^[A-Z0-9]{4}[0-9]{1,2}[A-Z]?$`) com "Digite o código do ativo, como CVBI11.". `AssetRequest.MaxWaiting = 20`.
- **Tabela** `asset_requests` (com dono, fora de `GlobalTables`): `user_id`, `symbol` (até 12), auditoria e
  `deleted_at`. Índice único (`user_id`, `symbol`) contando os cancelados: avisar de novo devolve o mesmo registro, como
  a carteira (`Holding`). Sem FK para `assets`: o pedido existe justamente porque o ativo não está lá.
- **Estado, calculado na leitura:**
  - **Esperando:** o código não está no catálogo.
  - **Chegou:** o código está no catálogo (ativo ou inativo) e a pessoa não tem nota com ele.
  - **Resolvido:** o código está no catálogo e a pessoa já tem nota com ele (de qualquer data). Não aparece mais.
- **`POST /asset-requests`** `{ symbol }`:
  - Código que já está no catálogo: 409 "CVBI11 já está no Prisma. Busque de novo." (nada gravado).
  - Pedido aberto do mesmo código: devolve o existente (200), sem gravar outro; a tela mostra "Você já avisou em
    02/10.". Cancelado antes: volta o mesmo registro. Corrida no índice único: devolve o que ganhou.
  - 20 esperando: 409 "Você tem 20 avisos esperando. Cancele um ou espere algum chegar." Pedido que já chegou não
    conta.
  - Limite de envios por pessoa (política nova no `AuthSetup`, partição pelo usuário, 10 por minuto).
  - Log `AssetRequested` (Information, id 4108): id do pedido e código. O código não é dado pessoal. Cancelar e
    restaurar: `AssetRequestRemoved` (4109) e `AssetRequestRestored` (4110).
- **`GET /asset-requests`:** os pedidos esperando e os que chegaram, do mais novo ao mais velho. Cada um traz id,
  código, data e estado; o que chegou traz também o ativo (id, nome, tipo, logo, se ainda negocia), para abrir a nota
  com ele.
- **`DELETE /asset-requests/{id}`** (cancelar o que espera ou dispensar o que chegou) e
  **`POST /asset-requests/{id}/restore`** (o "Desfazer" do aviso). Restaurar um que espera respeita o limite de 20.
- **Tela, na busca de ativo** (`AssetPicker`):
  - Nada encontrado e texto no formato de código: "Nenhum ativo com "CVBI11"." e a ação "Avisar que falta".
    Depois do toque: "Avisado. Quando CVBI11 chegar, aparece em Investimentos."
  - Pedido já feito: "Você já avisou em 02/10." no lugar da ação.
  - Texto fora do formato (busca pelo nome, "VBI CRI"): a mensagem de hoje e a dica "Para avisar que falta, digite o
    código, como CVBI11."
- **Tela de Investimentos:** bloco que só aparece com pedidos.
  - Chegou: "CVBI11 chegou" com "Lançar nota", que abre a nota com o ativo escolhido, e "Dispensar".
  - Esperando, no fim da tela, em "Esperando chegar": "CVBI11", "Avisado em 02/10" e "Cancelar".
  - Cancelar e dispensar têm "Desfazer" no aviso.
- **Curadoria** (`docs/operacao.md`, seção nova 4.3): consulta da fila, agrupada por código, com a contagem e o
  primeiro pedido, só dos que esperam; o filtro do log `AssetRequested`. Trazer o ativo continua sendo o roteiro da
  seção 4.2 (o arquivo de eventos) ou a sincronização da brapi.
- **API só acrescenta:** endpoints novos, nenhum campo mudado.
- **Testes:**
  - Domínio: normalização (espaços, minúsculas, "CVBI11F" → "CVBI11") e formato (aceita "BBAS3", "TAEE11", "EQMA3B";
    recusa "", "CVBI", "CVBI111", "VBI CRI").
  - Integração:
    - criar;
    - código do catálogo (ativo e inativo) recusado;
    - repetido devolve o mesmo;
    - limite de 20, sem contar os que chegaram;
    - o pedido passa a "chegou" quando o ativo entra (pela curadoria e pela brapi) e some depois da nota;
    - cancelar e restaurar (o mesmo registro), e restaurar respeitando o limite;
    - dois usuários não veem os pedidos um do outro, e a consulta da curadoria conta os dois;
    - limite por minuto.
  - Vitest: quando mostrar a ação, a dica ou "já avisou".
  - Tela conferida em 320, 390 e 1280 px, claro e escuro.

### I.7c3 Ativo faltando: feita (2026-10-08)

- **Domínio:** `AssetRequest` (com dono, como a carteira), `Normalize` e `Create`; testes `AssetRequestTests`.
- **API:** `RequestAsset`, `ListAssetRequests`, `RemoveAssetRequest`, `RestoreAssetRequest` em `/asset-requests`;
  `AssetRequestBook` calcula o estado (esperando, chegou; resolvido sai da lista). Migration `AddAssetRequests`;
  política de limite `asset-requests` por pessoa (`RateLimiting:AssetRequests:PermitLimit`, 10 por minuto); logs 4108 a
  4110. Fila da curadoria em `docs/operacao.md`, seção 4.3, com a mesma consulta do teste. `Http/asset-requests.http`.
- **Tela:** na busca de ativo (`AssetPicker`, também em "Adicionar ativo" e no provento), "Avisar que falta", "Avisado.
  Quando CVBI11 chegar, aparece em Investimentos.", "Você já avisou em 08/10." ou a dica do código. Na tela de
  Investimentos (`AssetRequestList`), o que chegou fica no alto com "Lançar nota" (a nota abre com o ativo) e
  "Dispensar"; o que espera, no fim, em "Esperando chegar". Cancelar e dispensar têm "Desfazer". A nota lançada
  recarrega os avisos (o que chegou sai).
- **Testes:** domínio (19 casos de formato e normalização), integração (`AssetRequestsTests`, 14: formato, catálogo
  ativo e inativo, repetido, limite sem os que chegaram, chegada pela brapi e pela curadoria, saída depois da nota,
  cancelar e desfazer, limite no desfazer, isolamento e a fila, limite por minuto por pessoa), Vitest
  (`assetRequests.test.ts`, 24: o mesmo formato da API, a ação certa, a data, o ativo para a nota). Provados com 15
  mutações, todas pegas; 1 só depois de acertar o teste (os dois pedidos da ordem tinham o mesmo horário). Um caso do
  teste de domínio estava errado e foi corrigido pela especificação: "CVB11" tem formato válido (raiz "CVB1", como
  "B3SA3").
- **Conferido pela tela** em 390 claro, 320 escuro e 1280, com toque real: avisar, já avisado, dica, "Lançar nota",
  dispensar e cancelar com "Desfazer". Achado: os botões ao lado do nome o espremiam em 390 px; foram para uma linha
  própria.
- **Em aberto:** a nota aberta pelo aviso mostra o CVBI11 como "saiu da bolsa", sem o "virou PCIP11" que a busca
  mostra (o aviso não traz a conversão).

### I.7b3 Corretagem por tipo: decisão do dono e especificação (2026-10-08)

Caso real do dono: nota com venda de XINA11 (ETF) e compra de MXRF11 (FII), com corretagem só no ETF. A marcação da
nota inteira não servia: sem ela a nota era recusada (nenhuma ação), com ela o FII levava metade da corretagem.

- **Decisão:** a nota diz em quais **tipos** a corretagem foi cobrada ("Corretagem cobrada em: Ação, ETF, FII"),
  caixas de seleção que a pessoa marca e desmarca. Por tipo, não por ordem: é como a corretora cobra, acompanha a
  edição da nota (outro ETF incluído já entra) e cabe no celular. Ação e unit são um tipo só ("Ação").
- **Aparecem só os tipos que estão na nota.** Nota só com ações: nada aparece. **Vem marcado "Ação"** (a regra de
  antes); nenhum é obrigatório, mas corretagem maior que zero sem ordem de tipo marcado é recusada: "Marque em quais
  ativos a corretagem foi cobrada."
- **Divisão igual a antes:** em partes iguais entre as ordens dos tipos marcados (centavo extra para a primeira) e,
  dentro da ordem, pelo valor.
- **Domínio:** `TradeNote.BrokerageFeeKinds` (lista de `AssetKind`, sem `Unit`: unit conta como `Stock`;
  `Trade.BrokerageKind`). `TradeNote.StockOnly` é o padrão. Sem corretagem, a escolha fica guardada como veio.
- **Banco:** coluna `brokerage_fee_kinds` (`text[]`, nomes dos tipos, checagem dos nomes válidos, sem `Unit`) no lugar
  de `brokerage_fee_on_all_kinds`. Notas existentes: marcação ligada vira todos os tipos; desligada, só `Stock`. O
  rateio delas não muda.
- **API (só acrescenta):** o pedido ganha `brokerageFeeKinds` (opcional) e `brokerageFeeOnAllKinds` passa a opcional.
  Sem `brokerageFeeKinds`: a marcação antiga decide (ligada: todos; desligada ou ausente: só ação), como uma aba aberta
  antes do deploy manda. A resposta traz `brokerageFeeKinds` e mantém `brokerageFeeOnAllKinds`, calculado: ligado
  quando há operação que não é ação e todos os tipos da nota estão marcados.
- **Tela:** caixa de seleção nova no padrão do Prisma (`Checkbox` em `components/ui`, e `CheckChip` nos campos): vazia,
  só o contorno; marcada, o espectro frio com o visto branco, e o chip com o anel do espectro (o do item escolhido).
- **Testes:**
  - Domínio: o caso do dono (corretagem toda no XINA11; o MXRF11 só com a parte das taxas), unit conta como ação, tipo
    marcado ausente da nota não recebe, recusa sem tipo que receba, normalização (unit vira ação, sem repetição), a
    tradução da marcação antiga, e a propriedade da soma com tipos sorteados.
  - Integração: o caso do dono pela API; nota antiga sem `brokerageFeeKinds` (marcação ligada e desligada) dá o mesmo
    rateio de antes; os tipos voltam na leitura e na edição; a migração converte a marcação antiga.
  - Vitest: quais tipos aparecem, o padrão, o rótulo.

### I.7b3 Corretagem por tipo: feita (2026-10-08)

- **Domínio:** `TradeNote.BrokerageFeeKinds`, `StockOnly`, `AllKinds`, `BrokerageKindOf`, `BrokerageKindsFrom`;
  `Trade.BrokerageKind` no lugar de `IsStock`. Mensagem nova: "Marque em quais ativos a corretagem foi cobrada."
- **API e banco:** migration `BrokerageFeeKinds` (converte antes de tirar a coluna antiga; volta perdendo detalhe). O
  pedido aceita `brokerageFeeKinds` e o antigo `brokerageFeeOnAllKinds`; a resposta traz os dois. Tipo desconhecido não
  passa da leitura do JSON (enum só por nome); a lista tem até 20 itens.
- **Tela:** `Checkbox` (`components/ui/checkbox.tsx`) e `CheckChip` (`features/transactions/fields.tsx`), sobre o
  Checkbox do Radix (já no pacote `radix-ui`, sem dependência nova); `.check-box` e `.check-mark` no `index.css`. Na nota,
  "Corretagem cobrada em" com os tipos da nota, e a recusa antes de mandar.
- **Testes:** domínio (`TradeNoteTests`: o caso do dono, cada tipo sozinho, dois tipos, tipo ausente, unit como ação,
  normalização, tradução da marcação antiga, propriedade da soma com tipos sorteados), integração (`TradeNotesTests`: o
  caso do dono com a carteira, marcação antiga ligada e desligada, pedido sem campo nenhum, leitura e edição, tipo
  desconhecido, lista longa, resposta da nota só de ações; `BrokerageFeeKindsMigrationTests`: a conversão num banco
  migrado até antes), Vitest (`trades.test.ts`). Provados com 16 mutações: 15 pegas, 3 delas só depois de testes novos (a
  resposta da nota só de ações, o limite da lista, a escolha à vista com tudo marcado); a que sobrou mostrou código
  morto, a regra `IsInEnum`, que nunca era alcançada e saiu.
- **Conferido pela tela**, com toque real, em 390 claro (só ETF e FII, sem nada marcado e com ETF) e 320 escuro (ação
  marcada de início, recusa sem nada marcado, nota lançada com só ETF: R$ 4,90 todos no XINA11). As 36 notas do banco
  local convertidas numa cópia: 2 com a marcação ligada viraram todos os tipos, 34 só ação.

### Casos reais da curadoria e o que cada um pede (2026-10-08 e 09)

Testes do dono com ativos que saíram da bolsa. O arquivo `corporate-events.json` tem hoje BCFF11, CVBI11, PETZ3,
CRFB3, ENBR3 e IRDM11 em `historicalAssets`; as conversões CVBI11 → PCIP11 e PETZ3 → AUAU3; as liquidações BCFF11 →
BTHF11 e IRDM11 → IRIM11 (I.7c4); e os desdobramentos BCFF11 1:8 (28/11/2023), SNAG11 1:10 (posição de 01/08/2023),
CPTS11 1:10 (data-com 25/09/2023) e BBAS3 1:2 (data-com 15/04/2024). Os três últimos foram acrescentados pelo dono em
2026-10-12, ao lançar as notas, e conferidos contra elas: as compras antes e depois de cada data têm preços na
proporção do desdobramento.

- **PETZ3 → AUAU3** (conversão 1:1 em 02/01/2026, dado do dono): o caminho inteiro funcionou com o dono, do aviso
  "Avisar que falta" até o AUAU3 na carteira. **Parte em dinheiro (R$ 0,7109 por ação) não entra no cálculo:** a
  conversão só troca ações. O AUAU3 recebe o custo inteiro e o dinheiro some. Decidir junto da I.7c2 (dinheiro de
  frações), confirmando a regra da Receita (reduz o custo ou gera resultado). Conferir também se 02/01/2026 é a data-com.
- **Tropeço achado pelo dono:** a nota começa em "Hoje". Uma nota de PETZ3 com a data de hoje é recusada ("PETZ3 virou
  AUAU3 em 02/01/2026. Desde então, lance como AUAU3."), e o aviso só vem depois de preencher tudo. Melhorias
  propostas, não feitas: dizer "notas até 02/01/2026" ao escolher um ativo que virou outro, e abrir sem a data de hoje
  quando a nota vem de "Lançar nota" do aviso. Também em aberto: a nota aberta pelo aviso mostra "saiu da bolsa", sem
  o "virou AUAU3" da busca.
- **Explicação que o dono pediu (para a tela e para quem perguntar):** a conversão não é feita nota por nota nem
  gravada. A carteira é recalculada a cada leitura, juntando as notas e os eventos em ordem de data; a troca acontece
  uma vez, sobre o total que a pessoa tinha na data-com. As notas continuam como estão no papel.
- **CRFB3 (Carrefour Brasil)**: último pregão em 30/05/2025; cada acionista escolheu, até 12/05/2025, a Classe A (R$ 8,50
  em dinheiro por ação, o padrão de quem não escolheu), a B (R$ 4,25 + BDRs do Carrefour francês) ou a C (só BDRs).
  Está só em `historicalAssets`, sem evento: a carteira mostraria as ações para sempre. **Proposta:** evento com
  opções e a padrão (A), valendo sozinho, e o sino perguntando a opção da pessoa (I.7c2). **Hoje:** quem ficou na A
  lança uma venda a R$ 8,50 no dia do pagamento.
- **IRDM11 → IRIM11 (liquidação, não conversão)**: último pregão em 23/10/2025, liquidação em 18/11/2025. Por cota,
  R$ 78,09312078: R$ 73,78706886 em cotas do IRIM11 (0,884794245 cota; R$ 83,3946076 por cota do IRIM11), entregues a
  partir de 02/12/2025, e R$ 4,306051918 em dinheiro, em 09/12/2025. IR de 20% retido sobre o ganho (R$ 78,09 menos o
  custo que cada cotista informou ao administrador; quem não informou ficou com o custo padrão de R$ 57,04, segundo as
  notícias). **Para a Receita é venda e compra:** tratar como conversão levaria o custo antigo ao IRIM11 e esconderia
  o ganho tributado. **Hoje, com notas:** venda das IRDM11 a R$ 78,09 em 18/11/2025 e compra das IRIM11 que entraram
  (o número do extrato) a R$ 83,39 em 02/12/2025, sem custos; o IR retido fica de fora (não é custo). **Depois, se
  valer automatizar:** evento "Liquidação" com a proporção em inteiros (1.000.000.000 : 884.794.245), o valor de saída
  por cota e o preço de referência do destino; o Prisma já sabe o preço médio de cada um, que é o que o administrador
  pediu. O JSON proposto pelo GPT (`"kind": "Liquidation"`, com bloco de imposto) quebrava a leitura do arquivo
  inteiro (o teste `Embedded_file_is_valid` acusou) e foi tirado; o bloco de imposto é processo do administrador, não
  regra da carteira.
- **Subscrição** (recomendação, falta o dono confirmar): é uma compra para o preço médio. A pessoa lança uma nota de
  compra do código normal (não o recibo nem o direito), na data em que as cotas entraram, ao preço da subscrição, sem
  custos. Sai da lista da I.7c2. Casos raros para depois: venda do direito (custo zero), direito comprado de terceiros
  (custo = direito + subscrição), sobras (outra nota).
- **Ideia do dono, "ativo como se existisse + aba de inconsistências":** a primeira metade já existe (o ativo antigo em
  `historicalAssets` existe no Prisma, inativo, e aceita notas); a aba de inconsistências é a I.7c2. Continua fora: a
  pessoa criar o ativo sozinha (risco de ativo inventado).
- **Produção:** a imagem migra ao subir (`Database__MigrateOnStartup`); as migrations `AddAssetRequests` e
  `BrokerageFeeKinds` entram sozinhas no deploy. Como a segunda tira uma coluna, a versão antiga pode dar erro por
  alguns segundos durante a troca no Railway. Conferir o backup da véspera antes.

### I.7e Visão da carteira: decisões do dono e especificação (2026-10-09)

Pedido do dono: "não tenho um summary dos meus investimentos, tipo, quanto tenho investido total, por categoria, FII,
stock e tudo mais", numa visualização "muito foda". E filtros: "ver como era minha carteira em 2020", por ano, e os
proventos com filtro.

- **Decisões (2026-10-09):**
  - A tela de Investimentos ganha **três abas: Carteira, Proventos e Notas**. A aba e o ano ficam no endereço
    (`?aba=proventos&ano=2020`), para voltar e compartilhar o ponto.
  - **Por ano, em todas as abas.** O ano escolhido vale para as três. O ano corrente é "hoje".
  - **Grupos:** unit fica com as ações; FI-Infra, FI-Agro, FIP, FIDC e outros fundos ficam em "Outros fundos"; o tipo
    desconhecido, em "Outros". Grupos: Ação, FII, ETF, BDR, Outros fundos e Outros.
  - **Sem cotação por ora** (a I.7d fica para depois, decisão em aberto). Tudo é pelo **custo**: o que saiu do bolso. A
    tela fica pronta para receber "valor hoje" sem redesenho.
- **O espectro da carteira** (a assinatura da tela): a luz entra no prisma e sai dividida em faixas, uma por grupo. A
  espessura de cada faixa é o peso do grupo. Cada grupo tem uma cor fixa do espectro, a mesma no espectro, na legenda e
  na barra de peso de cada ativo: FII violeta, Ação azul, ETF ciano, Outros fundos lima, BDR âmbar e Outros rosa.
  Tocar numa faixa (ou na linha da legenda) filtra a lista de ativos por aquele grupo; tocar de novo volta a tudo. A
  faixa menor ainda aparece (espessura mínima), para o grupo pequeno não sumir. A luz atravessa o prisma uma vez,
  quando os dados chegam; trocar de ano não anima (mantém o anterior esmaecido, `StaleFade`). Sem movimento, já aparece
  pronta.

**Regras (domínio, com teste antes):**

1. **A carteira em um ano** é a posição **no fim do dia 31/12** daquele ano, depois das notas e dos eventos do dia. No
   ano corrente, no fim do dia de **hoje** (`IClock.Today`): nota com data futura ainda não conta. É o último passo do
   histórico de cada ativo até a data (`AssetPosition.On`). Exemplo do dono: com BCFF11 comprado em 2020 e convertido
   em BTHF11 em 16/12/2024, a carteira de 2023 mostra BCFF11 (já desdobrado 1:8 em 28/11/2023) e a de 2024 mostra
   BTHF11.
2. **Investido** é a soma do **custo de aquisição** (preço + custos) dos ativos com quantidade na data. É o que saiu
   do bolso pelo que ainda se tem.
3. **Peso** em centésimos de ponto percentual (`10000` = 100%), do custo de cada grupo e de cada ativo sobre o total.
   A soma dá **exatamente 100%** pelo maior resto (a sobra vai para quem perdeu mais no arredondamento; empate, para o
   que vem antes na lista). Sem nada investido, tudo é zero. Os pesos dos ativos de um grupo podem somar 0,01 ponto a
   mais ou a menos que o peso do grupo, porque cada lista fecha 100% por si.
4. **Proventos do ano** na Carteira: os que caíram no ano **até a data** (no ano corrente, até hoje). O que tem data
   futura é "a receber" e aparece na aba Proventos.
5. **Resultado das vendas do ano:** a soma do resultado das vendas com data no ano (até a data). Fica no ativo e no
   grupo mesmo que o ativo tenha sido vendido inteiro.
6. **Quem entra na lista:** ativo com quantidade na data, e também o que, sem quantidade, teve provento ou venda no
   ano (peso zero). Assim a carteira de 2022 mostra o que foi vendido em 2022.
7. **Anos que se escolhem:** do primeiro ano com nota ou provento até o ano corrente. Ano depois do corrente é recusado:
   "Escolha um ano até {ano corrente}." Antes do primeiro ano, a carteira vem vazia.

**API (só acrescenta):**

- `GET /portfolio?year=2020` (sem `year`: o ano corrente). Devolve o ano, a data da foto (`asOf`), os anos que se
  escolhem, o total investido, os proventos e o resultado das vendas do ano, os grupos (custo, peso, quantos ativos,
  proventos e resultado) do maior para o menor, e os ativos (quantidade, custos, preços médios, peso, proventos e
  resultado do ano) do maior para o menor, empate pelo código. Notas que não fecham: 409, como `/positions`.
- `GET /trade-notes?year=2020`: as notas daquele ano (pela data da nota). Sem `year`, como antes.

**Tela:**

- **Carteira:** o ano (‹ 2020 ›, como o mês do Resumo), "Investido" em destaque ("em 31/12/2020" ou "hoje"), a linha
  de proventos e de resultado das vendas do ano, o espectro com a legenda (grupo, %, valor), e "Meus ativos": cada ativo
  com o custo, a quantidade, o peso em % e uma barra de peso na cor do grupo. No ano corrente, embaixo, os ativos sem
  posição (adicionados à mão, ou vendidos) e o botão "Adicionar".
- **Proventos:** o total do ano em destaque (no ano corrente, separado em recebido e a receber), filtros por **tipo de
  provento** (Dividendo, JCP, Rendimento) e por **grupo** (os que pagaram algo), 12 barras mensais do ano (o mês futuro,
  esmaecido), "Ano a ano" (uma barra por ano; tocar escolhe o ano), "Por ativo" (do que mais pagou ao que menos, com
  barra de participação) e os lançamentos do ano por dia. Os filtros valem para tudo da aba, menos o "Ano a ano", que
  segue os filtros mas mostra todos os anos.
- **Notas:** as notas do ano, com o total comprado e vendido no ano, "Nova nota", e os avisos "Esperando chegar".
- Os avisos que chegaram ficam acima das abas: pedem ação.

**Testes:**

- Domínio: grupo de cada tipo; a carteira em 31/12 de um ano passado e hoje no corrente; o caso do dono BCFF11 →
  BTHF11 em 2023 e 2024; nota futura fora; investido só com quantidade; proventos e resultado do ano até a data; quem
  entra na lista; ordem; pesos que somam 100% (propriedade, CsCheck), maior resto e empate; carteira vazia; ano futuro
  recusado.
- Integração: `/portfolio` com notas e proventos em anos diferentes; o ano passado e o corrente; recusa do ano futuro;
  anos que se escolhem; isolamento entre usuários; login exigido; `/trade-notes?year=`. (O 409 de nota que não fecha é o
  mesmo caminho de `/positions`; não dá para criar pela API, que recusa a nota.)
- Vitest: geometria do espectro (espessuras proporcionais, mínima, soma), filtros dos proventos (tipo, grupo), meses e
  anos, recebido e a receber, ranking por ativo, rótulos e cores dos grupos, o endereço (aba e ano).

### I.7e Visão da carteira: feita (2026-10-09)

- **Domínio:** `PortfolioSnapshot.Build` (a carteira em um ano), `PortfolioGroup` e `PortfolioGroups.Of` (unit com as
  ações, fundos menores juntos), `PortfolioWeights.BasisPoints` (maior resto, soma 100%), `AssetPosition.On` (a posição no
  fim de um dia) e `AssetPosition.RealizedBetween`.
- **API (só acrescenta):** `GET /portfolio?year=` (`GetPortfolio`) e `GET /trade-notes?year=` (pelo intervalo de datas,
  que usa o índice). Sem migration.
- **Tela:** a página virou três abas (Radix Tabs, do pacote `radix-ui`; aba e ano no endereço) com o seletor de ano.
  `PortfolioTab` (o total, o espectro `PrismSpectrum` com a legenda que filtra, cada ativo com o peso e a barra na cor do
  grupo, e "Sem posição" / "Fora da carteira no fim de {ano}"), `PayoutsTab` (recebido e a receber, filtros por tipo e
  grupo, 12 meses que se tocam, "Ano a ano" que escolhe o ano, "Por ativo", lançamentos) e `NotesTab` (as notas do ano,
  comprado e vendido, e os avisos esperando). O painel do ativo foi para `HoldingSheet.tsx`. A luz atravessa o prisma
  uma vez em 300 ms (`.prism-reveal`, desligada com menos movimento). Saiu o `withPositions`, que a lista nova dispensa.
- **Testes:** domínio (`PortfolioSnapshotTests`, 34: o caso do dono BCFF11 → BTHF11 ano a ano, 31/12 e hoje, nota
  futura, venda nas bordas do ano, proventos até a data, quem entra na lista, ordem e empates, pesos pelo maior resto e
  a propriedade com CsCheck); integração (`PortfolioTests`, 9); Vitest (`portfolio.test.ts`, e os filtros, meses, anos,
  ranking e média em `payouts.test.ts`). Mutação: 11 trocas nas regras, todas pegas depois do teste da venda em 1º/01.
- **Conferido na tela** com um usuário descartável de 2020 a 2026: 320, 390 e 1280 px, claro e escuro, filtro por grupo,
  mês escolhido, filtro por JCP, anos 2020, 2024 e 2025, painel do ativo.
- **Revisão (2026-10-12):** proventos ou notas que não carregam mostram o erro, e não "R$ 0,00" nem "Nenhuma nota", que
  leriam como verdade. No ano corrente sem nenhum ativo com quantidade (só postos à mão), o "Adicionar" vai para o
  título da primeira seção embaixo; antes, sumia.
- **"Sem posição" desfeito (pedido do dono, 2026-10-12):** juntava, no ano corrente, o que tinha saído em anos passados
  (BCFF11 virou BTHF11 em 2024, IRDM11 virou IRIM11 em 2025; ficavam por serem da carteira, com proventos). Agora são
  duas seções: **"Saíram em {ano}"**, em qualquer ano, com o que zerou naquele ano (o resultado ou os proventos dele;
  antes, "Fora da carteira no fim de {ano}"), e **"Sem notas"**, só no ano corrente, com os ativos da carteira que nunca
  tiveram nota nem evento (`holdingsWithoutNotes`). O que zerou num ano passado é histórico: aparece no ano em que saiu.
- **Fica para depois:** valor de hoje e ganho não realizado (cotação, I.7d, decisão em aberto); rendimento sobre o custo
  (precisa do custo médio ao longo do ano, não só o do fim).

### I.7c4 Liquidação paga em cotas: decisões do dono e especificação (2026-10-09)

Achado do dono: no BTHF11 que veio do BCFF11, a corretora e a B3 mostram preço médio de **R$ 10,25**; pelo Prisma, a
conversão levava o custo inteiro do BCFF11 e dava R$ 10,54 (896 cotas a R$ 8,42 = R$ 7.544,32 ÷ 716). O fato relevante
(material de apoio de dezembro/2024, fnet B3) explica: o BCFF11 **foi liquidado e pago em cotas de BTHF11**, e quem as
recebeu passa a ter **R$ 10,25 por cota** de custo, o valor patrimonial da cota de BTHF11 na entrega. Não é conversão:
para a Receita é venda do BCFF11 e compra do BTHF11 (houve IR retido sobre o ganho, pelo custo médio que cada cotista
declarou ao administrador até 12/11/2024).

**O que o fato relevante traz (fonte oficial, valores por cota de BCFF11):**

| Parte | Por cota |
|---|---:|
| Cota patrimonial do BCFF11 | R$ 8,90 |
| Amortização em caixa: o dinheiro pago em 19/12 e/ou o IR recolhido sobre o ganho | R$ 0,56 |
| Rendimento de setembro (pago em 14/10) | R$ 0,07 |
| Rendimento de outubro (pago em 25/10) | R$ 0,07 |
| Cotas de BTHF11: 0,80 × R$ 10,25 | R$ 8,20 |

- **Caso 1 do documento (não tinha BTHF11):** as cotas recebidas entram a R$ 10,25 cada.
- **Caso 2 do documento (já tinha BTHF11):** média ponderada, como uma compra: 100 cotas a R$ 10,00 (R$ 1.000,00) mais
  50 recebidas a R$ 10,25 (R$ 512,50) = 150 cotas, R$ 1.512,50, preço médio **R$ 10,08**.
- **Exemplo do documento:** 53 cotas de BCFF11 → 53 × 0,8 = 42,40 → **42 cotas** de BTHF11 (R$ 430,50), fração de 0,40
  vendida em leilão e creditada (R$ 4,10, pelo preço de referência), caixa de 53 × R$ 0,56 = R$ 29,68.

**Decisões (2026-10-09):**

- **Tipo de evento novo, `Liquidation`:** o ativo de origem sai **como uma venda** (com resultado) e o de destino entra
  **como uma compra** pelo valor oficial da cota. A conversão (`Conversion`) continua para o que mantém o custo (troca de
  código, incorporação sem valor atribuído).
- **Os rendimentos de setembro e outubro ficam fora do evento:** são proventos comuns, lançados como provento. Contá-los
  no evento os contaria duas vezes.
- **O dinheiro (caixa e leilão da fração) entra só no resultado da venda,** sem mexer em saldo de conta: carteira não é
  caixa, e o evento é igual para todos, a conta de cada um não. Quem quiser o dinheiro no saldo lança à mão (ou ele
  chega com a importação de extrato, Fase 4).
- **A fração é avaliada pelo preço de referência do destino** (como no exemplo do documento): o preço real do leilão
  varia por pessoa e não está em documento.
- **O IR retido fica de fora,** como o IRRF da nota: não é custo nem muda o resultado. Os R$ 0,56 contam inteiros no
  valor recebido, porque o IR sai deles: quem teve imposto recebeu menos dinheiro, mas o resultado da venda é o mesmo.
- **IRDM11 → IRIM11 entrou em 2026-10-12** (abaixo). Antes ficava em aberto: tem a mesma forma, mas o dono vai procurar o documento oficial antes (os números
  de hoje, na seção "Casos reais da curadoria", vêm de notícias). Também em aberto: se **PETZ3 → AUAU3** (1:1 com
  R$ 0,7109 em dinheiro) e **CVBI11 → PCIP11** (1:1) mantiveram o custo ou tiveram valor atribuído; cada um muda de tipo
  só com documento.

**Regras (domínio, com teste antes):** para a posição de origem com quantidade `Q` e custo de aquisição `C` no fim do
dia da data (depois das notas do dia, como os outros eventos), com proporção `from:to`, preço de referência do destino
`U` e caixa por cota `K`:

1. **Cotas recebidas:** `R = ⌊Q × to ÷ from⌋` (para baixo, como na conversão).
2. **Valor das cotas:** `R × U`, em centavos (meio centavo para cima).
3. **Fração:** `(Q × to ÷ from − R) × U`, em centavos (meio centavo para cima).
4. **Caixa:** `Q × K`, em centavos (meio centavo para cima).
5. **Valor recebido** = valor das cotas + fração + caixa. **Resultado da origem** = valor recebido − `C`. A origem zera
   (quantidade e custos), como numa venda de tudo.
6. **Destino:** soma `R` cotas com custo bruto e custo de aquisição iguais ao valor das cotas (sem custos de nota). Se
   já havia posição, a média é ponderada (Caso 2).
7. **Sem posição de origem na data,** o evento não deixa rastro. **Nota da origem depois da data** é recusada, como
   na conversão ("BCFF11 virou BTHF11 em 16/12/2024. Desde então, lance como BTHF11.").
8. **Os valores por cota têm mais casas que centavos** (o IRIM11 tem R$ 83,3946076): no arquivo, texto com até 8 casas
   decimais; no domínio e no banco, `long` em centésimos de milionésimo de real (`10.25` → `1_025_000_000`). Dinheiro
   continua em centavos: só o produto final (regras 2 a 4) vira centavo.

**O caso do dono** (896 cotas a R$ 8,42, custo R$ 7.544,32):

| | Valor |
|---|---:|
| Cotas de BTHF11 | 896 × 0,8 = 716,8 → **716** × R$ 10,25 = R$ 7.339,00 |
| Fração | 0,8 × R$ 10,25 = R$ 8,20 |
| Caixa | 896 × R$ 0,56 = R$ 501,76 |
| Valor recebido | R$ 7.848,96 (= 896 × R$ 8,76) |
| **Resultado do BCFF11** | **+R$ 304,64** |
| **BTHF11** | 716 cotas, custo R$ 7.339,00, **preço médio R$ 10,25** |

**Arquivo de eventos:** o BCFF11 → BTHF11 muda de `Conversion` para `Liquidation` (a curadoria atualiza a linha pela
chave; o cálculo refaz tudo sozinho, porque a posição nunca é gravada):

```json
{
  "key": "bcff11-bthf11-2024-12-16",
  "kind": "Liquidation",
  "symbol": "BCFF11",
  "target": "BTHF11",
  "date": "2024-12-16",
  "from": 100,
  "to": 80,
  "targetUnitPrice": "10.25",
  "cashPerUnit": "0.56",
  "source": "Fato relevante dez/2024: cota de R$ 8,90 = 0,8 BTHF11 a R$ 10,25 + R$ 0,56 de caixa + rendimentos de set e out"
}
```

- `targetUnitPrice` é obrigatório na liquidação e maior que zero; `cashPerUnit` é opcional (ausente: zero), nunca
  negativo. Os dois são recusados em `Split` e `Conversion`. Mais de 8 casas decimais, recusado. Um campo errado
  continua recusando o arquivo inteiro (`docs/operacao.md`, 4.2), com a mensagem dizendo qual chave.
- **Banco:** `corporate_events` ganha `target_unit_price` e `cash_per_unit` (`bigint`, nulos, em 10⁻⁸ real), com
  checagem: preenchidos só na liquidação, preço maior que zero, caixa não negativo. Migration só acrescenta.

**API e tela (só acrescenta):** o histórico do ativo ganha os passos `LiquidationOut` (na origem: quantidade que saiu,
valor recebido em `GrossCents`, resultado em `RealizedCents`, destino em `RelatedSymbol`) e `LiquidationIn` (no
destino: cotas recebidas e o valor delas). Na tela:

- BCFF11: "Liquidado em BTHF11" · "16/12/2024 · 896 cotas → 716 BTHF11", com o resultado à direita.
- BTHF11: "Veio de BCFF11" · "16/12/2024 · 716 cotas a R$ 10,25".
- A carteira de 2024 (I.7e) mostra o resultado na linha "nas vendas"; a busca continua com "virou BTHF11".

**Testes:**

- Domínio, com os números do documento: o caso do dono (716 cotas, R$ 7.339,00, resultado +R$ 304,64); o exemplo de 53
  cotas (42 cotas, R$ 430,50, fração R$ 4,10, caixa R$ 29,68, recebido R$ 464,28); o Caso 2 (100 a R$ 10,00 + 50
  recebidas = preço médio R$ 10,08, com 63 cotas de origem: 50,4 → 50); sem caixa; sem fração; sem posição de origem;
  liquidação no fim do dia (compra na data entra, no dia seguinte não); nota da origem depois recusada; venda do destino
  depois usa o custo novo; a carteira do ano (I.7e) com o resultado no ano da liquidação; arredondamento dos valores
  com 8 casas; a soma (o que sai da origem + resultado = valor recebido) com CsCheck.
- Leitura do arquivo: valores com 2 e 8 casas, mais de 8 recusado, negativo e zero recusados, campo de liquidação em
  conversão recusado, `Embedded_file_is_valid` com o BCFF11 novo.
- Integração: a curadoria troca o tipo do evento existente e a posição muda na leitura seguinte; o histórico com os
  dois passos novos; o resultado aparece em `/portfolio?year=2024`.
- Vitest: títulos e detalhes dos passos novos.

### I.7c4 Liquidação paga em cotas: feita (2026-10-09)

- **Domínio:** `CorporateEventKind.Liquidation`, `CorporateEvent.TargetUnitPrice` e `CashPerUnit` (em 10⁻⁸ real,
  `UnitValueScale`), `TryParseUnitValue` (texto com ponto, até 8 casas, até R$ 1 bilhão por cota) e as validações. No
  cálculo, `LiquidateOut` (origem: cotas, fração e caixa, cada parte meio centavo para cima; resultado; zera) e
  `LiquidateIn` (destino: compra pelo valor das cotas, média ponderada com o que já havia; só se recebeu alguma cota).
  Nota da origem depois da data é recusada, como na conversão. Passos novos: `LiquidationOut` e `LiquidationIn`.
- **API e banco:** migration `LiquidationEvents` (duas colunas e checagens; a volta apaga as liquidações, que a
  curadoria da versão anterior recria como estavam). O arquivo aceita `targetUnitPrice` e `cashPerUnit`; valor fora do
  formato recusa o arquivo inteiro. A busca mostra "virou BTHF11" também na liquidação.
- **Arquivo de eventos:** BCFF11 → BTHF11 passou a `Liquidation`, com `"10.25"` e `"0.56"`.
- **Tela:** "Liquidado em BTHF11 · 896 cotas saíram" com a linha "recebeu R$ 7.848,96 · resultado +R$ 304,64"; "Veio de
  BCFF11 · 716 cotas a R$ 10,25".
- **Testes:** domínio (`LiquidationInPositionTests`, 13: o caso do dono, o exemplo de 53 cotas e o Caso 2 do documento,
  sem caixa, perda, 8 casas, uma cota ou nenhuma, fim do dia, venda depois, carteira do ano, propriedade com CsCheck;
  `LiquidationEventTests`: validações, leitura dos valores, a curadoria trocando o tipo e corrigindo só os valores);
  integração (`CorporateEventsTests`: a troca do tipo muda posição, histórico e `/portfolio?year=2024`; valor inválido
  recusa o arquivo); Vitest (títulos e detalhes). Mutação: 11 trocas, todas pegas depois dos testes de uma cota e da
  correção só dos valores.
- **Com os dados do dono** (custo real do BCFF11 de R$ 7.542,93, pelas notas): BTHF11 de R$ 10,53 para **R$ 10,25**
  (716 cotas, R$ 7.339,00) e o BCFF11 com resultado de R$ 7.848,96 − R$ 7.542,93 = **+R$ 306,03** em 2024.
- **Local:** rodar `dotnet ef database update -p src/Prisma.Api -s src/Prisma.Api` e reiniciar a API (a curadoria
  troca o evento ao subir). **Produção:** a migration e o arquivo entram sozinhos no deploy.

### IRDM11 → IRIM11 como liquidação (2026-10-12)

O dono confirmou: o custo das cotas de IRIM11 é o valor de referência do fato relevante (27/11/2025), **R$ 83,3946076**.
A corretora mostrava R$ 82,74, que não bate com nenhuma regra do documento: parece o custo antigo levado adiante, como
uma conversão. Mas a liquidação foi tributada como venda a R$ 78,09312078, o que já pressupõe receber o IRIM11 a R$ 83,39.
Com custo menor, parte do mesmo ganho seria tributada de novo na venda futura.

- **Evento:** `irdm11-irim11-2025-10-23`, `Liquidation`, proporção 1.000.000.000 : 884.794.245, `targetUnitPrice`
  `"83.3946076"` e `cashPerUnit` `"4.30605192"`. O documento traz R$ 4,306051918, com 9 casas; o arquivo aceita 8, e a
  diferença no total fica abaixo de um centavo. A data é o último pregão (23/10/2025): quem tinha IRDM11 no fim desse dia
  recebeu o IRIM11. Fração leiloada; dinheiro creditado em 09/12/2025.
- **O caso do dono:** 234 IRDM11 (11 notas, de 2021 a 2025) viram **207 IRIM11 a R$ 83,39** (R$ 17.262,68). Recebeu
  R$ 18.273,79: cotas R$ 17.262,68, fração (0,0418533) R$ 3,49 e dinheiro R$ 1.007,62. O resultado em 2025 é isso menos
  o custo de aquisição do IRDM11 pelas notas.

### I.7c2 Conferir eventos: decisões e especificação (2026-10-12)

O evento da empresa vale sozinho para todos que tinham o ativo, e um erro no arquivo (proporção, data, tipo) mudaria a
carteira de todo mundo sem ninguém perceber. A pessoa confere, uma vez, o que cada evento fez com a quantidade **dela**.
O dono pediu a conversa (2026-10-12) e deixou as decisões comigo ("faça o que achar recomendado"); o dinheiro de frações
e conversões, a confirmação do PETZ3 e do CVBI11 e o CRFB3 ficam para quando precisarem.

**Decisões (2026-10-12):**

- **Onde:** no sino, **uma linha só** para todos os eventos ("8 eventos na carteira para conferir"), que leva a
  Investimentos; lá, o bloco **"Conferir eventos"** no alto, acima das abas, como o aviso de ativo que chegou. Nada de
  Ajustes: lá ninguém confere, e o erro passaria. O dono temia o sino crescer com os débitos automáticos; com os eventos
  agrupados, ele cresce uma linha.
- **Cada evento mostra o que fez com a quantidade da pessoa:** "SNAG11 · desdobramento 1:10 · 01/08/2023 · 19 → 190
  cotas"; "BCFF11 → BTHF11 · liquidação · 16/12/2024 · 896 cotas → 716 BTHF11 a R$ 10,25".
- **Confere:** sai da lista, com "Desfazer". **Não confere:** registra para a curadoria, com um campo opcional "quantas
  a corretora mostra"; a carteira **não muda sozinha** (o evento vale até a curadoria corrigir ou tirar). Sai da lista
  também: a pessoa já disse o que tinha a dizer.
- **"Conferir todos"** só quando há mais de um: no primeiro dia a pessoa encontra todos os eventos do passado de uma vez
  (o dono, uns oito). Diferente do débito automático (nada de "marcar todos", porque a estimativa passaria por valor
  real): aqui a lista inteira está à vista, e o botão confere exatamente o que ela mostra.
- **Evento corrigido pela curadoria volta a pedir conferência:** a conferência guarda a versão do evento (`UpdatedAt`);
  se o evento mudar depois, ele volta à lista. É o caminho do "Não confere" → curadoria corrige → a pessoa confere de novo.

**Regras:**

1. **O que pede conferência** é calculado, nunca gravado: o evento com data até hoje que deixou rastro na posição da
   pessoa (passo de desdobramento, saída de conversão ou saída de liquidação no histórico), sem conferência da versão
   atual. Evento sem posição na data não pede nada.
2. **A conferência** (`EventReview`) é da pessoa, uma por evento (o índice único conta as desfeitas, e conferir de novo
   devolve o mesmo registro): o veredito (`Confirmed` ou `Disputed`), a quantidade que a corretora mostra (opcional, só
   no "Não confere", de 0 a 100.000.000) e a versão do evento conferida. "Desfazer" é soft delete.
3. **Conferir os que a lista mostra:** só eventos que pedem conferência da pessoa; qualquer outro, 404 "Evento não
   encontrado." (também para quem não tem o ativo: a pessoa não fica sabendo de evento de carteira alheia). Até 200 por
   vez.
4. Sair o evento do arquivo apaga as conferências dele (não há mais o que conferir).

**API (só acrescenta):** `GET /event-reviews` (o que pede conferência, do mais antigo ao mais novo); `POST
/event-reviews/confirm` (`eventIds`); `POST /event-reviews/{eventId}/dispute` (`reportedQuantity` opcional); `DELETE
/event-reviews/{eventId}` (desfazer). Log: `EventReviewsConfirmed` (quantos) e `EventReviewDisputed` (o evento), sem
quantidade.

**Curadoria:** `docs/operacao.md` ganha a consulta dos "Não confere" por evento (quantas pessoas e as quantidades que a
corretora mostra).

**Testes:**

- Domínio: o que pede conferência (desdobramento, conversão e liquidação; sem posição, nada; evento futuro, nada; só a
  origem da conversão, não o destino); conferido some; evento corrigido volta; a conferência guarda veredito, quantidade
  e versão; conferir de novo restaura e atualiza.
- Integração: a lista com os eventos do caso do dono; conferir alguns e todos; "Não confere" com e sem quantidade;
  desfazer; evento corrigido pela curadoria volta à lista; evento de quem não tem o ativo, 404; isolamento; login.
- Vitest: título e detalhe de cada tipo, o rótulo do sino com débitos e eventos, quando aparece o "Conferir todos".

### I.7c2 Conferir eventos: feita (2026-10-12)

- **Domínio:** `EventReview` (veredito, quantidade da corretora, versão do evento; `Review` conferindo de novo devolve o
  mesmo registro) e `EventEffects` (o que pede conferência, calculado das posições: desdobramento, saída de conversão e
  de liquidação, com data até hoje; `Pending` tira os conferidos da versão atual).
- **API e banco:** tabela `event_reviews` (migration `AddEventReviews`: com dono, única por pessoa e evento contando as
  desfeitas, apagada junto com o evento). `GET /event-reviews`, `POST /event-reviews/confirm`, `POST
  /event-reviews/{eventId}/dispute` e `DELETE /event-reviews/{eventId}`. Logs `EventReviewsConfirmed`,
  `EventReviewDisputed` e `EventReviewUndone`. A consulta da curadoria está em `docs/operacao.md`, 4.4, e num teste.
- **Tela:** "Conferir eventos" no alto de Investimentos (`EventReviewList`), com "Confere", "Não confere" (painel com
  a quantidade opcional), "Conferir todos" com mais de um e "Desfazer" em todos. O sino conta os eventos junto com os
  débitos e mostra uma linha só para eles, que leva a Investimentos; só balança depois que as duas listas chegam (senão
  balançaria ao abrir a tela). As consultas dos eventos ficam num módulo próprio (`eventReviewQueries.ts`): o sino está
  no pacote principal, que cresceu 0,9 kB.
- **Testes:** domínio (`EventReviewTests`, 8), integração (`EventReviewsTests`, 12, com o caso do dono BCFF11 e a
  consulta da curadoria) e Vitest (`eventReviews.test.ts` e o rótulo do sino). Mutação: 11 trocas, todas pegas (a do
  "restaurar o desfeito", pela integração).
- **Conferido na tela** com um usuário descartável numa cópia do banco: 320 e 390 px, claro e escuro; toque real em
  "Confere", "Desfazer" e "Conferir todos"; o painel do "Não confere"; a linha do sino. Em 320 px a proporção ficava
  sozinha na linha ("1:10"); agora vai presa à palavra.
- **Local:** rodar `dotnet ef database update -p src/Prisma.Api -s src/Prisma.Api` e reiniciar a API. **Produção:** a
  migration entra sozinha no deploy (só cria a tabela).

## 9. Em aberto

- **Liquidação paga em cotas (I.7c4):** feita, com BCFF11 → BTHF11 e IRDM11 → IRIM11. PETZ3 → AUAU3 e CVBI11 → PCIP11
  a conferir se mantiveram o custo.
- **Conferir a carteira com a B3** (sugerido ao dono, 2026-10-12): com todas as notas lançadas, comparar a quantidade de
  cada ativo na aba Carteira com a Área do Investidor. Diferença é nota faltando, evento fora do arquivo (desdobramento,
  grupamento, bonificação) ou troca de código pendente.
- **Pontos menores da revisão de 2026-10-12, sem correção por ora:** uma aba aberta antes do deploy mostra os passos
  novos da liquidação como "Compra" até recarregar (só lê passos que já conhecia); o painel aberto pelo "Por ativo" dos
  proventos não sabe se o ativo saiu da bolsa quando ele não está na carteira (o provento não traz isso).
- **Visão da carteira:** feita (I.7e, 2026-10-09). Em aberto: o valor de hoje (cotação, I.7d) e o rendimento sobre o custo.
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
- **Segunda importação, de 2020 a 2025 (banco local, 2026-10-12):** 590 proventos da tabela do dono, pelo mesmo caminho
  (importador descartável fora do repositório, `SavePayout.Handler` como o usuário, numa transação, ensaio antes); os de
  2026 já estavam lá e ficaram de fora. Mesmas regras da I.6: na Íon, com a soma descontada do saldo inicial (o saldo não
  muda); "Rend. Tributado" como dividendo; "dividendo" de fundo como rendimento. Novas: o provento de um código de
  subscrição (MXRF13, MXRF14, BCFF13, IRDM13, IRDM14) vai para o ativo original (decisão do dono); data só com o mês (2020
  e 2021) cai no dia 1, como o dono lançou 2022. Rodar de novo não duplica (mesmo ativo, dia, tipo e valor ficam de
  fora). A produção ainda não recebeu.
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
