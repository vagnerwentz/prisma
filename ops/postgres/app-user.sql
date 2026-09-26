-- Usuário próprio da API (etapa H.2a): `prisma_app`, dono do banco do app, sem superusuário.
-- O `postgres` fica só para administração.
--
-- Rodar com psql, conectado como superusuário ao banco do app:
--   psql "postgresql://postgres@<host>:<porta>/<banco>" -f ops/postgres/app-user.sql
-- A senha não fica neste arquivo nem no histórico: defina depois, no próprio psql, com
--   \password prisma_app
--
-- Pode rodar de novo sem estrago: cria o usuário só se faltar e reatribui o que for do app.
-- As sequências são de colunas identity e acompanham a tabela; a extensão citext continua do
-- superusuário (a migration usa CREATE EXTENSION IF NOT EXISTS, que só confere).

\set ON_ERROR_STOP on

BEGIN;

DO $$
BEGIN
    IF NOT EXISTS (SELECT FROM pg_roles WHERE rolname = 'prisma_app') THEN
        CREATE ROLE prisma_app LOGIN NOSUPERUSER NOCREATEDB NOCREATEROLE NOREPLICATION;
    END IF;
END
$$;

DO $$
DECLARE
    t record;
BEGIN
    EXECUTE format('ALTER DATABASE %I OWNER TO prisma_app', current_database());
    ALTER SCHEMA public OWNER TO prisma_app;
    FOR t IN SELECT tablename FROM pg_tables WHERE schemaname = 'public' LOOP
        EXECUTE format('ALTER TABLE public.%I OWNER TO prisma_app', t.tablename);
    END LOOP;
END
$$;

COMMIT;

-- Conferência: todas as tabelas do app devem aparecer com o dono prisma_app.
SELECT tableowner AS dono, count(*) AS tabelas
FROM pg_tables
WHERE schemaname = 'public'
GROUP BY tableowner;
