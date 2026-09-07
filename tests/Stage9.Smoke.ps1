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
Add-Type -AssemblyName System.Drawing

function Assert-True {
    param([bool]$Condition, [string]$Message)
    if (-not $Condition) { throw "Проверка не пройдена: $Message" }
}

function Invoke-Json {
    param([string]$Method, [string]$Path, [object]$Body, [hashtable]$Headers = @{})
    $parameters = @{
        Method = $Method
        Uri = "$BaseUri$Path"
        Headers = $Headers
        ContentType = "application/json; charset=utf-8"
    }
    if ($null -ne $Body) { $parameters.Body = $Body | ConvertTo-Json -Depth 12 -Compress }
    Invoke-RestMethod @parameters
}

function Send-Screenshot {
    param([string]$DeviceToken, [object]$Metadata, [byte[]]$Bytes)
    Add-Type -AssemblyName System.Net.Http
    $client = [System.Net.Http.HttpClient]::new()
    $multipart = [System.Net.Http.MultipartFormDataContent]::new()
    try {
        $client.DefaultRequestHeaders.Authorization =
            [System.Net.Http.Headers.AuthenticationHeaderValue]::new("Device", $DeviceToken)
        $metadataContent = [System.Net.Http.StringContent]::new(
            ($Metadata | ConvertTo-Json -Depth 8 -Compress),
            [System.Text.Encoding]::UTF8,
            "application/json")
        $fileContent = [System.Net.Http.ByteArrayContent]::new($Bytes)
        $fileContent.Headers.ContentType =
            [System.Net.Http.Headers.MediaTypeHeaderValue]::new("image/jpeg")
        $multipart.Add($metadataContent, "metadata")
        $multipart.Add($fileContent, "file", "dashboard-smoke.jpg")
        $response = $client.PostAsync("$BaseUri/api/agent/screenshots", $multipart).GetAwaiter().GetResult()
        $content = $response.Content.ReadAsStringAsync().GetAwaiter().GetResult()
        if ([int]$response.StatusCode -ne 201) {
            throw "Снимок отклонён: HTTP $([int]$response.StatusCode) $content"
        }
        $response.Dispose()
    }
    finally {
        $multipart.Dispose()
        $client.Dispose()
    }
}

function New-DashboardJpeg {
    param([string]$Label, [System.Drawing.Color]$Color)
    $bitmap = [System.Drawing.Bitmap]::new(640, 360)
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    $font = [System.Drawing.Font]::new("Bahnschrift", 38, [System.Drawing.FontStyle]::Bold)
    $smallFont = [System.Drawing.Font]::new("Consolas", 14, [System.Drawing.FontStyle]::Regular)
    $stripeBrush = [System.Drawing.SolidBrush]::new($Color)
    $stream = [System.IO.MemoryStream]::new()
    try {
        $graphics.Clear([System.Drawing.Color]::FromArgb(19, 28, 36))
        $graphics.FillRectangle($stripeBrush, 0, 0, 12, 360)
        $graphics.DrawString($Label, $font, [System.Drawing.Brushes]::White, 54, 132)
        $graphics.DrawString("MONTAGE MONITOR / TEST FRAME", $smallFont, [System.Drawing.Brushes]::LightGray, 57, 198)
        $bitmap.Save($stream, [System.Drawing.Imaging.ImageFormat]::Jpeg)
        $stream.ToArray()
    }
    finally {
        $stream.Dispose()
        $stripeBrush.Dispose()
        $smallFont.Dispose()
        $font.Dispose()
        $graphics.Dispose()
        $bitmap.Dispose()
    }
}

