#!/bin/sh
set -u

umask 077

log() {
    level="$1"
    event="$2"
    message="$3"
    printf '{"timestamp":"%s","level":"%s","event":"%s","message":"%s"}\n' \
        "$(date -u +%Y-%m-%dT%H:%M:%SZ)" "$level" "$event" "$message"
}

require_positive_number() {
    name="$1"
    value="$2"
    case "$value" in
        ''|*[!0-9]*) log error configuration_invalid "$name must be a positive integer"; exit 64 ;;
    esac
    if [ "$value" -lt 1 ]; then
        log error configuration_invalid "$name must be greater than zero"
        exit 64
    fi
}

BACKUP_INTERVAL_SECONDS="${BACKUP_INTERVAL_SECONDS:-86400}"
BACKUP_RETRY_SECONDS="${BACKUP_RETRY_SECONDS:-300}"
BACKUP_RETENTION_DAYS="${BACKUP_RETENTION_DAYS:-14}"
BACKUP_ON_START="${BACKUP_ON_START:-true}"
BACKUP_SCREENSHOTS="${BACKUP_SCREENSHOTS:-false}"

require_positive_number BACKUP_INTERVAL_SECONDS "$BACKUP_INTERVAL_SECONDS"
require_positive_number BACKUP_RETRY_SECONDS "$BACKUP_RETRY_SECONDS"
require_positive_number BACKUP_RETENTION_DAYS "$BACKUP_RETENTION_DAYS"

run_backup() {
    lock_path="/tmp/montage-monitor-backup.lock"
    if ! mkdir "$lock_path" 2>/dev/null; then
        log warning backup_skipped "another backup is already running"
        return 75
    fi

    stamp="$(date -u +%Y%m%dT%H%M%SZ)"
    database_name="montage_monitor_${stamp}.dump"
    database_temp="/backups/postgres/.${database_name}.$$"
    database_final="/backups/postgres/${database_name}"
    success=0

    log information database_backup_started "$database_name"
    if pg_dump \
        --format=custom \
        --compress=zstd:6 \
        --no-owner \
        --no-privileges \
        --file="$database_temp" \
        "$PGDATABASE" \
        && pg_restore --list "$database_temp" >/dev/null 2>&1; then
        mv "$database_temp" "$database_final"
        date +%s > /backups/last-success
        find /backups/postgres -type f -name 'montage_monitor_*.dump' \
            -mtime "+$BACKUP_RETENTION_DAYS" -delete
        log information database_backup_completed "$database_name"
        success=1
    else
        log error database_backup_failed "$database_name"
    fi
    rm -f "$database_temp"

    if [ "$success" -eq 1 ] && [ "$BACKUP_SCREENSHOTS" = "true" ]; then
        screenshot_name="screenshots_${stamp}.tar.gz"
        screenshot_temp="/backups/screenshots/.${screenshot_name}.$$"
        screenshot_final="/backups/screenshots/${screenshot_name}"
        log information screenshot_backup_started "$screenshot_name"
        if tar -czf "$screenshot_temp" -C /screenshots .; then
            mv "$screenshot_temp" "$screenshot_final"
            date +%s > /backups/last-screenshot-success
            find /backups/screenshots -type f -name 'screenshots_*.tar.gz' \
                -mtime "+$BACKUP_RETENTION_DAYS" -delete
            log information screenshot_backup_completed "$screenshot_name"
        else
            rm -f "$screenshot_temp"
            log error screenshot_backup_failed "$screenshot_name"
            success=0
        fi
    fi

    rmdir "$lock_path"
    [ "$success" -eq 1 ]
}

if [ "${1:-}" = "--once" ]; then
    run_backup
    exit $?
fi

if [ "$BACKUP_ON_START" != "true" ]; then
    sleep "$BACKUP_INTERVAL_SECONDS"
fi

while true; do
    if run_backup; then
        sleep "$BACKUP_INTERVAL_SECONDS"
    else
        log warning backup_retry_scheduled "$BACKUP_RETRY_SECONDS seconds"
        sleep "$BACKUP_RETRY_SECONDS"
    fi
done
