param()

$ErrorActionPreference = "Stop"
$repositoryRoot = Split-Path -Parent $PSScriptRoot
$suffix = [guid]::NewGuid().ToString("N").Substring(0, 10)
$projectName = "montage-monitor-stage13-$suffix"
$appVersion = "stage13-$suffix"
$ownerLogin = "owner"
$ownerPassword = "Owner-Integration-1!"
$composePrefix = @(
    "compose",
    "--project-name", $projectName,
    "--env-file", (Join-Path $repositoryRoot ".env.example"),
    "-f", (Join-Path $repositoryRoot "docker-compose.yml"),
    "-f", (Join-Path $PSScriptRoot "docker-compose.integration.yml")
)
$environmentValues = @{
    MONITOR_DOMAIN = "integration.invalid"
    ACME_EMAIL = "integration@example.invalid"
    APP_VERSION = $appVersion
    POSTGRES_DB = "montage_monitor"
    POSTGRES_USER = "montage_monitor"
    POSTGRES_PASSWORD = "Database-Integration-1!"
    JWT_SIGNING_KEY = "stage13-integration-signing-key-with-more-than-32-bytes"
    OWNER_LOGIN = $ownerLogin
    OWNER_PASSWORD = $ownerPassword
    OWNER_DISPLAY_NAME = "Владелец Integration"
    BACKUP_ON_START = "false"
}
$previousEnvironment = @{}

function Invoke-Compose {
    param([string[]]$ComposeArguments)
    $arguments = $composePrefix + $ComposeArguments
    & docker @arguments
    if ($LASTEXITCODE -ne 0) {
        throw "docker compose завершился с кодом $LASTEXITCODE."
    }
}

function Wait-Healthy {
    param([string]$ContainerId, [TimeSpan]$Timeout)
    $deadline = [DateTimeOffset]::UtcNow.Add($Timeout)
    do {
        $status = docker inspect --format "{{.State.Health.Status}}" $ContainerId 2>$null
        if ($LASTEXITCODE -eq 0 -and $status -eq "healthy") { return }
        Start-Sleep -Seconds 2
    } while ([DateTimeOffset]::UtcNow -lt $deadline)
    throw "Контейнер $ContainerId не стал healthy за $($Timeout.TotalSeconds) секунд."
}

function Wait-HttpReady {
    param([string]$Uri, [TimeSpan]$Timeout)
    $deadline = [DateTimeOffset]::UtcNow.Add($Timeout)
    do {
        try {
            $response = Invoke-WebRequest -Uri $Uri -UseBasicParsing -TimeoutSec 3
            if ($response.StatusCode -eq 200) { return }
        }
        catch {
            Start-Sleep -Seconds 2
        }
    } while ([DateTimeOffset]::UtcNow -lt $deadline)
    throw "HTTP endpoint $Uri не стал доступен за $($Timeout.TotalSeconds) секунд."
}

$listener = [System.Net.Sockets.TcpListener]::new(
    [System.Net.IPAddress]::Loopback,
    0)
$listener.Start()
$integrationPort = ([System.Net.IPEndPoint]$listener.LocalEndpoint).Port
$listener.Stop()
$environmentValues.INTEGRATION_PORT = $integrationPort.ToString()

foreach ($name in $environmentValues.Keys) {
    $previousEnvironment[$name] = [Environment]::GetEnvironmentVariable($name, "Process")
    [Environment]::SetEnvironmentVariable($name, $environmentValues[$name], "Process")
}

try {
    Push-Location $repositoryRoot
    try {
        Invoke-Compose -ComposeArguments @("config", "--quiet")
        Invoke-Compose -ComposeArguments @("up", "-d", "--build", "postgres", "server")

        $serverPsArguments = $composePrefix + @("ps", "-q", "server")
        $postgresPsArguments = $composePrefix + @("ps", "-q", "postgres")
        $serverContainer = ((& docker @serverPsArguments) | Out-String).Trim()
        $postgresContainer = ((& docker @postgresPsArguments) | Out-String).Trim()
        if ([string]::IsNullOrWhiteSpace($serverContainer) -or
            [string]::IsNullOrWhiteSpace($postgresContainer)) {
            throw "Compose не вернул контейнеры Server/PostgreSQL."
        }
        Wait-Healthy $serverContainer ([TimeSpan]::FromMinutes(3))

        $baseUri = "http://127.0.0.1:$integrationPort"
        Wait-HttpReady "$baseUri/health/ready" ([TimeSpan]::FromMinutes(1))
        $api = & (Join-Path $PSScriptRoot "Stage13.ApiSmoke.ps1") `
            -BaseUri $baseUri `
            -OwnerLogin $ownerLogin `
            -OwnerPassword $ownerPassword `
            -PostgresContainer $postgresContainer
        $excel = & (Join-Path $PSScriptRoot "Stage11.Smoke.ps1") `
            -BaseUri $baseUri `
            -OwnerLogin $ownerLogin `
            -OwnerPassword $ownerPassword `
            -PostgresContainer $postgresContainer

        [pscustomobject]@{
            Result = "OK"
            Project = $projectName
            BaseUri = $baseUri
            ApiIntegration = $api.Result
            ExcelIntegration = $excel.Result
        }
    }
    finally {
        Pop-Location
    }
}
finally {
    $downArguments = $composePrefix + @("down", "--volumes", "--remove-orphans", "--rmi", "local")
    & docker @downArguments
    if ($LASTEXITCODE -ne 0) {
        Write-Warning "Не удалось полностью удалить disposable Compose project $projectName."
    }
    foreach ($name in $environmentValues.Keys) {
        [Environment]::SetEnvironmentVariable($name, $previousEnvironment[$name], "Process")
    }
}
