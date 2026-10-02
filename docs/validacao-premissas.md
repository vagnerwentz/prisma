# Validação de premissas do produto

Análise feita em 2026-09-27 (skill `identify-assumptions-existing`), a partir do código, do `PLAN.md`
e do que o dono, o pai e a noiva relataram ao usar o app. Nada foi implementado: o documento lista
as premissas, como validá-las e onde anotar o resultado.

**Leitura geral:** o núcleo do Prisma foi construído com rigor (centavo exato, testes de propriedade,
isolamento por usuário, logs). As premissas mais arriscadas estão **em volta** dele: se as pessoas vão
lançar tudo, se vão escolher o fluxo certo, e se o app aguenta ter usuários de verdade.

**Confiança** é o quanto se acredita que a premissa é **verdadeira**. Confiança baixa = risco alto.

---

## 1. Valor: resolve um problema real?

### Premissa 1. As pessoas vão lançar cada gasto na hora da compra *(PM, Designer)*

| | |
|---|---|
| **Por que assumimos** | É a visão no `PLAN.md`: "uso primário no celular, lançando na hora da compra". Todo o resto (Resumo, Análise, variação do mês) depende disso. |
| **O que pode estar errado** | Lançar à mão é o hábito que mais se abandona em apps de finanças; o próprio dono ainda não usava o app no dia a dia. Se metade das compras não entra, os números ficam errados de forma plausível, o erro que o `CLAUDE.md` mais teme. |
| **Impacto se errada** | Muito alto: as análises viram ficção, a pessoa perde a confiança e para de abrir o app. |
| **Confiança** | Baixa |
| **Como validar** | Consultas A1 a A3 (lançamentos por semana, atraso entre compra e lançamento, conferência com a fatura do banco). |

### Premissa 2. A visão de caixa é a que as pessoas entendem *(PM, Designer)*

| | |
|---|---|
| **Por que assumimos** | "Decisões tomadas" no `PLAN.md`: o gasto do cartão conta no mês do vencimento da fatura. É correta para o fluxo de caixa. |
| **O que pode estar errado** | As pessoas pensam pela data da compra ("gastei no mercado ontem, é setembro"). Uma compra de hoje no cartão aparece no "Saiu" do mês seguinte, e o mês atual parece mais barato. O texto explicativo no Resumo existe, mas explicação lida é sinal de modelo pouco intuitivo. |
| **Impacto se errada** | Alto: "o app está errado" mesmo com tudo certo. |
| **Confiança** | Média |
| **Como validar** | Tarefa 3 das sessões ("quanto você gastou em setembro?"). |

### Premissa 3. O diferencial é reduzir a fricção de lançar e descobrir "onde o dinheiro foi" *(PM)*

| | |
|---|---|
| **Por que assumimos** | É a visão; as Fases 3 e 4 (NFC-e, CNPJ, importação) partem daí. |
| **O que pode estar errado** | A pergunta que faz alguém abrir o app pode ser outra: "vou fechar o mês no azul?", "quanto já tenho comprometido em parcelas?". A 2.8 (compromissos herdados) e a ideia de projeção de caixa apontam para esse lado. |
| **Impacto se errada** | Médio a alto: meses de importação e NFC-e para uma dor secundária. |
| **Confiança** | Média |
| **Como validar** | Perguntas finais das sessões; filtros de logs L1 e L2 (quais telas cada pessoa abre). |

### Premissa 4. O centavo exato importa mais do que lançar rápido *(PM, Engineer)*

| | |
|---|---|
| **Por que assumimos** | Regra inviolável 1 e o investimento em `Money` e `SplitInto`. |
| **O que pode estar errado** | A exatidão interna está certa e deve ficar. O risco é **exigir** exatidão de quem lança (valor completo, conta, categoria, meio): "uns R$ 50 no mercado" lançado é melhor que nada. |
| **Impacto se errada** | Médio: mais atrito por lançamento e menos lançamentos (reforça a premissa 1). |
| **Confiança** | Média |
| **Como validar** | Tarefa 2 das sessões (tempo e toques de um lançamento; campos que causam hesitação). |

### Premissa 5. O pai e a noiva representam quem vai usar o Prisma *(PM)*

| | |
|---|---|
| **Por que assumimos** | São os únicos testes com pessoas reais. |
| **O que pode estar errado** | São próximos: tendem a ser gentis e a pedir ajuda ao dono em vez de desistir. O retorno de família subestima o atrito. |
| **Impacto se errada** | Médio: decisões calibradas por usuários que perdoam demais. |
| **Confiança** | Baixa |
| **Como validar** | Repetir o roteiro das sessões com alguém que não conhece bem o dono, só observando, sem ajudar. |

