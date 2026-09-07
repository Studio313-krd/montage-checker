param(
    [Parameter(Mandatory = $true)]
    [switch]$AcknowledgeDatabaseReplacement,
    [string]$EnvironmentFile = ".env.example",
    [string]$PostgresContainer = "montage-monitor-postgres-1",
    [string]$BackupContainer = "montage-monitor-backup-1",
    [string]$DatabaseUser = "montage_monitor",
    [string]$DatabaseName = "montage_monitor"
)

$ErrorActionPreference = "Stop"
$projectRoot = Split-Path -Parent $PSScriptRoot
$environmentPath = Join-Path $projectRoot $EnvironmentFile

function Assert-True {
    param([bool]$Condition, [string]$Message)
    if (-not $Condition) { throw "Проверка не пройдена: $Message" }
}

function Invoke-Compose {
    & docker compose --env-file $environmentPath @args
    return $LASTEXITCODE
}

if (-not $AcknowledgeDatabaseReplacement) {
    throw "Тест выполняет полный restore. Передайте -AcknowledgeDatabaseReplacement только для временного стенда."
}

Push-Location $projectRoot
try {
    Invoke-Compose config --quiet | Out-Null
    Assert-True ($LASTEXITCODE -eq 0) "docker-compose.yml не прошёл валидацию"

    $postgresPorts = docker inspect --format '{{json .HostConfig.PortBindings}}' $PostgresContainer
    $serverPorts = docker inspect --format '{{json .HostConfig.PortBindings}}' montage-monitor-server-1
    Assert-True ($postgresPorts.Trim() -eq '{}') "PostgreSQL не должен публиковать host ports"
    Assert-True ($serverPorts.Trim() -eq '{}') "Server не должен публиковать host ports"

    docker exec $PostgresContainer psql -U $DatabaseUser -d $DatabaseName -v ON_ERROR_STOP=1 `
        -c "CREATE TABLE stage12_restore_probe (value text NOT NULL); INSERT INTO stage12_restore_probe VALUES ('backup-restore-ok');"
    if ($LASTEXITCODE -ne 0) { throw "Не удалось создать restore probe." }

    docker exec $BackupContainer /usr/local/bin/montage-backup --once
    if ($LASTEXITCODE -ne 0) { throw "Ручной backup завершился ошибкой." }
    docker exec $BackupContainer /usr/local/bin/montage-backup-health
    if ($LASTEXITCODE -ne 0) { throw "Backup healthcheck завершился ошибкой." }

    $backupPaths = @(docker exec $BackupContainer find /backups/postgres -maxdepth 1 -type f -name 'montage_monitor_*.dump')
    $backupPath = $backupPaths | Sort-Object | Select-Object -Last 1
    Assert-True (-not [string]::IsNullOrWhiteSpace($backupPath)) "dump не создан"
    $backupFile = Split-Path $backupPath.Trim() -Leaf

    $env:RESTORE_FILE = $backupFile
    $env:RESTORE_CONFIRM = ""
    Invoke-Compose run --rm restore | Out-Null
    Assert-True ($LASTEXITCODE -eq 64) "restore без подтверждения должен завершиться кодом 64"

    Invoke-Compose stop server backup | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "Не удалось остановить Server и backup." }

    docker exec $PostgresContainer psql -U $DatabaseUser -d $DatabaseName -v ON_ERROR_STOP=1 `
        -c "DROP TABLE stage12_restore_probe;"
    if ($LASTEXITCODE -ne 0) { throw "Не удалось удалить restore probe." }

    $env:RESTORE_CONFIRM = "ERASE_AND_RESTORE"
    Invoke-Compose run --rm restore | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "Restore завершился ошибкой." }

    $probe = docker exec $PostgresContainer psql -U $DatabaseUser -d $DatabaseName -tAc `
        "SELECT value FROM stage12_restore_probe;"
    Assert-True ($probe.Trim() -eq "backup-restore-ok") "данные из dump не восстановлены"

    docker exec $PostgresContainer psql -U $DatabaseUser -d $DatabaseName -v ON_ERROR_STOP=1 `
        -c "DROP TABLE stage12_restore_probe;" | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "Не удалось удалить restore probe после проверки." }

    [pscustomobject]@{
        Result = "OK"
        BackupFile = $backupFile
        RestoreProbe = $probe.Trim()
        PublishedDatabasePorts = 0
        PublishedServerPorts = 0
    }
}
finally {
    $env:RESTORE_FILE = $null
    $env:RESTORE_CONFIRM = $null
    Invoke-Compose up -d server backup | Out-Null
    Pop-Location
}