function New-DashboardEmployee {
    param(
        [string]$Name,
        [string]$Department,
        [string]$Process,
        [string]$WindowTitle,
        [string]$HumanState,
        [string]$MachineState,
        [int]$StartedMinutesAgo,
        [byte[]]$ScreenshotBytes,
        [hashtable]$AdminHeaders,
        [string]$Suffix
    )

    $loginPart = ($Name -replace '[^a-zA-Zа-яА-Я0-9]', '').ToLowerInvariant()
    $employee = Invoke-Json POST "/api/employees" @{
        name = $Name
        login = "$loginPart-$Suffix"
        department = $Department
        screenshotEnabled = $true
        screenshotIntervalMinutes = 5
        idleThresholdSeconds = 300
    } $AdminHeaders
    $token = Invoke-Json POST "/api/admin/employees/$($employee.id)/agent-enrollment" @{ expiresInHours = 1 } $AdminHeaders
    $enrollment = Invoke-Json POST "/api/agent/enroll" @{
        enrollmentToken = $token.enrollmentToken
        machineName = "EDIT-$($employee.name.Split(' ')[0].ToUpperInvariant())"
        windowsUser = $loginPart
        operatingSystem = "Windows 11 Pro"
        agentVersion = "0.8.0"
    }
    $startedAt = [DateTimeOffset]::UtcNow.AddMinutes(-$StartedMinutesAgo).ToString("O")
    $heartbeatBody = @{
        eventId = [guid]::NewGuid()
        agentId = $enrollment.agentId
        employeeId = $enrollment.employeeId
        computerId = $enrollment.computerId
        agentVersion = "0.8.0"
        timestampUtc = $startedAt
        windowsUser = $loginPart
        machineName = "EDIT-$($employee.name.Split(' ')[0].ToUpperInvariant())"
        humanState = $HumanState
        machineState = $MachineState
        foregroundProcess = $Process
        foregroundExecutablePath = "C:\\Program Files\\Smoke\\$Process"
        foregroundWindowTitle = $WindowTitle
        idleSeconds = $(if ($HumanState -eq "Idle") { $StartedMinutesAgo * 60 } else { 0 })
        cpuLoadPercent = $(if ($MachineState -eq "Normal") { 18 } else { 82 })
        memoryLoadPercent = 46
    }
    if ($MachineState -eq "Render") {
        $heartbeatBody.renderTelemetry = @{
            program = $Process
            processStartedAtUtc = $startedAt
            processCpuPercent = 79
            processWorkingSetBytes = 2147483648
            processIoReadBytesPerSecond = 3000000
            processIoWriteBytesPerSecond = 14000000
            childProcessCount = 2
            gpuLoadPercent = 71
            outputFolder = "D:\\RENDER"
            outputFile = "D:\\RENDER\\master.mp4"
            outputFileSizeBytes = 734003200
            detectionConfidence = 94
            detectionReason = "Media Encoder + рост выходного файла + process IO"
        }
    }
    if ($MachineState -eq "Proxy") {
        $heartbeatBody.proxyTelemetry = @{
            program = $Process
            processStartedAtUtc = $startedAt
            processCpuPercent = 68
            processWorkingSetBytes = 1610612736
            processIoReadBytesPerSecond = 6000000
            processIoWriteBytesPerSecond = 10000000
            childProcessCount = 1
            outputFolder = "D:\\PROXY"
            outputFile = "D:\\PROXY\\scene_04_Proxy.mov"
            outputFileSizeBytes = 419430400
            detectionConfidence = 91
            detectionReason = "Resolve + Proxy Folder + рост видеофайла"
            fileNameMatched = $true
        }
    }
    $deviceHeaders = @{ Authorization = "Device $($enrollment.deviceAccessToken)" }
    $null = Invoke-Json POST "/api/agent/heartbeat" $heartbeatBody $deviceHeaders

    Send-Screenshot $enrollment.deviceAccessToken @{
        eventId = [guid]::NewGuid()
        agentId = $enrollment.agentId
        employeeId = $enrollment.employeeId
        computerId = $enrollment.computerId
        timestampUtc = [DateTimeOffset]::UtcNow.ToString("O")
        screenIndex = 0
        width = 640
        height = 360
        foregroundProcess = $Process
        foregroundWindowTitle = $WindowTitle
        humanState = $HumanState
        machineState = $MachineState
    } $ScreenshotBytes

    [pscustomobject]@{ Employee = $employee; Enrollment = $enrollment; StartedAt = $startedAt }
}

$login = Invoke-Json POST "/api/auth/login" @{ login = $OwnerLogin; password = $OwnerPassword }
$adminHeaders = @{ Authorization = "Bearer $($login.accessToken)" }
$suffix = [guid]::NewGuid().ToString("N").Substring(0, 8)