---

## 2. Usabilidade: as pessoas vão conseguir usar?

### Premissa 6. As pessoas vão pagar a fatura pelo "Pagar fatura", e não lançá-la como despesa *(Designer, Engineer)*

| | |
|---|---|
| **Por que assumimos** | A regra 5 (transferência nunca é despesa) está protegida no domínio, e existe o "Pagar fatura" na tela da fatura. |
| **O que pode estar errado** | A proteção só vale se a pessoa usar esse caminho. Para quem vem de outros apps, o natural é abrir o "+" e lançar "Fatura Nubank, R$ 1.200" como **despesa**: o gasto do mês dobra, porque as compras já estavam lá. Nada impede nem avisa. |
| **Impacto se errada** | Muito alto e silencioso: o erro de dinheiro que o projeto inteiro tenta evitar. |
| **Confiança** | Baixa a média |
| **Como validar** | Consultas B1 e B2; tarefa 4 das sessões. |

### Premissa 7. As pessoas sabem o dia de fechamento e o de vencimento do cartão *(Designer, Engineer)*

| | |
|---|---|
| **Por que assumimos** | O formulário de conta pede os dois dias, e o `StatementCalculator` é exato a partir deles. |
| **O que pode estar errado** | Muita gente sabe o vencimento, mas não o fechamento. Um palpite errado põe compras na fatura errada, e o `SettlementDate` delas no mês errado. Mudar o dia depois não recalcula as faturas já criadas (pendência no `PLAN.md`). |
| **Impacto se errada** | Alto: números errados de forma plausível em todo cartão, difíceis de corrigir depois. |
| **Confiança** | Média |
| **Como validar** | Consulta C1; tarefa 6 das sessões (conferir os dias no app do banco). Evidência na seção 11. |

### Premissa 8. A linguagem visual do espectro é lida sem explicação *(Designer)*

| | |
|---|---|
| **Por que assumimos** | Decisão de identidade (1.13): receita em gradiente frio, despesa em tinta, nada de verde e vermelho. |
| **O que pode estar errado** | Verde e vermelho não são só clichê: são leitura instantânea. Sinais sutis já passaram batido (o campo do valor, 2.17). Receita em gradiente e despesa em tinta podem exigir um segundo olhar. |
| **Impacto se errada** | Médio: leitura mais lenta, não número errado. |
| **Confiança** | Média |
| **Como validar** | Tarefa 5 das sessões (teste de 5 segundos). |

### Premissa 9. Um site no navegador do celular basta, sem app instalado (PWA) *(Designer, Engineer)*

| | |
|---|---|
| **Por que assumimos** | A PWA está planejada para a Fase 5. |
| **O que pode estar errado** | Lançar "na hora da compra" exige chegar ao app em dois segundos. Abrir o navegador, achar a aba e às vezes entrar de novo é atrito direto na premissa 1. |
| **Impacto se errada** | Alto, somado à premissa 1. |
| **Confiança** | Baixa a média |
| **Como validar** | Tarefa 1 das sessões (como e em quanto tempo a pessoa chega ao app). |

---

## 3. Viabilidade: o projeto se sustenta?

### Premissa 10. "É só para uso próprio", então o app pode quebrar ou perder dados sem grande prejuízo *(PM, Engineer)*

| | |
|---|---|
| **Por que assumimos** | O `PLAN.md` trata a hospedagem como "uso próprio", e isso justificou adiar o backup. |
| **O que pode estar errado** | **Já é falsa.** O pai usa a produção com dados reais. E hoje: não há backup fora do Railway; a conta está no plano Free (pode parar, e o volume de conta vinda do teste é apagado 30 dias depois do fim do crédito); não há homologação; as migrations rodam direto no banco de produção a cada deploy. |
| **Impacto se errada** | Muito alto: um problema apaga o histórico financeiro de outra pessoa, e a confiança não volta. |
| **Confiança** | Muito baixa (já se vê que é falsa) |
| **Como validar** | Não precisa validar, precisa agir: H.2b (backup com restauração testada) e plano Hobby. A mais urgente da lista. |

### Premissa 11. Guardar dados financeiros de outras pessoas não traz obrigação *(PM)*

