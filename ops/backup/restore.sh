#!/usr/bin/env bash
# Restaura uma cópia do backup (etapa H.2b) num banco Postgres. Roteiro em docs/operacao.md, seção 5.
#
# Uso: restore.sh <origem>
#   <origem>  arquivo local (/in/prisma-....dump.age) ou remoto no rclone (r2:<bucket>/prisma-....dump.age)
#
# Variáveis:
#   AGE_IDENTITY     caminho do arquivo com a chave PRIVADA do age
#   PGHOST, PGPORT, PGUSER, PGPASSWORD  Postgres de destino, com um usuário que cria banco
#   RESTORE_DB       banco de destino (padrão: prisma_restore)
#
# O `prisma_restore` é apagado e recriado a cada execução: é o banco da restauração de prova. Qualquer
# outro nome precisa existir sem nenhuma tabela, para nunca sobrescrever um banco em uso.
# A cópia aberta nunca vai para o disco: sai do age direto para o pg_restore.

set -euo pipefail

src="${1:?usage: restore.sh <file or r2:bucket/file>}"
: "${AGE_IDENTITY:?missing variable AGE_IDENTITY}"
db="${RESTORE_DB:-prisma_restore}"
here="$(cd "$(dirname "$0")" && pwd)"

admin=(psql -X -q -A -t --no-password -v ON_ERROR_STOP=1 -d postgres)

if [[ "$db" == "prisma_restore" ]]; then
    "${admin[@]}" -c "DROP DATABASE IF EXISTS prisma_restore WITH (FORCE)" -c "CREATE DATABASE prisma_restore"
else
    exists="$("${admin[@]}" -v db="$db" <<<"SELECT 1 FROM pg_database WHERE datname = :'db'")"
    if [[ -z "$exists" ]]; then
        echo "restore: database $db does not exist; create it empty first" >&2
        exit 2
    fi
    tables="$(psql -X -q -A -t --no-password -v ON_ERROR_STOP=1 -d "$db" \
        -c "SELECT count(*) FROM pg_tables WHERE schemaname = 'public'")"
    if [[ "$tables" != "0" ]]; then
        echo "restore: database $db already has $tables tables; refusing to overwrite" >&2
        exit 2
    fi
fi

# --no-owner e --no-acl: os objetos ficam com quem restaura (o usuário do Postgres de destino).
rclone cat "$src" |
    age --decrypt --identity "$AGE_IDENTITY" |
    pg_restore --no-owner --no-acl --exit-on-error --dbname="$db"

echo "restore: ok database=$db"
psql -X -q -A -t -F ' ' --no-password -v ON_ERROR_STOP=1 -d "$db" -f "$here/counts.sql" |
    while read -r table rows; do echo "restore: count $table $rows"; done
