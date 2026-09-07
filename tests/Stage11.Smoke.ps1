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
Add-Type -AssemblyName System.IO.Compression.FileSystem

function Assert-True {
    param([bool]$Condition, [string]$Message)
    if (-not $Condition) { throw "Проверка не пройдена: $Message" }
}

$login = Invoke-RestMethod -Method POST -Uri "$BaseUri/api/auth/login" `
    -ContentType "application/json; charset=utf-8" `
    -Body (@{ login = $OwnerLogin; password = $OwnerPassword } | ConvertTo-Json -Compress)
$headers = @{ Authorization = "Bearer $($login.accessToken)" }
$employees = Invoke-RestMethod -Method GET -Uri "$BaseUri/api/employees" -Headers $headers
Assert-True ($employees.Count -ge 2) "для проверки нужны минимум два сотрудника"

$today = [DateTimeOffset]::Now.Date
$fromUtc = [uri]::EscapeDataString($today.ToUniversalTime().ToString("O"))
$toUtc = [uri]::EscapeDataString($today.AddDays(1).ToUniversalTime().ToString("O"))
$zone = [uri]::EscapeDataString("Europe/Moscow")
$employeeIds = [uri]::EscapeDataString(($employees | Select-Object -First 2 -ExpandProperty id) -join ",")
$path = "/api/reports/export.xlsx?fromUtc=$fromUtc&toUtc=$toUtc&timeZone=$zone&employeeIds=$employeeIds"
$downloadPath = Join-Path ([IO.Path]::GetTempPath()) "montage-monitor-stage11-$([guid]::NewGuid()).xlsx"

try {
    $response = Invoke-WebRequest -Uri "$BaseUri$path" -Headers $headers -OutFile $downloadPath -PassThru -UseBasicParsing
    Assert-True ($response.StatusCode -eq 200) "сервер не вернул Excel"
    Assert-True ($response.Headers['Content-Type'] -match '^application/vnd.openxmlformats-officedocument.spreadsheetml.sheet') "неверный MIME type"
    $bytes = [IO.File]::ReadAllBytes($downloadPath)
    Assert-True ($bytes.Length -gt 1000) "файл Excel пуст"
    Assert-True ($bytes[0] -eq 0x50 -and $bytes[1] -eq 0x4b) "ответ не является ZIP/Open XML"

    $archive = [IO.Compression.ZipFile]::OpenRead($downloadPath)
    try {
        $entryNames = @($archive.Entries | ForEach-Object FullName)
        Assert-True ($entryNames -contains '[Content_Types].xml') "нет Open XML content types"
        Assert-True ($entryNames -contains 'xl/workbook.xml') "нет описания workbook"
        Assert-True (@($entryNames | Where-Object { $_ -match '^xl/worksheets/sheet\d+\.xml$' }).Count -eq 5) "ожидалось пять листов"

        $workbookEntry = $archive.GetEntry('xl/workbook.xml')
        $reader = [IO.StreamReader]::new($workbookEntry.Open())
        try { $workbookXml = $reader.ReadToEnd() } finally { $reader.Dispose() }
        foreach ($sheet in @('SUMMARY', 'TIMELINE', 'APPLICATIONS', 'RENDERS', 'IDLE')) {
            Assert-True ($workbookXml -match "name=`"$sheet`"") "нет листа $sheet"
        }
    }
    finally { $archive.Dispose() }

    $unauthorizedStatus = 0
    try { Invoke-WebRequest -Uri "$BaseUri$path" -UseBasicParsing | Out-Null }
    catch { $unauthorizedStatus = [int]$_.Exception.Response.StatusCode }
    Assert-True ($unauthorizedStatus -eq 401) "запрос без авторизации должен вернуть 401"

    $invalidStatus = 0
    try {
        Invoke-WebRequest -Uri "$BaseUri/api/reports/export.xlsx?fromUtc=$fromUtc&toUtc=$toUtc&employeeIds=wrong" `
            -Headers $headers -UseBasicParsing | Out-Null
    }
    catch { $invalidStatus = [int]$_.Exception.Response.StatusCode }
    Assert-True ($invalidStatus -eq 400) "некорректные employeeIds должны вернуть 400"

    $auditCount = docker exec $PostgresContainer psql -U $DatabaseUser -d $DatabaseName -tAc `
        "SELECT count(*) FROM audit_logs WHERE action = 'report.excel.downloaded';"
    if ($LASTEXITCODE -ne 0) { throw "Не удалось проверить аудит Excel." }
    Assert-True ([int]$auditCount.Trim() -gt 0) "скачивание Excel не записано в аудит"

    [pscustomobject]@{
        Result = "OK"
        Bytes = $bytes.Length
        Worksheets = 5
        SelectedEmployees = 2
        ExcelAuditRows = [int]$auditCount.Trim()
    }
}
finally {
    if (Test-Path -LiteralPath $downloadPath) { Remove-Item -LiteralPath $downloadPath -Force }
}