| | |
|---|---|
| **Por que assumimos** | O cadastro é fechado, e a LGPD está nas pendências "antes de abrir o cadastro público". |
| **O que pode estar errado** | Com cadastro fechado ou aberto, o app já guarda dados financeiros do pai e da noiva. Não há recuperação de senha, nem forma de a pessoa exportar ou apagar os próprios dados. |
| **Impacto se errada** | Médio hoje (família), alto se o círculo crescer. |
| **Confiança** | Média |
| **Como validar** | Checklist D1 (o que cada pessoa consegue fazer sozinha). |

### Premissa 12. O custo fica baixo (US$ 5 a 10 por mês) *(PM)*

| | |
|---|---|
| **Por que assumimos** | Estimativa feita na escolha do Railway. |
| **O que pode estar errado** | Provavelmente certa no volume atual. O risco é somar: backup, logs retidos por mais tempo, APIs pagas da Fase 3 (CNPJ), um dia um serviço de observabilidade. |
| **Impacto se errada** | Baixo |
| **Confiança** | Alta |
| **Como validar** | Página *Usage* do Railway, uma vez por mês. |

---

## 4. Viabilidade técnica: dá para construir?

### Premissa 13. A leitura da NFC-e e a consulta de CNPJ são viáveis e estáveis (Fase 3) *(Engineer)*

| | |
|---|---|
| **Por que assumimos** | O QR Code da NFC-e é padrão nacional, e existem APIs públicas de CNPJ. |
| **O que pode estar errado** | Os portais das SEFAZ variam por estado, alguns têm captcha e mudam sem aviso; as APIs gratuitas de CNPJ têm limite baixo; ler QR pelo navegador do iPhone tem limitações; o nome fantasia costuma ser pior que a descrição que a pessoa escreveria. |
| **Impacto se errada** | Alto: a Fase 3 gira em torno disso. |
| **Confiança** | Baixa a média |
| **Como validar** | Experimento E1, antes de escrever o `docs/fase-3.md`. |

### Premissa 14. A importação da fatura em PDF do Itaú é confiável (Fase 4) *(Engineer)*

| | |
|---|---|
| **Por que assumimos** | Está no roteiro, com fixtures versionadas. |
| **O que pode estar errado** | O layout muda sem aviso, cada banco é um formato, e a deduplicação contra lançamentos manuais é o problema difícil: o valor bate, a data não (compra × lançamento) e a descrição é sempre diferente. |
| **Impacto se errada** | Alto: importação que duplica ou pula lançamentos corrompe os números sem ninguém ver. |
| **Confiança** | Média |
| **Como validar** | Experimento E2. |

### Premissa 15. Capturas de tela e testes manuais bastam para o frontend *(Engineer)*

| | |
|---|---|
| **Por que assumimos** | Não há testes de componente nem E2E (o Playwright está na Fase 5); o Vitest cobre regras e formatação. |
| **O que pode estar errado** | Os bugs da sessão de 2026-09-26 (campo que passou batido, cursor congelado apagado, propriedade de log que sumia) foram pegos por olho ou por sorte. Com mais telas, uma regressão num fluxo como "Pagar fatura" chega à produção sem ninguém ver. |
| **Impacto se errada** | Médio a alto, e cresce com o tempo. |
| **Confiança** | Média |
| **Como validar** | Registro de bugs F1 (de onde veio cada bug). |

---

## 5. Priorização (impacto × incerteza)

| Prioridade | Premissa | Por quê |
|---|---|---|
| **1** | 10. Uso próprio pode quebrar | Já é falsa, e o dano (perder dados do pai) é irreversível |
| **2** | 1. Lançar na hora da compra | Sustenta o produto inteiro, e há sinal de que não acontece |
| **3** | 6. Pagamento de fatura como despesa | Erro silencioso de dinheiro, o que o projeto quer evitar |
| **4** | 7. Dias do cartão corretos | Erro silencioso em todo cartão |
| **5** | 2. Visão de caixa é intuitiva | "O app está errado" mesmo com tudo certo |
| **6** | 13. NFC-e e CNPJ viáveis | Decide se a Fase 3, como planejada, faz sentido |

---

## 6. Consultas no banco de produção

### Como rodar com segurança

1. No Railway, ligar o **TCP Proxy** do serviço Postgres (*Settings* → *Networking*) e anotar host e
   porta.
2. Conectar como `postgres` (a senha é pedida na tela):
   ```bash
   psql "postgresql://postgres@<host do proxy>:<porta>/railway"
   ```
3. Abrir uma transação **só de leitura** antes de qualquer consulta, e fechá-la no fim:
   ```sql
   BEGIN READ ONLY;
   -- consultas
   ROLLBACK;
   ```
