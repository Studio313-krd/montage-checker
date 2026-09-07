param(
    [Parameter(Mandatory = $true)]
    [string]$BaseUri,
    [Parameter(Mandatory = $true)]
    [string]$OwnerLogin,
    [Parameter(Mandatory = $true)]
    [string]$OwnerPassword,
    [Parameter(Mandatory = $true)]
    [string]$PostgresContainer,
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

function Get-HttpStatus {
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
        UseBasicParsing = $true
    }
    if ($null -ne $Body) {
        $parameters.Body = $Body | ConvertTo-Json -Depth 12 -Compress
    }
    try {
        $response = Invoke-WebRequest @parameters
        [int]$response.StatusCode
    }
    catch {
        if ($null -eq $_.Exception.Response) { throw }
        [int]$_.Exception.Response.StatusCode
    }
}

function New-EmployeeAndAgent {
    param(
        [string]$Name,
        [string]$Suffix,
        [hashtable]$AdminHeaders
    )

    $employee = Invoke-Json POST "/api/employees" @{
        name = $Name
        login = "integration-$Suffix"
        department = "Интеграционные тесты"
        screenshotEnabled = $false
        screenshotIntervalMinutes = 5
        idleThresholdSeconds = 300
    } $AdminHeaders
    $token = Invoke-Json POST "/api/admin/employees/$($employee.id)/agent-enrollment" @{
        expiresInHours = 1
    } $AdminHeaders
    $enrollmentRequest = @{
        enrollmentToken = $token.enrollmentToken
        machineName = "TEST-PC-$($Suffix.ToUpperInvariant())"
        windowsUser = "test-user"
        operatingSystem = "Windows integration"
        agentVersion = "0.13.0"
    }
    $enrollment = Invoke-Json POST "/api/agent/enroll" $enrollmentRequest
    [pscustomobject]@{
        Employee = $employee
        Enrollment = $enrollment
        EnrollmentRequest = $enrollmentRequest
    }
}

$owner = Invoke-Json POST "/api/auth/login" @{
    login = $OwnerLogin
    password = $OwnerPassword
}
$adminHeaders = @{ Authorization = "Bearer $($owner.accessToken)" }
$suffix = [guid]::NewGuid().ToString("N").Substring(0, 10)
$first = New-EmployeeAndAgent "Монтажёр Integration 1" "a-$suffix" $adminHeaders
$second = New-EmployeeAndAgent "Монтажёр Integration 2" "b-$suffix" $adminHeaders

$reusedTokenStatus = Get-HttpStatus POST "/api/agent/enroll" $first.EnrollmentRequest
Assert-True ($reusedTokenStatus -eq 401) "одноразовый enrollment token должен отклоняться после использования"

$eventId = [guid]::NewGuid()
$heartbeat = @{
    eventId = $eventId
    agentId = $first.Enrollment.agentId
    employeeId = $first.Enrollment.employeeId
    computerId = $first.Enrollment.computerId
    agentVersion = "0.13.0"
    timestampUtc = [DateTimeOffset]::UtcNow.ToString("O")
    windowsUser = "test-user"
    machineName = "TEST-PC-A-$($suffix.ToUpperInvariant())"
    humanState = "Active"
    machineState = "Normal"
    foregroundProcess = "Adobe Premiere Pro.exe"
    foregroundExecutablePath = "C:\Program Files\Adobe\Premiere.exe"
    foregroundWindowTitle = "Интеграционный проект"
    idleSeconds = 0
    cpuLoadPercent = 25
    memoryLoadPercent = 40
}
$firstDeviceHeaders = @{ Authorization = "Device $($first.Enrollment.deviceAccessToken)" }
$accepted = Invoke-Json POST "/api/agent/heartbeat" $heartbeat $firstDeviceHeaders
$duplicate = Invoke-Json POST "/api/agent/heartbeat" $heartbeat $firstDeviceHeaders
Assert-True ($accepted.eventId -eq $eventId) "первый heartbeat не принят"
Assert-True ($duplicate.eventId -eq $eventId) "идемпотентный повтор heartbeat не принят"

$secondHeartbeat = $heartbeat.Clone()
$secondHeartbeat.agentId = $second.Enrollment.agentId
$secondHeartbeat.employeeId = $second.Enrollment.employeeId
$secondHeartbeat.computerId = $second.Enrollment.computerId
$secondDeviceHeaders = @{ Authorization = "Device $($second.Enrollment.deviceAccessToken)" }
$foreignDuplicateStatus = Get-HttpStatus POST "/api/agent/heartbeat" $secondHeartbeat $secondDeviceHeaders
Assert-True ($foreignDuplicateStatus -eq 409) "UUID другого устройства должен вернуть conflict"

$heartbeatCount = docker exec $PostgresContainer psql -U $DatabaseUser -d $DatabaseName -tAc `
    "SELECT count(*) FROM heartbeats WHERE event_id = '$eventId';"
if ($LASTEXITCODE -ne 0) { throw "Не удалось проверить UUID heartbeat в PostgreSQL." }
Assert-True ([int]$heartbeatCount.Trim() -eq 1) "duplicate UUID создал больше одного heartbeat"

$viewerPassword = "Viewer-Integration-1!"
$viewer = Invoke-Json POST "/api/admin/users/" @{
    login = "viewer-$suffix"
    displayName = "Наблюдатель Integration"
    password = $viewerPassword
    role = "Viewer"
    employeeIds = @($first.Employee.id)
} $adminHeaders
$viewerLogin = Invoke-Json POST "/api/auth/login" @{
    login = $viewer.login
    password = $viewerPassword
}
$viewerHeaders = @{ Authorization = "Bearer $($viewerLogin.accessToken)" }
$visibleEmployees = @(Invoke-Json GET "/api/employees" $null $viewerHeaders)
Assert-True ($visibleEmployees.Count -eq 1) "VIEWER должен видеть только назначенного сотрудника"
Assert-True ($visibleEmployees[0].id -eq $first.Employee.id) "VIEWER получил чужого сотрудника"

$hiddenStatus = Get-HttpStatus GET "/api/employees/$($second.Employee.id)" $null $viewerHeaders
$manageStatus = Get-HttpStatus POST "/api/employees" @{
    name = "Запрещённый сотрудник"
    login = "forbidden-$suffix"
    screenshotEnabled = $false
    screenshotIntervalMinutes = 5
    idleThresholdSeconds = 300
} $viewerHeaders
$excelStatus = Get-HttpStatus GET "/api/reports/export.xlsx" $null $viewerHeaders
Assert-True ($hiddenStatus -eq 404) "чужой сотрудник должен быть скрыт от VIEWER"
Assert-True ($manageStatus -eq 403) "VIEWER не должен управлять сотрудниками"
Assert-True ($excelStatus -eq 403) "VIEWER не должен скачивать общий Excel"

[pscustomobject]@{
    Result = "OK"
    EnrollmentReuseStatus = $reusedTokenStatus
    HeartbeatRows = [int]$heartbeatCount.Trim()
    ForeignDuplicateStatus = $foreignDuplicateStatus
    ViewerEmployeeCount = $visibleEmployees.Count
    ViewerManageStatus = $manageStatus
    ViewerExcelStatus = $excelStatus
}
