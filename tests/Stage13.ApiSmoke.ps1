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
    param([object]$Condition, [string]$Message)
    if ($Condition -is [array]) {
        throw "Проверка вернула массив вместо bool: $Message"
    }
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
    try {
        Invoke-RestMethod @parameters
    }
    catch {
        $details = $_.Exception.Message
        if ($null -ne $_.Exception.Response) {
            try {
                $reader = [System.IO.StreamReader]::new($_.Exception.Response.GetResponseStream())
                $responseBody = $reader.ReadToEnd()
                $reader.Dispose()
                if (-not [string]::IsNullOrWhiteSpace($responseBody)) {
                    $details = "$details Ответ: $responseBody"
                }
            }
            catch {
            }
        }
        throw "$Method $Path завершился ошибкой. $details"
    }
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

function New-EmployeeAndAgentLogin {
    param(
        [string]$Name,
        [string]$Suffix,
        [hashtable]$AdminHeaders,
        [string]$AgentVersion = "0.9.0",
        [switch]$UseLegacyLogin
    )

    $employee = Invoke-Json POST "/api/employees" @{
        name = $Name
        login = "integration-$Suffix"
        department = "Интеграционные тесты"
        screenshotEnabled = $false
        screenshotIntervalMinutes = 5
        idleThresholdSeconds = 300
    } $AdminHeaders
    $passwords = @(Invoke-Json GET "/api/admin/employees/agent-passwords" $null $AdminHeaders |
        ForEach-Object { $_ })
    $password = [string]($passwords |
        Where-Object { $_.employeeId -eq $employee.id } |
        Select-Object -ExpandProperty password)
    Assert-True ($password -match '^\d{4}$') "админка не вернула четырёхзначный пароль Agent"
    $loginRequest = @{
        password = $password
        machineName = "TEST-PC-$($Suffix.ToUpperInvariant())"
        windowsUser = "test-user"
        operatingSystem = "Windows integration"
        agentVersion = $AgentVersion
    }
    if ($UseLegacyLogin) {
        $loginRequest.login = $employee.login
    }
    else {
        $loginRequest.employeeId = $employee.id
    }
    $agentLogin = Invoke-Json POST "/api/agent/login" $loginRequest
    [pscustomobject]@{
        Employee = $employee
        Password = $password
        AgentLogin = $agentLogin
        LoginRequest = $loginRequest
    }
}

$owner = Invoke-Json POST "/api/auth/login" @{
    login = $OwnerLogin
    password = $OwnerPassword
}
$adminHeaders = @{ Authorization = "Bearer $($owner.accessToken)" }
$suffix = [guid]::NewGuid().ToString("N").Substring(0, 10)
$first = New-EmployeeAndAgentLogin "Монтажёр Integration 1" "a-$suffix" $adminHeaders
$second = New-EmployeeAndAgentLogin "Монтажёр Integration 2" "b-$suffix" $adminHeaders
$legacy = New-EmployeeAndAgentLogin `
    "Монтажёр Legacy" "legacy-$suffix" $adminHeaders "0.8.0" -UseLegacyLogin

$loginOptions = Invoke-Json GET "/api/agent/login-options" $null
$publicEmployees = @($loginOptions.employees)
Assert-True ($publicEmployees.Count -ge 3) "окно входа не получило список активных сотрудников"
$firstPublicEmployee = $publicEmployees | Where-Object { $_.employeeId -eq $first.Employee.id }
Assert-True ($null -ne $firstPublicEmployee) "новый сотрудник отсутствует в списке окна входа"
Assert-True ($firstPublicEmployee.name -eq $first.Employee.name) "в списке входа отображается неверное имя"
Assert-True ($null -eq $firstPublicEmployee.PSObject.Properties["login"]) `
    "публичный список сотрудников не должен раскрывать логины"