4. **Desligar o TCP Proxy** ao terminar.

**Privacidade:** as consultas B1 e A3 mostram descrições de lançamentos de outras pessoas (o pai, a
noiva). Olhar só o necessário, não copiar o resultado para fora do terminal e **não colar em conversa
com IA**. Para discutir um resultado, levar só as contagens.

Todas as consultas ignoram o que foi excluído (`deleted_at is null`). As datas de criação são
convertidas para o horário de São Paulo.

### A0. Quem é quem

```sql
-- Id e e-mail de cada usuário, para ler as outras consultas e filtrar os logs (@userId:…).
SELECT id, email
FROM users
ORDER BY email;
```

### A1. Premissa 1: lançamentos por semana, por pessoa

Conta **decisões de lançar**: uma compra parcelada conta uma vez (só a parcela 1), e uma
transferência também (só a ponta de saída).

```sql
SELECT u.email,
       date_trunc('week', t.created_at AT TIME ZONE 'America/Sao_Paulo')::date AS semana,
       count(*) AS lancamentos
FROM transactions t
JOIN users u ON u.id = t.user_id
WHERE t.deleted_at IS NULL
  AND (t.installment_number IS NULL OR t.installment_number = 1)
  AND (t.transfer_pair_id IS NULL OR t.transfer_direction = 'Out')
GROUP BY u.email, semana
ORDER BY u.email, semana;
```

**Como ler:** semanas com zero não aparecem. Semanas vazias entre semanas cheias, ou um número que cai
com o tempo, indicam que o hábito não pegou.

### A2. Premissa 1: lança na hora ou depois?

Diferença, em dias, entre a data da compra e o dia em que foi lançada.

```sql
SELECT u.email,
       count(*) AS lancamentos,
       round(avg(CASE WHEN (t.created_at AT TIME ZONE 'America/Sao_Paulo')::date = t.purchase_date
                      THEN 1.0 ELSE 0 END) * 100) AS pct_no_mesmo_dia,
       percentile_cont(0.5) WITHIN GROUP (
           ORDER BY (t.created_at AT TIME ZONE 'America/Sao_Paulo')::date - t.purchase_date
       ) AS atraso_mediano_dias,
       max((t.created_at AT TIME ZONE 'America/Sao_Paulo')::date - t.purchase_date) AS maior_atraso_dias
FROM transactions t
JOIN users u ON u.id = t.user_id
WHERE t.deleted_at IS NULL
  AND t.type IN ('Expense', 'Income')
  AND (t.installment_number IS NULL OR t.installment_number = 1)
GROUP BY u.email
ORDER BY u.email;
```

**Como ler:** perto de 100% no mesmo dia confirma a premissa. Atraso mediano de vários dias indica
lançamento em lote ("sento no domingo e lanço a semana"), o que muda o produto: importação passa a
valer mais que lançamento rápido.

### A3. Premissa 1: conferência com a fatura do banco

Lista as compras de uma fatura do Prisma, para comparar linha a linha com a fatura do app do banco
(feito junto com a pessoa, na sessão). Trocar o e-mail e a referência (`AAAA-MM`).

```sql
SELECT t.purchase_date, round(t.amount_cents / 100.0, 2) AS valor, t.description, t.installment_number
FROM transactions t
JOIN statements s ON s.id = t.statement_id
JOIN accounts a ON a.id = s.account_id
JOIN users u ON u.id = t.user_id
WHERE u.email = 'email@da.pessoa'
  AND s.reference = '2026-09'
  AND t.deleted_at IS NULL
  AND t.type <> 'Transfer'
ORDER BY a.name, t.purchase_date;
```

**Como ler:** a medida é **quantas compras da fatura do banco estão no Prisma** (ex.: 18 de 30 = 60%).
Abaixo de uns 80%, os números do app não representam o mês.

### B1. Premissa 6: despesas que parecem pagamento de fatura

```sql
SELECT u.email, t.purchase_date, round(t.amount_cents / 100.0, 2) AS valor, t.description, a.name AS conta
FROM transactions t
JOIN accounts a ON a.id = t.account_id
JOIN users u ON u.id = t.user_id
WHERE t.deleted_at IS NULL
  AND t.type = 'Expense'
  AND t.transfer_pair_id IS NULL
  AND t.description ILIKE ANY (ARRAY[
      '%fatura%', '%cartão%', '%cartao%', '%nubank%', '%itaú%', '%itau%', '%inter%',
      '%c6%', '%santander%', '%bradesco%', '%caixa%', '%mercado pago%', '%picpay%'])
ORDER BY u.email, t.purchase_date;
```

