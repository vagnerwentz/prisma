# Consultas de saúde do sistema

Consultas SQL **só de leitura e só agregadas**, para saber como o Prisma está: quantas pessoas, o que usam, se o e-mail
sai, se a limpeza roda. Nenhuma devolve e-mail, descrição, nome ou valor de ninguém: só contagens e ids. Mantê-las assim
é de propósito (CLAUDE.md, seção 8: o sistema não lê dado de ninguém).

## Como rodar

- Sempre como `prisma_backup` (só lê), nunca como `prisma_app` nem `postgres`.
- O TCP Proxy do Postgres fica desligado: ligue só durante a consulta e desligue depois (`docs/operacao.md`).
- Em desenvolvimento, rode no banco local (`docker compose up -d`) para conferir a consulta antes.
- Consulta nova entra aqui com o que responde. Tabela nova com `user_id` não precisa mudar nada, mas confira as de
  "uso por tabela" (seção 3).

```bash
psql "<conexão do prisma_backup>" -f consulta.sql
```

Enums são gravados como **texto** (`'Sent'`, `'Failed'`...). `users.created_at` é a data de entrada da pessoa. Nas contas de
antes da coluna (migration `AddUserCreatedAt`) ela é **aproximada**: o registro mais antigo que a pessoa ainda tinha na
época (aceite dos termos, e-mail de confirmação, categorias, contas, lançamentos...), ou seja, "no máximo esta data". Da
migration em diante é exata.

---

## 1. Pessoas

```sql
-- Totais: contas, e-mails confirmados, nunca confirmaram, bloqueadas agora
SELECT
  count(*)                                              AS contas,
  count(*) FILTER (WHERE email_confirmed)               AS confirmadas,
  count(*) FILTER (WHERE NOT email_confirmed)           AS sem_confirmar,
  count(*) FILTER (WHERE lockout_end > now())           AS bloqueadas_agora
FROM users;
```

```sql
-- Cadastros por dia, últimos 30 dias
SELECT (created_at AT TIME ZONE 'America/Sao_Paulo')::date AS dia, count(*) AS cadastros
FROM users
WHERE created_at > now() - interval '30 days'
GROUP BY 1
ORDER BY 1 DESC;
```

```sql
-- Funil: cadastrou → confirmou → criou conta → lançou algo
SELECT
  count(*)                                                                  AS cadastrou,
  count(*) FILTER (WHERE u.email_confirmed)                                 AS confirmou,
  count(*) FILTER (WHERE EXISTS (SELECT 1 FROM accounts a
                                 WHERE a.user_id = u.id AND a.deleted_at IS NULL)) AS criou_conta,
  count(*) FILTER (WHERE EXISTS (SELECT 1 FROM transactions t
                                 WHERE t.user_id = u.id AND t.deleted_at IS NULL)) AS lancou
FROM users u;
```

```sql
-- Quem aceitou qual versão dos termos (para saber quantos ainda vão ver a tela de aceite)
SELECT terms_version, privacy_version, count(*) AS pessoas
FROM users
GROUP BY 1, 2
ORDER BY 1 DESC NULLS LAST, 2 DESC NULLS LAST;
```

## 2. Atividade

```sql
-- Pessoas ativas: lançaram algo nos últimos 7 e 30 dias (por quando o lançamento foi criado)
SELECT
  count(DISTINCT user_id) FILTER (WHERE created_at > now() - interval '7 days')  AS ativas_7d,
  count(DISTINCT user_id) FILTER (WHERE created_at > now() - interval '30 days') AS ativas_30d
FROM transactions
WHERE deleted_at IS NULL;
```

```sql
-- Lançamentos criados por dia, últimos 14 dias, e por quantas pessoas
SELECT (created_at AT TIME ZONE 'America/Sao_Paulo')::date AS dia,
       count(*) AS lancamentos,
       count(DISTINCT user_id) AS pessoas
FROM transactions
WHERE created_at > now() - interval '14 days' AND deleted_at IS NULL
GROUP BY 1
ORDER BY 1 DESC;
```

```sql
-- De onde vêm os lançamentos (digitado, importado...) e que tipo são
SELECT source, type, count(*) AS lancamentos
FROM transactions
WHERE deleted_at IS NULL
GROUP BY 1, 2
ORDER BY 3 DESC;
```

```sql
-- Quanto cada recurso é usado: pessoas distintas por funcionalidade
SELECT 'contas' AS recurso, count(DISTINCT user_id) AS pessoas FROM accounts WHERE deleted_at IS NULL
UNION ALL SELECT 'cartões',            count(DISTINCT user_id) FROM accounts WHERE deleted_at IS NULL AND closing_day IS NOT NULL
UNION ALL SELECT 'compras parceladas', count(DISTINCT user_id) FROM installment_purchases WHERE deleted_at IS NULL
UNION ALL SELECT 'se repete (séries)', count(DISTINCT user_id) FROM recurrences WHERE deleted_at IS NULL
UNION ALL SELECT 'carteira (ativos)',  count(DISTINCT user_id) FROM holdings WHERE deleted_at IS NULL
UNION ALL SELECT 'notas de corretagem', count(DISTINCT user_id) FROM trade_notes WHERE deleted_at IS NULL
ORDER BY 2 DESC;
```

```sql
-- Séries ativas por tipo e frequência; quantas esperam decisão (cobrança numa fatura já paga)
SELECT kind, frequency, count(*) FILTER (WHERE end_date IS NULL OR end_date >= current_date) AS ativas, count(*) AS total
FROM recurrences
WHERE deleted_at IS NULL
GROUP BY 1, 2
ORDER BY 1, 2;

SELECT count(*) AS pendencias_de_recorrencia FROM recurrence_pendings WHERE deleted_at IS NULL;
```

