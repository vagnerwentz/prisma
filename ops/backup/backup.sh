#!/usr/bin/env bash
# Backup do banco de produção (etapa H.2b): pg_dump → age → destino (o R2, em produção).
# Roda como cron no serviço `backup` do Railway e termina; roteiro em docs/operacao.md, seção 5.
#
# Variáveis:
#   PGHOST, PGPORT, PGDATABASE, PGUSER, PGPASSWORD  conexão (as do libpq), com o `prisma_backup`
#   AGE_RECIPIENT    chave PÚBLICA do age (age1...). A privada nunca entra aqui.
#   BACKUP_DEST      destino no rclone: `r2:<bucket>` em produção, uma pasta nos testes
#   HEALTHCHECK_URL  opcional: o endereço do Healthchecks.io (avisa início, sucesso e falha)
#
# A cópia é criptografada antes de tocar o disco e só sobe se o pg_dump terminar bem: nunca fica no
# destino uma cópia cortada com cara de válida. Qualquer falha sai com status diferente de zero.

set -euo pipefail

for var in PGHOST PGDATABASE PGUSER PGPASSWORD AGE_RECIPIENT BACKUP_DEST; do
    if [[ -z "${!var:-}" ]]; then
        echo "backup: missing variable $var" >&2
        exit 2
    fi
done

here="$(cd "$(dirname "$0")" && pwd)"

# Aviso ao Healthchecks.io. Falha no aviso não derruba o backup: o silêncio já vira alerta lá.
ping() {
    [[ -n "${HEALTHCHECK_URL:-}" ]] || return 0
    curl -fsS -m 10 --retry 3 -o /dev/null "${HEALTHCHECK_URL}$1" ||
        echo "backup: healthcheck ping failed (${1:-success})" >&2
}

work="$(mktemp -d)"
on_exit() {
    local status=$?
    rm -rf "$work"
    if ((status != 0)); then
        echo "backup: failed status=$status" >&2
        ping /fail
    fi
}
trap on_exit EXIT

ping /start

name="prisma-$(date -u +%Y-%m-%dT%H%M%SZ).dump.age"
file="$work/$name"

# pipefail: se o pg_dump falhar no meio, o pipeline falha mesmo com o age terminando bem.
pg_dump --format=custom --no-password | age --encrypt --recipient "$AGE_RECIPIENT" --output "$file"

if [[ "$(head -c 21 "$file")" != "age-encryption.org/v1" ]]; then
    echo "backup: output is not an age file" >&2
    exit 3
fi
bytes="$(wc -c <"$file" | tr -d ' ')"

# Contagem por tabela, para comparar com a restauração (só números).
counts="$(psql -X -q -A -t -F ' ' --no-password -v ON_ERROR_STOP=1 -f "$here/counts.sql")"
while read -r table rows; do
    echo "backup: count $table $rows"
done <<<"$counts"

# --immutable: nunca sobrescreve um arquivo que já esteja no destino.
rclone copyto --immutable -q "$file" "$BACKUP_DEST/$name"

echo "backup: ok file=$name bytes=$bytes"
ping ""
