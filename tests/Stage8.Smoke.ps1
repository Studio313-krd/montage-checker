param(
    [Parameter(Mandatory = $true)]
    [string]$BaseUri,
    [Parameter(Mandatory = $true)]
    [string]$OwnerLogin,
    [Parameter(Mandatory = $true)]
    [string]$OwnerPassword,
    [string]$PostgresContainer = "montage-monitor-postgres-1",
    [string]$ServerContainer = "montage-monitor-server-1",
    [string]$DatabaseUser = "montage_monitor",
    [string]$DatabaseName = "montage_monitor"
)

$ErrorActionPreference = "Stop"
$BaseUri = $BaseUri.TrimEnd("/")

function Assert-True {
    param([bool]$Condition, [string]$Message)
    if (-not $Condition) {
        throw "Проверка не пройдена: $Message"
    }
}

function Invoke-Json {
    param(
        [string]$Method,
        [string]$Path,
        [object]$Body,
        [hashtable]$Headers = @{}
    )

    $parameters = @{
        Method = $Method
        Uri = "$BaseUri$Path"
        Headers = $Headers
        ContentType = "application/json; charset=utf-8"
    }
    if ($null -ne $Body) {
        $parameters.Body = $Body | ConvertTo-Json -Depth 12 -Compress
    }

    Invoke-RestMethod @parameters
}

function Send-Screenshot {
    param(
        [string]$DeviceToken,
        [object]$Metadata,
        [byte[]]$Bytes
    )

    Add-Type -AssemblyName System.Net.Http
    $client = [System.Net.Http.HttpClient]::new()
    $multipart = [System.Net.Http.MultipartFormDataContent]::new()
    try {
        $client.DefaultRequestHeaders.Authorization =
            [System.Net.Http.Headers.AuthenticationHeaderValue]::new("Device", $DeviceToken)
        $metadataJson = $Metadata | ConvertTo-Json -Depth 8 -Compress
        $metadataContent = [System.Net.Http.StringContent]::new(
            $metadataJson,
            [System.Text.Encoding]::UTF8,
            "application/json")
        $fileContent = [System.Net.Http.ByteArrayContent]::new($Bytes)
        $fileContent.Headers.ContentType =
            [System.Net.Http.Headers.MediaTypeHeaderValue]::new("image/jpeg")
        $multipart.Add($metadataContent, "metadata")
        $multipart.Add($fileContent, "file", "smoke.jpg")
        $response = $client.PostAsync("$BaseUri/api/agent/screenshots", $multipart).GetAwaiter().GetResult()
        $content = $response.Content.ReadAsStringAsync().GetAwaiter().GetResult()
        [pscustomobject]@{ StatusCode = [int]$response.StatusCode; Content = $content }
        $response.Dispose()
    }
    finally {
        $multipart.Dispose()
        $client.Dispose()
    }
}

function Invoke-DatabaseScalar {
    param([string]$Sql)
    $result = docker exec $PostgresContainer psql -U $DatabaseUser -d $DatabaseName -Atc $Sql
    if ($LASTEXITCODE -ne 0) {
        throw "Не удалось выполнить проверочный SQL-запрос."
    }

    ($result | Out-String).Trim()
}

$temporaryRoot = [System.IO.Path]::GetTempPath()
$temporaryDirectory = Join-Path $temporaryRoot ("montage-monitor-stage8-" + [guid]::NewGuid().ToString("N"))
$jpegPath = Join-Path $temporaryDirectory "smoke.jpg"
New-Item -ItemType Directory -Path $temporaryDirectory | Out-Null