**Como ler:** qualquer linha aqui é suspeita, mas pode ser legítima (anuidade do cartão, tarifa do
banco). Confirmar com a pessoa. Cada pagamento de fatura lançado como despesa dobra o gasto daquele mês.

### B2. Premissa 6: despesa fora do cartão com o mesmo valor de uma fatura

Pega o caso sem descrição reveladora: uma despesa na corrente (ou carteira) com exatamente o total
de uma fatura, até 7 dias antes ou depois do vencimento. O total segue a regra do app (compras menos
estornos, sem transferências).

```sql
WITH totais AS (
    SELECT s.id, s.user_id, s.account_id, s.reference, s.due_date,
           sum(CASE WHEN t.type = 'Refund' THEN -t.amount_cents ELSE t.amount_cents END) AS total_cents
    FROM statements s
    JOIN transactions t ON t.statement_id = s.id AND t.deleted_at IS NULL AND t.type <> 'Transfer'
    WHERE s.deleted_at IS NULL
    GROUP BY s.id
)
SELECT u.email, cartao.name AS cartao, f.reference AS fatura, round(f.total_cents / 100.0, 2) AS total_fatura,
       e.purchase_date, conta.name AS conta_da_despesa, e.description
FROM totais f
JOIN accounts cartao ON cartao.id = f.account_id
JOIN transactions e ON e.user_id = f.user_id
                   AND e.deleted_at IS NULL
                   AND e.type = 'Expense'
                   AND e.transfer_pair_id IS NULL
                   AND e.amount_cents = f.total_cents
                   AND e.purchase_date BETWEEN f.due_date - 7 AND f.due_date + 7
JOIN accounts conta ON conta.id = e.account_id AND conta.type <> 'CreditCard'
JOIN users u ON u.id = f.user_id
ORDER BY u.email, f.due_date;
```

**Como ler:** uma linha aqui é forte candidata a pagamento de fatura lançado como despesa, mas
confira: numa fatura de uma compra só, uma despesa comum do mesmo valor também aparece (no teste com
os dados locais, uma fatura de R$ 45,90 casou com um "Pão de queijo" de R$ 45,90). A descrição
e a conta da despesa costumam desfazer a dúvida.

### B3. Premissas 3 e 6: quais fluxos cada pessoa usa

```sql
SELECT u.email,
       count(*) FILTER (WHERE t.type = 'Expense' AND a.type <> 'CreditCard') AS despesas_fora_do_cartao,
       count(*) FILTER (WHERE t.type = 'Expense' AND a.type = 'CreditCard'
                        AND (t.installment_number IS NULL OR t.installment_number = 1)) AS compras_no_cartao,
       count(*) FILTER (WHERE t.type = 'Income') AS receitas,
       count(*) FILTER (WHERE t.type = 'Refund') AS estornos,
       count(*) FILTER (WHERE t.type = 'Transfer' AND t.transfer_direction = 'Out') AS transferencias,
       count(*) FILTER (WHERE t.type = 'Transfer' AND t.transfer_direction = 'In'
                        AND a.type = 'CreditCard') AS faturas_pagas_pelo_fluxo_certo
FROM transactions t
JOIN accounts a ON a.id = t.account_id
JOIN users u ON u.id = t.user_id
WHERE t.deleted_at IS NULL
GROUP BY u.email
ORDER BY u.email;
```

**Como ler:** quem tem compras no cartão há mais de um mês e zero `faturas_pagas_pelo_fluxo_certo`
ou não pagou nenhuma fatura pelo app, ou pagou pelo caminho errado (ver B1 e B2).

### C1. Premissa 7: dias de fechamento e vencimento dos cartões

```sql
SELECT u.email, a.name AS cartao, a.closing_day AS fecha, a.due_day AS vence,
       count(s.id) AS faturas,
       count(s.id) FILTER (WHERE s.dates_edited_manually) AS faturas_com_datas_editadas,
       min(s.reference) AS primeira, max(s.reference) AS ultima
FROM accounts a
JOIN users u ON u.id = a.user_id
LEFT JOIN statements s ON s.account_id = a.id AND s.deleted_at IS NULL
WHERE a.type = 'CreditCard' AND a.deleted_at IS NULL
GROUP BY u.email, a.name, a.closing_day, a.due_day
ORDER BY u.email, a.name;
```

