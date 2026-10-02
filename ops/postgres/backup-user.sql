-- Usuário do backup (etapa H.2b): `prisma_backup`, que só lê. Se a senha dele vazar, ninguém altera
-- nem apaga dado com ela.
--
-- Rodar com psql, conectado como superusuário ao banco do app:
--   psql "postgresql://postgres@<host>:<porta>/<banco>" -f ops/postgres/backup-user.sql
-- A senha não fica neste arquivo nem no histórico: defina depois, no próprio psql, com
--   \password prisma_backup
--
-- Pode rodar de novo sem estrago: cria o usuário só se faltar.
-- `pg_read_all_data` (Postgres 14+) dá leitura em todas as tabelas e sequências, inclusive as que
-- as migrations criarem depois: o backup não quebra quando o app ganha uma tabela nova.

\set ON_ERROR_STOP on

BEGIN;

DO $$
BEGIN
    IF NOT EXISTS (SELECT FROM pg_roles WHERE rolname = 'prisma_backup') THEN
        CREATE ROLE prisma_backup LOGIN NOSUPERUSER NOCREATEDB NOCREATEROLE NOREPLICATION;
    END IF;
END
$$;

GRANT pg_read_all_data TO prisma_backup;

COMMIT;

-- Conferência: deve mostrar prisma_backup sem superusuário e com pg_read_all_data.
SELECT r.rolname AS usuario, r.rolsuper AS superusuario,
       pg_has_role(r.rolname, 'pg_read_all_data', 'member') AS le_tudo,
       pg_has_role(r.rolname, 'pg_write_all_data', 'member') AS escreve
FROM pg_roles r
WHERE r.rolname = 'prisma_backup';