try {
    Add-Type -AssemblyName System.Drawing
    $bitmap = [System.Drawing.Bitmap]::new(8, 6)
    try {
        $bitmap.SetPixel(0, 0, [System.Drawing.Color]::DarkBlue)
        $bitmap.SetPixel(7, 5, [System.Drawing.Color]::Orange)
        $bitmap.Save($jpegPath, [System.Drawing.Imaging.ImageFormat]::Jpeg)
    }
    finally {
        $bitmap.Dispose()
    }
    [byte[]]$jpegBytes = [System.IO.File]::ReadAllBytes($jpegPath)

    $login = Invoke-Json POST "/api/auth/login" @{
        login = $OwnerLogin
        password = $OwnerPassword
    }
    $adminHeaders = @{ Authorization = "Bearer $($login.accessToken)" }
    $suffix = [guid]::NewGuid().ToString("N").Substring(0, 10)
    $employee = Invoke-Json POST "/api/employees" @{
        name = "Монтажёр Smoke $suffix"
        login = "smoke-$suffix"
        department = "Проверка"
        screenshotEnabled = $true
        screenshotIntervalMinutes = 1
        idleThresholdSeconds = 300
    } $adminHeaders
    $enrollmentToken = Invoke-Json POST "/api/admin/employees/$($employee.id)/agent-enrollment" @{
        expiresInHours = 1
    } $adminHeaders
    $enrollment = Invoke-Json POST "/api/agent/enroll" @{
        enrollmentToken = $enrollmentToken.enrollmentToken
        machineName = "SMOKE-PC-$suffix"
        windowsUser = "smoke-user"
        operatingSystem = "Windows smoke"
        agentVersion = "0.8.0"
    }
    $deviceHeaders = @{ Authorization = "Device $($enrollment.deviceAccessToken)" }

    $initialConfig = Invoke-Json GET "/api/agent/config" $null $deviceHeaders
    Assert-True ($initialConfig.screenshots.enabled -eq $true) "снимки должны быть включены"
    Assert-True ($initialConfig.screenshots.intervalMinutes -eq 1) "интервал должен быть равен минуте"
    Assert-True ($initialConfig.screenshots.excludedProcesses -contains "1Password.exe") "нет 1Password в privacy-исключениях"
    Assert-True ($initialConfig.screenshots.excludedProcesses -contains "KeePass.exe") "нет KeePass в privacy-исключениях"

    $settings = Invoke-Json PUT "/api/admin/screenshots/settings" @{
        captureMode = "AllMonitors"
        maxWidth = 1280
        jpegQuality = 60
        retentionDays = 30
    } $adminHeaders
    Assert-True ($settings.captureMode -eq "AllMonitors") "режим всех мониторов не сохранился"

    $privacyProcessName = "BankApp-$suffix.exe"
    $null = Invoke-Json POST "/api/admin/screenshots/privacy-processes" @{
        processName = $privacyProcessName
        displayName = "Банк Smoke"
    } $adminHeaders
    $updatedConfig = Invoke-Json GET "/api/agent/config" $null $deviceHeaders
    Assert-True ($updatedConfig.screenshots.captureMode -eq "AllMonitors") "Agent не получил AllMonitors"
    Assert-True ($updatedConfig.screenshots.maxWidth -eq 1280) "Agent не получил maxWidth"
    Assert-True ($updatedConfig.screenshots.jpegQuality -eq 60) "Agent не получил качество JPEG"
    Assert-True ($updatedConfig.screenshots.excludedProcesses -contains $privacyProcessName) "Agent не получил новое privacy-исключение"

    $screenshotEventId = [guid]::NewGuid()
    $oldTimestamp = [DateTimeOffset]::UtcNow.AddDays(-40)
    $metadata = @{
        eventId = $screenshotEventId
        agentId = $enrollment.agentId
        employeeId = $enrollment.employeeId
        computerId = $enrollment.computerId
        timestampUtc = $oldTimestamp.ToString("O")
        screenIndex = 0
        width = 8
        height = 6
        foregroundProcess = "explorer.exe"
        foregroundWindowTitle = "Проводник"
        humanState = "Active"
        machineState = "Normal"
    }
    $upload = Send-Screenshot $enrollment.deviceAccessToken $metadata $jpegBytes
    Assert-True ($upload.StatusCode -eq 201) "первая загрузка должна вернуть 201, получено $($upload.StatusCode): $($upload.Content)"
    $duplicateUpload = Send-Screenshot $enrollment.deviceAccessToken $metadata $jpegBytes
    Assert-True ($duplicateUpload.StatusCode -eq 200) "повторная загрузка должна быть идемпотентной"

    $fakeMetadata = $metadata.Clone()
    $fakeMetadata.eventId = [guid]::NewGuid()
    $fakeMetadata.width = 1
    $fakeMetadata.height = 1
    [byte[]]$fakeBytes = 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A
    $fakeUpload = Send-Screenshot $enrollment.deviceAccessToken $fakeMetadata $fakeBytes
    Assert-True ($fakeUpload.StatusCode -eq 415) "файл с поддельным типом должен быть отклонён"

    $lockedMetadata = $metadata.Clone()
    $lockedMetadata.eventId = [guid]::NewGuid()
    $lockedMetadata.humanState = "Locked"
    $lockedUpload = Send-Screenshot $enrollment.deviceAccessToken $lockedMetadata $jpegBytes
    Assert-True ($lockedUpload.StatusCode -eq 400) "снимок заблокированной сессии должен быть отклонён"

    $privacyEventId = [guid]::NewGuid()
    $heartbeatTimestamp = [DateTimeOffset]::UtcNow
    $heartbeat = Invoke-Json POST "/api/agent/heartbeat" @{
        eventId = [guid]::NewGuid()
        agentId = $enrollment.agentId
        employeeId = $enrollment.employeeId
        computerId = $enrollment.computerId
        agentVersion = "0.8.0"
        timestampUtc = $heartbeatTimestamp.ToString("O")
        windowsUser = "smoke-user"
        machineName = "SMOKE-PC-$suffix"
        humanState = "Active"
        machineState = "Normal"
        foregroundProcess = $privacyProcessName
        foregroundExecutablePath = "C:\\Smoke\\$privacyProcessName"
        foregroundWindowTitle = "Интернет-банк"
        idleSeconds = 0
        cpuLoadPercent = 5
        memoryLoadPercent = 20
        screenshotEvent = @{
            eventId = $privacyEventId
            timestampUtc = $heartbeatTimestamp.ToString("O")
            eventType = "SCREENSHOT_SKIPPED_PRIVACY"
            foregroundProcess = $privacyProcessName
            foregroundWindowTitle = "Интернет-банк"
        }
    } $deviceHeaders
    Assert-True ($null -ne $heartbeat.acceptedAtUtc) "privacy heartbeat не принят"

    $screenshotCount = [int](Invoke-DatabaseScalar "SELECT count(*) FROM screenshots WHERE event_id = '$screenshotEventId';")
    Assert-True ($screenshotCount -eq 1) "идемпотентность не обеспечена в БД"
    $privacyCount = [int](Invoke-DatabaseScalar "SELECT count(*) FROM activity_events WHERE event_id = '$privacyEventId' AND event_type = 'SCREENSHOT_SKIPPED_PRIVACY';")
    Assert-True ($privacyCount -eq 1) "privacy-событие не сохранено"
    $relativePath = Invoke-DatabaseScalar "SELECT storage_path FROM screenshots WHERE event_id = '$screenshotEventId';"
    Assert-True ($relativePath -match "^[0-9a-f]{32}/[0-9]{4}/[0-9]{2}/[0-9]{2}/[0-9a-f]{32}\.jpg$") "неверная структура пути: $relativePath"

    $null = docker restart $ServerContainer
    $deadline = [DateTimeOffset]::UtcNow.AddMinutes(2)
    do {
        Start-Sleep -Seconds 2
        $health = docker inspect --format "{{.State.Health.Status}}" $ServerContainer 2>$null
    } while ($health -ne "healthy" -and [DateTimeOffset]::UtcNow -lt $deadline)
    Assert-True ($health -eq "healthy") "сервер не восстановился после перезапуска"
    $deletedAt = Invoke-DatabaseScalar "SELECT COALESCE(file_deleted_at_utc::text, '') FROM screenshots WHERE event_id = '$screenshotEventId';"
    Assert-True (-not [string]::IsNullOrWhiteSpace($deletedAt)) "retention не отметил удаление файла"
    docker exec $ServerContainer test ! -f "/data/screenshots/$relativePath"
    Assert-True ($LASTEXITCODE -eq 0) "retention не удалил файл"

    $disabledEmployee = Invoke-Json PUT "/api/employees/$($employee.id)" @{
        name = $employee.name
        login = $employee.login
        department = $employee.department
        isActive = $true
        screenshotEnabled = $false
        screenshotIntervalMinutes = 1
        idleThresholdSeconds = 300
    } $adminHeaders
    Assert-True ($disabledEmployee.screenshotEnabled -eq $false) "флаг сотрудника не отключился"
    $disabledMetadata = $metadata.Clone()
    $disabledMetadata.eventId = [guid]::NewGuid()
    $disabledMetadata.timestampUtc = [DateTimeOffset]::UtcNow.ToString("O")
    $disabledUpload = Send-Screenshot $enrollment.deviceAccessToken $disabledMetadata $jpegBytes
    Assert-True ($disabledUpload.StatusCode -eq 403) "сервер должен отклонить снимок отключённого сотрудника"
    $disabledConfig = Invoke-Json GET "/api/agent/config" $null $deviceHeaders
    Assert-True ($disabledConfig.screenshots.enabled -eq $false) "Agent не получил отключение снимков"

    [pscustomobject]@{
        Result = "OK"
        EmployeeId = $employee.id
        AgentId = $enrollment.agentId
        ComputerId = $enrollment.computerId
        ScreenshotEventId = $screenshotEventId
        PrivacyEventId = $privacyEventId
    }
}
finally {
    if (Test-Path -LiteralPath $jpegPath) {
        Remove-Item -LiteralPath $jpegPath -Force
    }
    if (Test-Path -LiteralPath $temporaryDirectory) {
        Remove-Item -LiteralPath $temporaryDirectory -Force
    }
}
