param(
    [Parameter(Mandatory = $true)]
    [string]$BaseUri,
    [Parameter(Mandatory = $true)]
    [string]$OwnerLogin,
    [Parameter(Mandatory = $true)]
    [string]$OwnerPassword,
    [string]$PostgresContainer = "montage-monitor-postgres-1",
    [string]$DatabaseUser = "montage_monitor",
    [string]$DatabaseName = "montage_monitor"
)

$ErrorActionPreference = "Stop"
$BaseUri = $BaseUri.TrimEnd("/")

function Assert-True {
    param([bool]$Condition, [string]$Message)
    if (-not $Condition) { throw "Проверка не пройдена: $Message" }
}

function Invoke-Json {
    param([string]$Path, [hashtable]$Headers)
    Invoke-RestMethod -Method GET -Uri "$BaseUri$Path" -Headers $Headers
}

$login = Invoke-RestMethod -Method POST -Uri "$BaseUri/api/auth/login" `
    -ContentType "application/json; charset=utf-8" `
    -Body (@{ login = $OwnerLogin; password = $OwnerPassword } | ConvertTo-Json -Compress)
$headers = @{ Authorization = "Bearer $($login.accessToken)" }
$employees = Invoke-Json "/api/employees" $headers
$ivan = $employees | Where-Object name -eq "Иван Лебедев" | Select-Object -First 1
Assert-True ($null -ne $ivan) "не найден сотрудник для timeline"

$today = [DateTimeOffset]::Now.Date
$fromUtc = [uri]::EscapeDataString($today.ToUniversalTime().ToString("O"))
$toUtc = [uri]::EscapeDataString($today.AddDays(1).ToUniversalTime().ToString("O"))
$zone = [uri]::EscapeDataString("Europe/Moscow")
$range = "fromUtc=$fromUtc&toUtc=$toUtc&timeZone=$zone"

$timeline = Invoke-Json "/api/employees/$($ivan.id)/timeline?$range" $headers
Assert-True ($timeline.applications.Count -gt 0) "timeline не содержит foreground-приложение"
Assert-True ($timeline.humanStates.Count -gt 0) "timeline не содержит HumanState"

$applications = Invoke-Json "/api/reports/applications?$range" $headers
Assert-True ($applications.applications.Count -ge 4) "отчёт приложений не содержит четыре карточки"
Assert-True (($applications.applications | Where-Object application -match "Premiere").durationSeconds -gt 0) "Premiere не агрегирован"

$renders = Invoke-Json "/api/reports/renders?$range" $headers
Assert-True (@($renders.renders | Where-Object type -eq "Render").Count -gt 0) "Render отсутствует"
Assert-True (@($renders.renders | Where-Object type -eq "Proxy").Count -gt 0) "Proxy отсутствует"
Assert-True (($renders.renders | Where-Object type -eq "Render" | Select-Object -First 1).detectionReason.Length -gt 0) "нет причины детектора"

$summary = Invoke-Json "/api/reports/summary?$range" $headers
Assert-True ($summary.employees.Count -eq 4) "сводка должна содержать четыре сотрудника"
Assert-True (($summary.employees | Where-Object employeeName -eq "Пётр Волков").renderSeconds -gt 0) "Render time не посчитан"
Assert-True (($summary.employees | Where-Object employeeName -eq "Анна Морозова").proxySeconds -gt 0) "Proxy time не посчитан"

$gallery = Invoke-Json "/api/screenshots?$range&application=Premiere&page=1&pageSize=24" $headers
Assert-True ($gallery.totalCount -eq 1) "фильтр галереи по приложению работает неверно"
$content = Invoke-WebRequest -Uri "$BaseUri$($gallery.screenshots[0].contentUrl)" -Headers $headers -UseBasicParsing
Assert-True ($content.StatusCode -eq 200) "скриншот не загружен"
Assert-True ($content.Headers['Content-Type'] -match '^image/jpeg') "галерея получила не JPEG"

$auditCount = docker exec $PostgresContainer psql -U $DatabaseUser -d $DatabaseName -tAc `
    "SELECT count(*) FROM audit_logs WHERE action = 'screenshot.view';"
if ($LASTEXITCODE -ne 0) { throw "Не удалось проверить аудит просмотра." }
Assert-True ([int]$auditCount.Trim() -gt 0) "просмотр скриншота не записан в аудит"

$invalidRangeStatus = 0
try {
    Invoke-WebRequest -Uri "$BaseUri/api/reports/summary?fromUtc=2026-01-02T00:00:00Z&toUtc=2026-01-01T00:00:00Z" -Headers $headers -UseBasicParsing | Out-Null
}
catch { $invalidRangeStatus = [int]$_.Exception.Response.StatusCode }
Assert-True ($invalidRangeStatus -eq 400) "обратный диапазон должен вернуть 400"

[pscustomobject]@{
    Result = "OK"
    TimelineApplications = $timeline.applications.Count
    ApplicationRows = $applications.applications.Count
    RenderRows = $renders.renders.Count
    SummaryEmployees = $summary.employees.Count
    GalleryItems = $gallery.totalCount
    ScreenshotAuditRows = [int]$auditCount.Trim()
}
