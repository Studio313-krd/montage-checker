#!/bin/sh
set -eu

log() {
    level="$1"
    event="$2"
    message="$3"
    printf '{"timestamp":"%s","level":"%s","event":"%s","message":"%s"}\n' \
        "$(date -u +%Y-%m-%dT%H:%M:%SZ)" "$level" "$event" "$message"
}

if [ "${RESTORE_CONFIRM:-}" != "ERASE_AND_RESTORE" ]; then
    log error restore_not_confirmed "set RESTORE_CONFIRM=ERASE_AND_RESTORE"
    exit 64
fi

restore_file="${RESTORE_FILE:-}"
if [ -z "$restore_file" ] || [ "${restore_file##*/}" != "$restore_file" ]; then
    log error restore_file_invalid "RESTORE_FILE must be a backup file name without a path"
    exit 64
fi

case "$restore_file" in
    montage_monitor_*.dump) ;;
    *) log error restore_file_invalid "expected montage_monitor_*.dump"; exit 64 ;;
esac

archive="/backups/postgres/$restore_file"
if [ ! -f "$archive" ]; then
    log error restore_file_missing "$restore_file"
    exit 66
fi
if ! pg_restore --list "$archive" >/dev/null 2>&1; then
    log error restore_archive_invalid "$restore_file"
    exit 65
fi
if [ "$PGDATABASE" = "postgres" ] || [ "$PGDATABASE" = "template0" ] || [ "$PGDATABASE" = "template1" ]; then
    log error restore_database_invalid "refusing to replace a maintenance database"
    exit 64
fi

log warning database_restore_started "$restore_file"
psql --dbname=postgres --set=ON_ERROR_STOP=1 \
    --set=target_database="$PGDATABASE" --set=target_owner="$PGUSER" <<'SQL'
SELECT pg_terminate_backend(pid)
FROM pg_stat_activity
WHERE datname = :'target_database' AND pid <> pg_backend_pid();
DROP DATABASE IF EXISTS :"target_database";
CREATE DATABASE :"target_database" OWNER :"target_owner";
SQL

if pg_restore \
    --dbname="$PGDATABASE" \
    --exit-on-error \
    --no-owner \
    --no-privileges \
    "$archive"; then
    log information database_restore_completed "$restore_file"
else
    log error database_restore_failed "$restore_file"
    exit 1
fi