$ivan = New-DashboardEmployee "Иван Лебедев" "Монтаж" "Adobe Premiere Pro.exe" "Ролик_Весна — Монтаж" "Active" "Normal" 42 (New-DashboardJpeg "PREMIERE / CUT 04" ([System.Drawing.Color]::FromArgb(27, 133, 136))) $adminHeaders $suffix
$petr = New-DashboardEmployee "Пётр Волков" "Экспорт" "Adobe Media Encoder.exe" "Очередь экспорта — Master 4K" "Idle" "Render" 34 (New-DashboardJpeg "RENDER / MASTER 4K" ([System.Drawing.Color]::FromArgb(202, 119, 26))) $adminHeaders $suffix
$anna = New-DashboardEmployee "Анна Морозова" "Цветокоррекция" "Resolve.exe" "DaVinci Resolve — Scene 04" "Active" "Proxy" 18 (New-DashboardJpeg "PROXY / SCENE 04" ([System.Drawing.Color]::FromArgb(121, 83, 143))) $adminHeaders $suffix
$sergey = New-DashboardEmployee "Сергей Орлов" "Монтаж" "explorer.exe" "Материалы проекта" "Idle" "Normal" 12 (New-DashboardJpeg "OFFLINE / LAST FRAME" ([System.Drawing.Color]::FromArgb(93, 106, 113))) $adminHeaders $suffix

$offlineAt = [DateTimeOffset]::UtcNow.AddMinutes(-12).ToString("O")
$sql = "UPDATE agents SET status = 'Offline', last_seen_at_utc = '$offlineAt' WHERE id = '$($sergey.Enrollment.agentId)'; UPDATE computers SET last_heartbeat_at_utc = '$offlineAt', last_online_at_utc = '$offlineAt' WHERE id = '$($sergey.Enrollment.computerId)';"
docker exec $PostgresContainer psql -U $DatabaseUser -d $DatabaseName -v ON_ERROR_STOP=1 -c $sql | Out-Null
if ($LASTEXITCODE -ne 0) { throw "Не удалось подготовить offline-карточку." }

$dashboard = Invoke-Json GET "/api/dashboard" $null $adminHeaders
Assert-True ($dashboard.refreshAfterSeconds -eq 15) "неверный polling interval"
Assert-True ($dashboard.employees.Count -eq 4) "Dashboard должен вернуть четыре карточки"
Assert-True (($dashboard.employees | Where-Object name -eq "Пётр Волков").machineState -eq "Render") "Render не отображается"
Assert-True (($dashboard.employees | Where-Object name -eq "Анна Морозова").machineState -eq "Proxy") "Proxy не отображается"
Assert-True (($dashboard.employees | Where-Object name -eq "Сергей Орлов").isOnline -eq $false) "Offline не отображается"
Assert-True (($dashboard.employees | Where-Object name -eq "Иван Лебедев").latestScreenshot.contentUrl -match '^/api/screenshots/.+/content$') "нет ссылки на последний снимок"

$screenshotUrl = ($dashboard.employees | Where-Object name -eq "Иван Лебедев").latestScreenshot.contentUrl
$screenshotResponse = Invoke-WebRequest -Uri "$BaseUri$screenshotUrl" -Headers $adminHeaders -UseBasicParsing
Assert-True ($screenshotResponse.StatusCode -eq 200) "защищённый снимок не отдаётся"
Assert-True ($screenshotResponse.Headers['Content-Type'] -match '^image/jpeg') "неверный Content-Type снимка"

$unauthorizedStatus = 0
try { Invoke-WebRequest -Uri "$BaseUri/api/dashboard" -UseBasicParsing | Out-Null }
catch { $unauthorizedStatus = [int]$_.Exception.Response.StatusCode }
Assert-True ($unauthorizedStatus -eq 401) "Dashboard без JWT должен вернуть 401"

[pscustomobject]@{
    Result = "OK"
    EmployeeCount = $dashboard.employees.Count
    OnlineCount = @($dashboard.employees | Where-Object isOnline -eq $true).Count
    RenderEmployeeId = $petr.Employee.id
    ProxyEmployeeId = $anna.Employee.id
    OfflineEmployeeId = $sergey.Employee.id
    ActiveEmployeeId = $ivan.Employee.id
}
