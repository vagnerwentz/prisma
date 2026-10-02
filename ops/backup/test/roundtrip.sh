#!/usr/bin/env bash
# Teste de ida e volta do backup (etapa H.2b), no Docker: roda igual no Mac e no CI.
#
# Uso: ops/backup/test/roundtrip.sh <schema.sql>
#   <schema.sql>  saída de `dotnet ef migrations script --idempotent`: o banco do teste tem o schema real.
#
# Sobe um Postgres 18 (o da produção) e um servidor HTTP que faz o papel do Healthchecks.io, numa rede só deles, e confere:
#   1. o prisma_backup só lê;
#   2. o backup gera um arquivo do age, sem nenhum dado em claro, e avisa início e sucesso;
#   3. a restauração devolve as mesmas contagens e os mesmos dados;
#   4. a restauração recusa um banco que já tem tabelas;
#   5. um pg_dump que morre no meio não deixa arquivo no destino e avisa a falha;
#   6. senha errada e variável faltando também falham, sem arquivo.

set -euo pipefail

schema="$(cd "$(dirname "${1:?usage: roundtrip.sh <schema.sql>}")" && pwd)/$(basename "$1")"
root="$(cd "$(dirname "$0")/../../.." && pwd)"
image=prisma-backup-test
net="prisma-backup-test-$$"
pg="$net-pg"
hc="$net-hc"
tmp="$(mktemp -d)"
marker="sonda-em-claro-42"

cleanup() {
    docker rm -f "$pg" "$hc" >/dev/null 2>&1 || true
    docker network rm "$net" >/dev/null 2>&1 || true
    rm -rf "$tmp"
}
trap cleanup EXIT
trap 'echo "FALHOU na linha $LINENO" >&2' ERR

fail() {
    echo "FALHOU: $*" >&2
    exit 1
}
step() { echo "--- $*"; }

sql() { docker exec -i "$pg" psql -X -q -v ON_ERROR_STOP=1 -U postgres "$@"; }

step "imagem, Postgres e servidor de avisos"
docker build -q -f "$root/ops/backup/Dockerfile" -t "$image" "$root" >/dev/null
docker network create "$net" >/dev/null
docker run -d --name "$pg" --network "$net" -e POSTGRES_PASSWORD=admin -e POSTGRES_DB=prisma postgres:18 >/dev/null
docker run -d --name "$hc" --network "$net" python:3-alpine python -u -m http.server 8000 >/dev/null
# O servidor provisório da inicialização não escuta TCP: esperar pelo TCP é esperar o servidor de verdade.
for _ in $(seq 60); do
    docker exec "$pg" pg_isready -q -h 127.0.0.1 -U postgres -d prisma && break
    sleep 1
done
docker exec "$pg" pg_isready -q -h 127.0.0.1 -U postgres -d prisma || fail "Postgres não subiu"

step "schema real, dados de sonda e usuários"
sql -d prisma <"$schema" >/dev/null
sql -d prisma <<SQL
CREATE TABLE backup_probe (
    id bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    label citext NOT NULL,
    payload bytea NOT NULL
);
INSERT INTO backup_probe (label, payload)
SELECT '$marker ' || i || ' ação', decode(md5(i::text), 'hex') FROM generate_series(1, 500) AS i;
SQL
sql -d prisma <"$root/ops/postgres/app-user.sql" >/dev/null
sql -d prisma <"$root/ops/postgres/backup-user.sql" >/dev/null
sql -d prisma -c "ALTER ROLE prisma_backup PASSWORD 'backup-test'"

step "1. o prisma_backup só lê"
if docker exec -e PGPASSWORD=backup-test "$pg" psql -X -q -h 127.0.0.1 -U prisma_backup -d prisma \
    -c "INSERT INTO backup_probe (label, payload) VALUES ('x', '')" 2>/dev/null; then
    fail "o prisma_backup conseguiu gravar"
fi

docker run --rm "$image" age-keygen 2>/dev/null >"$tmp/key.txt"
chmod 644 "$tmp/key.txt" # chave descartável do teste; o contêiner roda com outro uid
recipient="$(grep -o 'age1[0-9a-z]*' "$tmp/key.txt")"
mkdir -p "$tmp/out"
chmod 777 "$tmp/out"

