# Imagem do backup (etapa H.2b): o cron do Railway roda o backup.sh e o contêiner termina.
# Também serve para a restauração no Mac, sem instalar Postgres, age nem rclone (docs/operacao.md, seção 5).
# Build na raiz do repositório: docker build -f ops/backup/Dockerfile -t prisma-backup .
#
# O pg_dump precisa ser da mesma versão principal do Postgres de produção (o Railway usa o 18) ou mais
# nova. Se o Railway mudar de versão, troque o pacote (e o Alpine, se a versão dele ainda não a tiver).
FROM alpine:3.23

RUN apk add --no-cache bash curl age rclone postgresql18-client \
    && adduser -D -H backup

COPY --chmod=755 ops/backup/backup.sh ops/backup/restore.sh /opt/backup/
COPY ops/backup/counts.sql /opt/backup/

USER backup
CMD ["/opt/backup/backup.sh"]