$wrongLoginRequest = $first.LoginRequest.Clone()
$wrongLoginRequest.password = if ($first.Password -eq "0000") { "0001" } else { "0000" }
$wrongAgentLoginStatus = Get-HttpStatus POST "/api/agent/login" $wrongLoginRequest
Assert-True ($wrongAgentLoginStatus -eq 401) "неверный пароль Agent должен отклоняться"
$removedEnrollmentStatus = Get-HttpStatus POST "/api/agent/enroll" @{}
Assert-True ($removedEnrollmentStatus -ge 400) "старый endpoint enrollment должен быть удалён"

$firstDeviceHeaders = @{ Authorization = "Device $($first.AgentLogin.deviceAccessToken)" }
$secondDeviceHeaders = @{ Authorization = "Device $($second.AgentLogin.deviceAccessToken)" }
$legacyDeviceHeaders = @{ Authorization = "Device $($legacy.AgentLogin.deviceAccessToken)" }
$operatorOptions = Invoke-Json GET "/api/agent/operators" $null $firstDeviceHeaders
$operatorCount = @($operatorOptions.employees).Count
Assert-True ($operatorCount -ge 3) "Agent не получил список активных сотрудников"
$legacyOperatorContract = Invoke-Json POST "/api/agent/operator-session" @{
    employeeId = $first.Employee.id
    pin = $first.Password
} $firstDeviceHeaders
Assert-True ($legacyOperatorContract.employeeId -eq $first.Employee.id) "Agent 0.9.1 не смог подтвердить старый формат выбора"
$loginOperatorContract = Invoke-Json POST "/api/agent/operator-session" @{
    login = $first.Employee.login
    password = $first.Password
} $firstDeviceHeaders
Assert-True ($loginOperatorContract.employeeId -eq $first.Employee.id) `
    "Agent 0.10.0 не смог подтвердить выбор по логину"

$eventId = [guid]::NewGuid()
$heartbeat = @{
    eventId = $eventId
    agentId = $first.AgentLogin.agentId
    employeeId = $second.AgentLogin.employeeId
    computerId = $first.AgentLogin.computerId
    agentVersion = "0.9.0"
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
$missingOperatorStatus = Get-HttpStatus POST "/api/agent/heartbeat" $heartbeat $firstDeviceHeaders
Assert-True ($missingOperatorStatus -eq 428) "новый Agent без выбора монтажёра должен получить 428"
$invalidPasswordStatus = Get-HttpStatus POST "/api/agent/operator-session" @{
    login = $second.Employee.login
    password = "99999"
} $firstDeviceHeaders
Assert-True ($invalidPasswordStatus -eq 400) "пароль неверного формата должен быть отклонён"
$wrongPassword = if ($second.Password -eq "0000") { "0001" } else { "0000" }
$wrongPasswordStatus = Get-HttpStatus POST "/api/agent/operator-session" @{
    login = $second.Employee.login
    password = $wrongPassword
} $firstDeviceHeaders
Assert-True ($wrongPasswordStatus -eq 401) "неверный пароль должен быть отклонён"
$firstOperatorSession = Invoke-Json POST "/api/agent/operator-session" @{
    employeeId = $second.Employee.id
    password = $second.Password
} $firstDeviceHeaders
Assert-True ($firstOperatorSession.employeeId -eq $second.Employee.id) "общий ПК не переключился на выбранного сотрудника"
$heartbeat.operatorSessionId = $firstOperatorSession.sessionId
$clockSkewHeartbeat = $heartbeat.Clone()
$clockSkewHeartbeat.eventId = [guid]::NewGuid()
$clockSkewHeartbeat.agentVersion = "0.10.3"
$confirmedStart = [DateTimeOffset]::Parse($firstOperatorSession.startedAtUtc)
$clockSkewHeartbeat.timestampUtc = $confirmedStart.AddSeconds(-5).ToString("O")
$clockSkewStatus = Get-HttpStatus POST "/api/agent/heartbeat" $clockSkewHeartbeat $firstDeviceHeaders
Assert-True ($clockSkewStatus -eq 428) "heartbeat до начала подтверждённой смены должен воспроизводить HTTP 428"
$clockSkewHeartbeat.timestampUtc = $confirmedStart.ToString("O")
$correctedClockAccepted = Invoke-Json POST "/api/agent/heartbeat" $clockSkewHeartbeat $firstDeviceHeaders
Assert-True ($correctedClockAccepted.eventId -eq $clockSkewHeartbeat.eventId) "heartbeat с серверным временем начала смены должен приниматься без нового входа"
$clockDashboard = Invoke-Json GET "/api/dashboard" $null $adminHeaders
$clockEmployee = @($clockDashboard.employees | Where-Object { $_.employeeId -eq $second.Employee.id })
Assert-True ($clockEmployee.Count -eq 1 -and $clockEmployee[0].isOnline) "после исправления времени сотрудник должен отображаться Online"
$heartbeat.timestampUtc = [DateTimeOffset]::UtcNow.ToString("O")
$accepted = Invoke-Json POST "/api/agent/heartbeat" $heartbeat $firstDeviceHeaders
$duplicate = Invoke-Json POST "/api/agent/heartbeat" $heartbeat $firstDeviceHeaders
Assert-True ($accepted.eventId -eq $eventId) "первый heartbeat не принят"
Assert-True ($duplicate.eventId -eq $eventId) "идемпотентный повтор heartbeat не принят"

$secondHeartbeat = $heartbeat.Clone()
$secondHeartbeat.agentId = $second.AgentLogin.agentId
$secondHeartbeat.employeeId = $second.AgentLogin.employeeId
$secondHeartbeat.computerId = $second.AgentLogin.computerId
$secondHeartbeat.operatorSessionId = $second.AgentLogin.operatorSession.sessionId
$secondHeartbeat.timestampUtc = [DateTimeOffset]::UtcNow.ToString("O")
$foreignDuplicateStatus = Get-HttpStatus POST "/api/agent/heartbeat" $secondHeartbeat $secondDeviceHeaders
Assert-True ($foreignDuplicateStatus -eq 409) "UUID другого устройства должен вернуть conflict"

$legacyHeartbeat = $heartbeat.Clone()
$legacyHeartbeat.eventId = [guid]::NewGuid()
$legacyHeartbeat.agentId = $legacy.AgentLogin.agentId
$legacyHeartbeat.employeeId = $legacy.AgentLogin.employeeId
$legacyHeartbeat.computerId = $legacy.AgentLogin.computerId
$legacyHeartbeat.agentVersion = "0.8.0"
$legacyHeartbeat.machineName = "TEST-PC-LEGACY-$($suffix.ToUpperInvariant())"
$legacyHeartbeat.timestampUtc = [DateTimeOffset]::UtcNow.ToString("O")
$legacyHeartbeat.Remove("operatorSessionId")
$legacyAccepted = Invoke-Json POST "/api/agent/heartbeat" $legacyHeartbeat $legacyDeviceHeaders
Assert-True ($legacyAccepted.eventId -eq $legacyHeartbeat.eventId) "старый Agent 0.8.0 потерял обратную совместимость"

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
    WrongAgentLoginStatus = $wrongAgentLoginStatus
    RemovedEnrollmentStatus = $removedEnrollmentStatus
    LegacyOperatorContract = "accepted"
    LoginOperatorContract = "accepted"
    HeartbeatRows = [int]$heartbeatCount.Trim()
    ForeignDuplicateStatus = $foreignDuplicateStatus
    MissingOperatorStatus = $missingOperatorStatus
    ClockSkewStatus = $clockSkewStatus
    CorrectedClockStatus = "accepted"
    WrongPasswordStatus = $wrongPasswordStatus
    LegacyAgentStatus = "accepted"
    ViewerEmployeeCount = $visibleEmployees.Count
    ViewerManageStatus = $manageStatus
    ViewerExcelStatus = $excelStatus
}
