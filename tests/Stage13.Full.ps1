param(
    [switch]$SkipIntegration
)

$ErrorActionPreference = "Stop"
$repositoryRoot = Split-Path -Parent $PSScriptRoot

Push-Location $repositoryRoot
try {
    dotnet restore
    if ($LASTEXITCODE -ne 0) { throw "dotnet restore завершился с ошибкой." }
    dotnet build --no-restore
    if ($LASTEXITCODE -ne 0) { throw "dotnet build завершился с ошибкой." }
    dotnet test --no-build
    if ($LASTEXITCODE -ne 0) { throw "dotnet test завершился с ошибкой." }
    dotnet format --verify-no-changes --no-restore
    if ($LASTEXITCODE -ne 0) { throw "dotnet format обнаружил отклонения." }

    Push-Location (Join-Path $repositoryRoot "src\MontageMonitor.Web")
    try {
        npm ci
        if ($LASTEXITCODE -ne 0) { throw "npm ci завершился с ошибкой." }
        npm run lint
        if ($LASTEXITCODE -ne 0) { throw "npm run lint завершился с ошибкой." }
        npm run build
        if ($LASTEXITCODE -ne 0) { throw "npm run build завершился с ошибкой." }
    }
    finally {
        Pop-Location
    }

    if (-not $SkipIntegration) {
        & (Join-Path $PSScriptRoot "Stage13.Integration.ps1")
    }
}
finally {
    Pop-Location
}