**Como ler:** conferir `fecha` e `vence` com o app do banco de cada pessoa (tarefa 6 das sessões).
Faturas com datas editadas à mão são sinal de que o dia cadastrado não bate com o real.

---

## 7. Filtros nos logs do Railway

Serviço da API → *Logs*. Trocar `<id>` pelo id da pessoa (consulta A0).

| Id | Pergunta | Filtro |
|---|---|---|
| L1 | Quantas vezes a pessoa abriu o Resumo | `@userId:<id> @route:/dashboard/summary` |
| L2 | Quantas vezes abriu a Análise (o comparativo só carrega lá) | `@userId:<id> @route:/dashboard/history` |
| L3 | Quantos lançamentos fez (premissa 1) | `@userId:<id> @route:/transactions @method:POST` |
| L4 | Se a tela quebrou no celular dela | `@userId:<id> @eventName:ClientError` |
| L5 | Se ela tentou entrar e errou a senha | `@eventName:LoginFailed @userId:<id>` |

Os logs ficam 7 dias no Hobby e 3 no Free: anotar os números no dia.

---

## 8. Roteiro das sessões com o pai e a noiva

Uma sessão por pessoa, uns **20 a 30 minutos**, no celular dela, com os dados dela.

### Antes

- Rodar A0 a C1 e anotar só as contagens (não as descrições).
- Pedir que a pessoa tenha à mão o app do banco dela (para as tarefas 4 e 6).
- Levar a folha de anotação (seção 8.4) impressa ou num bloco de notas.

### Regras para quem conduz

- **Não ajudar.** Se a pessoa travar, perguntar "o que você está procurando?" e esperar. Só ajudar se
  ela desistir, e anotar que desistiu.
- **Pedir que pense em voz alta:** "vai falando o que você está pensando, o que espera que aconteça".
- **Não explicar o app antes nem durante.** Explicações vêm só no fim.
- **Não defender o app.** Se ela criticar, perguntar "por quê?" e anotar.
- **Cronometrar** as tarefas 1 e 2 (o celular basta).

### 8.1 Abertura (2 min)

> "Não é um teste seu, é um teste do app. Se você se atrapalhar, o problema é do app, e é justamente
> isso que eu quero achar. Vai falando o que pensa enquanto usa."

### 8.2 Tarefas

**Tarefa 1. Chegar ao app** *(premissa 9)*
> "Imagina que você acabou de pagar alguma coisa. Guarda o celular no bolso. Agora tira e abre o
> Prisma, do jeito que você abriria no dia a dia."

Observar: por onde abre (favorito, aba aberta, digitando o endereço, tela inicial), se precisa entrar
de novo, e o **tempo do bolso até a tela do app**.

**Tarefa 2. Lançar o último gasto** *(premissas 1 e 4)*
> "Lança o último gasto que você fez de verdade, hoje ou ontem."

Observar: tempo e número de toques até salvar; campos em que hesita ou que pula; se acha o valor de
primeira; se escolhe a conta e a categoria certas.

**Tarefa 3. Quanto gastou no mês** *(premissa 2)*
> "Quanto você gastou em setembro?"

Antes de ela abrir o app, perguntar: "qual número você espera ver, mais ou menos?". Depois observar
onde ela procura, qual número lê e se ele bate com o que esperava. Se ela tem cartão, perguntar:
"essa compra que você fez no cartão semana passada está nesse número?".

**Tarefa 4. Pagar a fatura** *(premissa 6; só para quem tem cartão no Prisma)*
> "Chegou a fatura do cartão e você pagou pelo app do banco. Registra isso no Prisma."

Observar: se vai ao "+" (caminho errado: lança como despesa) ou à fatura do cartão ("Pagar fatura").
**Não corrigir durante.** No fim, se ela lançou como despesa, mostrar o caminho certo e **excluir o
lançamento errado junto com ela**, para não deixar o mês dobrado.

**Tarefa 5. Entrou ou saiu** *(premissa 8; teste de 5 segundos)*
Abrir a lista de Lançamentos de um mês com receitas e despesas, mostrar por **5 segundos** e tirar da
frente dela.
> "Dos lançamentos que você viu, qual foi dinheiro que entrou?"

Observar: se acertou, e o que ela usou para decidir (cor, sinal de menos, ícone, nome).

**Tarefa 6. Os dias do cartão** *(premissa 7; só para quem tem cartão)*
> "Qual é o dia em que a fatura do seu cartão fecha? E o dia em que vence?"