```sql
-- Lançamentos com estimativa ainda a conferir (o que enche o sino)
SELECT count(*) AS a_conferir, count(DISTINCT user_id) AS pessoas
FROM transactions
WHERE amount_estimated AND deleted_at IS NULL;
```

## 3. Tamanho e forma dos dados

```sql
-- Linhas por tabela e tamanho em disco (estimativa do Postgres: rápida)
SELECT relname AS tabela, n_live_tup AS linhas,
       pg_size_pretty(pg_total_relation_size(relid)) AS tamanho
FROM pg_stat_user_tables
ORDER BY pg_total_relation_size(relid) DESC
LIMIT 20;
```

```sql
-- Tamanho do banco todo
SELECT pg_size_pretty(pg_database_size(current_database())) AS banco;
```

```sql
-- Quem tem mais lançamentos (só o id, para achar uso fora do comum, não para ler dado)
SELECT user_id, count(*) AS lancamentos
FROM transactions
WHERE deleted_at IS NULL
GROUP BY 1
ORDER BY 2 DESC
LIMIT 10;
```

```sql
-- Itens excluídos aguardando a limpeza (devem sumir em ~30 min; número alto e parado = a tarefa não roda)
SELECT 'transactions' AS tabela, count(*) AS excluidos_pendentes, min(deleted_at) AS mais_antigo
FROM transactions WHERE deleted_at IS NOT NULL
UNION ALL SELECT 'accounts',  count(*), min(deleted_at) FROM accounts WHERE deleted_at IS NOT NULL
UNION ALL SELECT 'categories', count(*), min(deleted_at) FROM categories WHERE deleted_at IS NOT NULL
UNION ALL SELECT 'statements', count(*), min(deleted_at) FROM statements WHERE deleted_at IS NOT NULL;
```

## 4. E-mail

```sql
-- Fila por tipo e situação. 'Failed' e 'Pending' antigos merecem olhar
SELECT kind, status, count(*) AS emails, max(created_at) AS ultimo
FROM outbox_emails
GROUP BY 1, 2
ORDER BY 1, 2;
```

```sql
-- Pendentes há mais de 10 minutos (o worker envia a cada 5 s: isso indica problema com o Resend)
SELECT kind, count(*) AS parados, min(created_at) AS mais_antigo, max(attempts) AS maior_n_tentativas
FROM outbox_emails
WHERE status = 'Pending' AND created_at < now() - interval '10 minutes'
GROUP BY 1;
```

```sql
-- Motivos de falha (texto curto do erro, sem destinatário)
SELECT last_error, count(*) AS vezes, max(created_at) AS ultima
FROM outbox_emails
WHERE status = 'Failed'
GROUP BY 1
ORDER BY 2 DESC;
```

```sql
-- Taxa de confirmação: de quem recebeu o e-mail de confirmação, quantos confirmaram
SELECT count(DISTINCT e.user_id)                                   AS receberam,
       count(DISTINCT e.user_id) FILTER (WHERE u.email_confirmed)  AS confirmaram
FROM outbox_emails e
JOIN users u ON u.id = e.user_id
WHERE e.kind = 'ConfirmEmail' AND e.status = 'Sent';
```

## 5. Segurança e privacidade

```sql
-- Contas bloqueadas por senha errada, e com quantas tentativas
SELECT access_failed_count AS erros, count(*) AS contas
FROM users
WHERE access_failed_count > 0
GROUP BY 1
ORDER BY 1 DESC;
```

```sql
-- Contas apagadas (só o id e o instante ficam, por 90 dias, para reaplicar depois de restaurar backup)
SELECT count(*) AS apagadas_ultimos_90d, min(deleted_at) AS mais_antiga, max(deleted_at) AS mais_recente
FROM deleted_accounts;
```

```sql
-- Chaves do cookie (Data Protection): precisa haver ao menos uma
SELECT count(*) AS chaves FROM data_protection_keys;
```

## 6. Investimentos (dados de mercado, globais)

```sql
-- Catálogo de ativos: ativos e inativos por tipo; quando foi a última atualização (a tarefa roda às 4h)
SELECT kind, count(*) FILTER (WHERE inactive_since IS NULL) AS ativos,
       count(*) FILTER (WHERE inactive_since IS NOT NULL)   AS inativos
FROM assets
GROUP BY 1
ORDER BY 2 DESC;

SELECT max(updated_at) AS ultima_atualizacao_do_catalogo FROM assets;
```

```sql
-- Ativos pedidos ("Não encontrou? Avise a gente") que ainda não estão no catálogo, mais pedidos primeiro
SELECT r.symbol, count(*) AS pedidos
FROM asset_requests r
WHERE r.deleted_at IS NULL
  AND NOT EXISTS (SELECT 1 FROM assets a WHERE a.symbol = r.symbol AND a.inactive_since IS NULL)
GROUP BY 1
ORDER BY 2 DESC
LIMIT 20;
```

```sql
-- Eventos da empresa conferidos: "confere" × "não confere" por evento (o "não confere" pede revisão da curadoria)
SELECT corporate_event_id, verdict, count(*) AS pessoas
FROM event_reviews
WHERE deleted_at IS NULL
GROUP BY 1, 2
ORDER BY 3 DESC;
```

---

## Não faça por aqui

- `SELECT email ...`, `SELECT description ...` ou qualquer coluna de dado de uma pessoa: não é preciso para saber a saúde do
  sistema. Pedido de titular tem roteiro próprio (`docs/operacao.md`, seção 9; `ops/privacy/export-user.sql`).
- `UPDATE`, `DELETE`, `INSERT`: o `prisma_backup` não deixa, e correção de dado vai por migration ou pelo app.
- Colar o resultado de uma consulta com ids de usuário em issue, PR ou commit (o repositório é público).
