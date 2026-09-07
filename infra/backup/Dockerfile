FROM postgres:18.6-alpine3.23

RUN mkdir -p /backups/postgres /backups/screenshots \
    && chown -R postgres:postgres /backups

COPY --chmod=755 infra/backup/backup.sh /usr/local/bin/montage-backup
COPY --chmod=755 infra/backup/healthcheck.sh /usr/local/bin/montage-backup-health
COPY --chmod=755 infra/backup/restore.sh /usr/local/bin/montage-restore

USER postgres
ENTRYPOINT ["/usr/local/bin/montage-backup"]