Primeiro de memória, depois conferindo no app do banco. Comparar com o cadastrado no Prisma (C1).

### 8.3 Perguntas finais (5 a 10 min)

1. "Da última vez que você abriu o Prisma, foi para saber o quê?" *(premissa 3)*
2. "Você lança na hora da compra ou depois? Quando é depois, por quê?" *(premissa 1)*
3. "Quanto do que você gasta você acha que está no Prisma?" *(premissa 1)*
4. "O que mais te irrita no app?"
5. "O que faria você abrir o app todo dia?"
6. "Se o Prisma sumisse amanhã, o que você perderia?" *(valor)*
7. "Se você esquecer a senha, o que você faria?" *(premissa 11)*

### 8.4 Folha de anotação

| Tarefa | Conseguiu sozinho? (sim / com ajuda / desistiu) | Tempo | O que travou ou surpreendeu |
|---|---|---|---|
| 1. Chegar ao app | | | |
| 2. Lançar o último gasto | | | |
| 3. Quanto gastou em setembro (esperado: ____ / leu: ____) | | | |
| 4. Pagar a fatura (caminho usado: ____) | | | |
| 5. Entrou ou saiu | | | |
| 6. Dias do cartão (memória: __/__, banco: __/__, Prisma: __/__) | | | |

Respostas das perguntas finais: anotar as palavras da pessoa, não o resumo.

---

## 9. Experimentos técnicos

### E1. NFC-e e CNPJ (premissa 13), antes do `docs/fase-3.md`

1. Juntar **10 notas fiscais reais** (cupom com QR Code), de pelo menos **3 estados**.
2. Para cada uma, anotar: o QR foi lido pela câmera do iPhone? A página da SEFAZ abriu sem captcha? Dá
   para extrair CNPJ, data, valor total e itens? Em quanto tempo?
3. Consultar os CNPJs numa API pública gratuita e anotar o limite de consultas, o nome fantasia e o
   CNAE que voltam.
4. **Critério:** se menos de 8 em 10 notas funcionam de ponta a ponta, rever o escopo da Fase 3 antes
   de escrever o plano.

### E2. Fatura em PDF (premissa 14), antes do `docs/fase-4.md`

1. Separar **3 faturas reais** do mesmo cartão, de meses em que houve lançamento manual no Prisma.
2. Extrair as linhas à mão (ou com um script descartável) e comparar com a consulta A3 do mesmo mês.
3. Medir: quantas linhas batem por valor, quantas por valor e data, e quantas descrições seriam
   reconhecíveis.
4. **Critério:** se a deduplicação óbvia (mesmo valor, data próxima) pegar menos de 90%, a importação
   precisa de revisão manual pela pessoa, e isso entra no desenho da Fase 4.

---

## 10. Outros acompanhamentos

- **D1. Checklist de autonomia** *(premissa 11)*: para cada pessoa, ela consegue sozinha (sim/não):
  trocar a senha? recuperar o acesso se esquecer? exportar os dados? excluir a conta?
- **F1. Origem dos bugs** *(premissa 15)*: a cada bug, anotar quem achou (teste automatizado, revisão
  pela tela, ou usuário). Se a maioria vier de usuários, os testes do frontend não estão bastando.

---

## 11. Evidências

### Premissa 7: o cartão Itaú do dono (2026-09-27)

Datas tiradas dos PDFs das faturas de fevereiro a outubro de 2026 e do app do Itaú. As 9 seguem a
mesma regra, sem exceção:

1. **O vencimento nominal é sempre o dia 4** (o cartão estava cadastrado no Prisma com vencimento 5).
2. **O fechamento (emissão) é 7 dias antes do vencimento nominal:** 28/jan, 25/fev, 28/mar, 27/abr,
   28/mai, 27/jun, 28/jul, 28/ago, 27/set; previsto 28/out. Varia porque os meses têm tamanhos
   diferentes.
3. **O "melhor dia de compra" é o próprio dia do fechamento** (app: melhor dia 28/out, fechamento
   previsto 28/out): a compra feita nesse dia já vai para a fatura seguinte. O último dia que entra é
   o anterior.
4. **Vencimento em dia não útil vai para o próximo dia útil.** O PDF mostra o nominal (04) e o app, o
   dia útil: abr e jul (sábado → segunda 06), jun (Corpus Christi, 04/06 → 05/06), out (domingo →
   segunda 05; só aqui o PDF já trouxe o 05).
5. **A "Previsão próx. Fechamento" do PDF não é confiável:** o PDF de janeiro previu 28/02 (foi
   25/02); o de fevereiro previu 25/03 (foi 28/03).
