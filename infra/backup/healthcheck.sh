#!/bin/sh
set -eu

interval="${BACKUP_INTERVAL_SECONDS:-86400}"
grace="${BACKUP_HEALTH_GRACE_SECONDS:-1800}"
case "$interval:$grace" in
    *[!0-9:]*) exit 1 ;;
esac

check_marker() {
    marker="$1"
    test -s "$marker"
    last="$(cat "$marker")"
    case "$last" in ''|*[!0-9]*) return 1 ;; esac
    now="$(date +%s)"
    age=$((now - last))
    maximum_age=$((interval + grace))
    [ "$age" -ge 0 ] && [ "$age" -le "$maximum_age" ]
}

check_marker /backups/last-success
latest="$(find /backups/postgres -type f -name 'montage_monitor_*.dump' | sort | tail -n 1)"
test -n "$latest"
pg_restore --list "$latest" >/dev/null 2>&1

if [ "${BACKUP_SCREENSHOTS:-false}" = "true" ]; then
    check_marker /backups/last-screenshot-success
fi