run_backup() {
    docker run --rm --network "$net" -v "$tmp/out:/out" \
        -e PGHOST="$pg" -e PGDATABASE=prisma -e PGUSER=prisma_backup -e PGPASSWORD="$1" \
        -e AGE_RECIPIENT="$recipient" -e BACKUP_DEST=/out -e HEALTHCHECK_URL="http://$hc:8000/ping" \
        "${@:2}" "$image"
}
pings() { docker logs "$hc" 2>&1 | grep -o 'GET /ping[^ ]*' || true; }

step "2. backup"
run_backup backup-test >"$tmp/backup.log" 2>&1 || {
    cat "$tmp/backup.log"
    fail "o backup falhou"
}
files=("$tmp/out"/*)
[[ ${#files[@]} -eq 1 && -f "${files[0]}" ]] || fail "esperava um arquivo no destino"
file="$(basename "${files[0]}")"
[[ "$file" =~ ^prisma-[0-9]{4}-[0-9]{2}-[0-9]{2}T[0-9]{6}Z\.dump\.age$ ]] || fail "nome inesperado: $file"
[[ "$(head -c 21 "${files[0]}")" == "age-encryption.org/v1" ]] || fail "o arquivo não é do age"
if grep -aq "$marker" "${files[0]}"; then fail "dado em claro dentro do arquivo"; fi
grep -q "^backup: ok file=$file bytes=" "$tmp/backup.log" || fail "faltou a linha de sucesso no log"
grep -q "^backup: count backup_probe 500$" "$tmp/backup.log" || fail "faltou a contagem da sonda no log"
[[ "$(pings)" == $'GET /ping/start\nGET /ping' ]] || fail "avisos esperados: início e sucesso; vieram: $(pings)"

restore() {
    docker run --rm --network "$net" -v "$tmp/out:/in:ro" -v "$tmp/key.txt:/key.txt:ro" \
        -e AGE_IDENTITY=/key.txt -e PGHOST="$pg" -e PGUSER=postgres -e PGPASSWORD=admin \
        "$@" "$image" /opt/backup/restore.sh "/in/$file"
}

step "3. restauração"
for round in 1 2; do # a segunda prova que o prisma_restore é recriado
    restore >"$tmp/restore.log" 2>&1 || {
        cat "$tmp/restore.log"
        fail "a restauração falhou (rodada $round)"
    }
done
diff <(sed -n 's/^backup: count //p' "$tmp/backup.log") <(sed -n 's/^restore: count //p' "$tmp/restore.log") ||
    fail "contagens diferentes entre backup e restauração"
probe="SELECT md5(string_agg(id || label || encode(payload, 'hex'), ',' ORDER BY id)) FROM backup_probe"
[[ "$(sql -d prisma -A -t -c "$probe")" == "$(sql -d prisma_restore -A -t -c "$probe")" ]] ||
    fail "dados da sonda diferentes depois da restauração"
tables="$(sql -d prisma_restore -A -t -c "SELECT count(*) FROM pg_tables WHERE schemaname = 'public'")"
((tables > 10)) || fail "a restauração trouxe só $tables tabelas"

step "4. a restauração recusa um banco com tabelas"
if restore -e RESTORE_DB=prisma >"$tmp/refuse.log" 2>&1; then fail "restaurou por cima do banco em uso"; fi
grep -q "refusing to overwrite" "$tmp/refuse.log" || fail "recusa sem o motivo"
[[ "$(sql -d prisma -A -t -c "SELECT count(*) FROM backup_probe")" == "500" ]] || fail "o banco em uso mudou"

rm -f "$tmp/out"/*
expect_failure() {
    local label="$1"
    shift
    if run_backup "$@" >"$tmp/fail.log" 2>&1; then
        cat "$tmp/fail.log"
        fail "$label: o backup terminou bem"
    fi
    [[ -z "$(ls -A "$tmp/out")" ]] || fail "$label: ficou arquivo no destino"
}

step "5. pg_dump que morre no meio"
cat >"$tmp/pg_dump" <<'SH'
#!/bin/sh
# Imita um pg_dump interrompido: escreve parte da saída e falha.
printf 'PGDMP parcial'
exit 1
SH
chmod 755 "$tmp/pg_dump"
expect_failure "dump cortado" backup-test -v "$tmp/pg_dump:/usr/local/bin/pg_dump:ro"
pings | grep -qx 'GET /ping/fail' || fail "dump cortado: faltou o aviso de falha"

step "6. senha errada e variável faltando"
expect_failure "senha errada" senha-errada
expect_failure "sem AGE_RECIPIENT" backup-test -e AGE_RECIPIENT=

echo "OK: backup e restauração conferidos"