6. **A regra já mudou antes:** segundo o dono, o cartão fechava 10 dias antes do vencimento; hoje são 7.

**O que isso mostra:** a premissa é mais frágil do que parecia. Não basta a pessoa saber os dias:
no Itaú, o fechamento **não é um dia fixo do mês**, e sim "N dias antes do vencimento", com N que o
banco pode mudar. Com o cadastro atual (fecha 26, vence 5), em 7 de 10 meses uma compra feita no dia
27 vai para a fatura seguinte (mês errado no Resumo), e em fevereiro compras de 25 e 26 vão para uma
fatura anterior à do banco. **Nenhum dia fixo acerta todos os meses.** Discussão da correção no
`PLAN.md` ("Cartão que fecha N dias antes do vencimento").

### Premissa 7: o segundo cartão Itaú do dono (2026-09-29)

Fatura com fechamento em **29/09/2026** e vencimento em **07/10/2026** (o dono escreveu 07/09; o
vencimento vem depois do fechamento). No dia 29/09, o app do Itaú já mostrava a fatura **fechada**; o
Prisma, **aberta**.

**O que isso mostra:**

1. **O Itaú fecha no início do dia do fechamento.** A data que o banco chama de fechamento é o melhor
   dia de compra (seção 11, item 3): a compra desse dia já é da fatura seguinte, e no próprio dia a
   fatura aparece fechada. O Prisma trata o dia do fechamento como o último que entra (`docs/fase-1.md`,
   2.1, regra 1): a compra do dia 29 cai na fatura de outubro e o status só vira "Fechada" no dia 30
   (`statements.ts`: fechada quando `closingDate < hoje`). São dois erros de um dia: a compra do dia do
   fechamento na fatura errada, e o status aberto no dia em que o banco já fechou.
2. **Também neste cartão o fechamento é contado a partir do vencimento:** 8 dias antes (07/10 → 29/09),
   não 7 como no outro. O N muda de cartão para cartão, até no mesmo banco.
3. **Contorno até a 2.21, sem código:** cadastrar o cartão com o fechamento **um dia antes** do que o
   banco mostra (28 em vez de 29). O Prisma passa a fechar no dia certo e a pôr a compra do dia 29 na
   fatura seguinte. Continua valendo o limite do dia fixo (seção acima): o dia muda com o tamanho do mês.
   Faturas que já existem não mudam com o cadastro; para elas, o ajuste de datas da fatura (2.20).

A especificação da 2.21 já resolve os dois pontos (`docs/fase-2.md`, 2.12, regra 2): o Prisma guarda
o último dia que entra (vencimento − N − 1) e a tela diz "Compras até 28/09", sem usar a palavra do banco.
Falta decidir, ao retomar, se o modelo de dia fixo também passa a ler o dia informado como o do banco
(exclusivo), já que o Itaú, nos dois cartões, conta assim.

## 12. Registro de resultados

Preencher conforme as validações acontecem. Cada conclusão que mudar o produto vira decisão no
`PLAN.md`.

| Premissa | Data | Evidência (contagens, observações) | Conclusão (confirmada / refutada / inconclusiva) | Decisão |
|---|---|---|---|---|
| 1 | | | | |
| 2 | | | | |
| 3 | | | | |
| 6 | | | | |
| 7 | 2026-09-27 | Cartão Itaú do dono: fecha 7 dias antes do vencimento (antes eram 10), dia fixo não representa (seção 11) | Refutada para o Itaú | Etapas 2.20 a 2.22 no `PLAN.md` (fatura alinhada ao banco) |
| 7 | 2026-09-29 | Segundo cartão Itaú: fechamento 29/09, vencimento 07/10 (8 dias antes); no dia 29 o banco já mostrava fechada e o Prisma, aberta (seção 11) | Refutada: o dia do fechamento do banco é exclusivo | Reforça a 2.21 (adiada); contorno: cadastrar o fechamento um dia antes |
| 8 | | | | |
| 9 | | | | |
| 10 | 2026-10-02 | Backup diário fora do Railway (R2, criptografado, alerta de silêncio) e restauração de prova: 13 tabelas com as mesmas contagens e o Resumo igual ao da produção | Parte resolvida: a perda de dados tem saída; seguem sem homologação, com migrations direto na produção, e o plano do Railway a confirmar | H.2b concluída no `PLAN.md`; roteiro em `docs/operacao.md`, seção 5 |
| 13 | | | | |
