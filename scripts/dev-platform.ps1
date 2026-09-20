param(
    [Parameter(Mandatory = $true)]
    [ValidateSet("Start", "Stop", "Status")]
    [string]$Action
)

$ErrorActionPreference = "Stop"
$ProgressPreference = "SilentlyContinue"

$repoRoot = Split-Path -Parent $PSScriptRoot
$logsDir = Join-Path $repoRoot ".devlogs"
$pidsDir = Join-Path $repoRoot ".devpids"

$nodeDir = Join-Path $repoRoot "services\restaurant-node\src\RestaurantNode.Api"
$agentDir = Join-Path $repoRoot "services\pos-agent\src\PosAgent.Api"
$backofficeDir = Join-Path $repoRoot "apps\backoffice"
$posDir = Join-Path $repoRoot "apps\pos"

$restaurantId = "11111111-1111-1111-1111-111111111111"
$deviceId = "01a0b072-5a20-7a10-add0-4f890c477588"
$agentKey = "restaurant-pos-agent-local-dev-key-2026"
$jwtKey = "restaurant-pos-development-jwt-key-change-me-2026-123456789"
$dbConnection = "Host=localhost;Port=5432;Database=restaurant_local;Username=restaurant;Password=restaurant_dev_password"

function Ensure-Directory([string]$path) {
    if (-not (Test-Path $path)) {
        New-Item -ItemType Directory -Path $path -Force | Out-Null
    }
}

function Write-State([string]$name, [bool]$running, [string]$detail = "") {
    $label = if ($running) { "RUNNING" } else { "STOPPED" }
    $color = if ($running) { "Green" } else { "Yellow" }
    $suffix = if ([string]::IsNullOrWhiteSpace($detail)) { "" } else { "  $detail" }

    Write-Host ("{0,-18} " -f $name) -NoNewline
    Write-Host $label -ForegroundColor $color -NoNewline
    Write-Host $suffix
}

function Test-Command([string]$name) {
    return $null -ne (Get-Command $name -ErrorAction SilentlyContinue)
}

function Wait-Http([string]$url, [int]$seconds) {
    $deadline = (Get-Date).AddSeconds($seconds)
    while ((Get-Date) -lt $deadline) {
        try {
            $response = Invoke-WebRequest -Uri $url -UseBasicParsing -TimeoutSec 2
            if ($response.StatusCode -ge 200 -and $response.StatusCode -lt 500) {
                return $true
            }
        }
        catch {
        }
        Start-Sleep -Milliseconds 700
    }
    return $false
}

function Test-Http([string]$url) {
    try {
        $response = Invoke-WebRequest -Uri $url -UseBasicParsing -TimeoutSec 2
        return $response.StatusCode -ge 200 -and $response.StatusCode -lt 500
    }
    catch {
        return $false
    }
}

function Get-ListeningPid([int]$port) {
    try {
        $connection = Get-NetTCPConnection -State Listen -LocalPort $port -ErrorAction SilentlyContinue | Select-Object -First 1
        if ($null -ne $connection) {
            return [int]$connection.OwningProcess
        }
    }
    catch {
    }
    return $null
}

function Stop-ProcessTree([int]$processId) {
    if ($processId -le 0) { return }

    try {
        $children = Get-CimInstance Win32_Process -Filter "ParentProcessId=$processId" -ErrorAction SilentlyContinue
        foreach ($child in $children) {
            Stop-ProcessTree -processId ([int]$child.ProcessId)
        }
    }
    catch {
    }

    Stop-Process -Id $processId -Force -ErrorAction SilentlyContinue
}

function Stop-Port([int]$port) {
    $pidOnPort = Get-ListeningPid $port
    if ($null -ne $pidOnPort -and $pidOnPort -ne $PID) {
        Write-Host "Stopping old process on port $port (PID $pidOnPort)..."
        Stop-ProcessTree -processId $pidOnPort
        Start-Sleep -Milliseconds 500
    }
}

function Get-PidPath([string]$name) {
    return Join-Path $pidsDir "$name.pid"
}

function Save-Pid([string]$name, [int]$processId) {
    Set-Content -Path (Get-PidPath $name) -Value $processId -Encoding ASCII
}

function Stop-SavedProcess([string]$name) {
    $path = Get-PidPath $name
    if (-not (Test-Path $path)) { return }

    $raw = Get-Content $path -ErrorAction SilentlyContinue | Select-Object -First 1
    $processId = 0
    if ([int]::TryParse([string]$raw, [ref]$processId)) {
        Stop-ProcessTree -processId $processId
    }

    Remove-Item $path -Force -ErrorAction SilentlyContinue
}

function Start-LoggedProcess(
    [string]$name,
    [string]$filePath,
    [string[]]$argumentList,
    [string]$workingDirectory,
    [switch]$Visible
) {
    $stdout = Join-Path $logsDir "$name.log"
    $stderr = Join-Path $logsDir "$name.err.log"

    Remove-Item $stdout, $stderr -Force -ErrorAction SilentlyContinue

    $params = @{
        FilePath = $filePath
        ArgumentList = $argumentList
        WorkingDirectory = $workingDirectory
        PassThru = $true
        RedirectStandardOutput = $stdout
        RedirectStandardError = $stderr
    }

    if (-not $Visible) {
        $params.WindowStyle = "Hidden"
    }

    $process = Start-Process @params
    Save-Pid $name $process.Id
    return $process
}

function Ensure-Docker {
    if (-not (Test-Command "docker")) {
        throw "Docker CLI was not found. Install Docker Desktop first."
    }

    & docker info *> $null
    if ($LASTEXITCODE -eq 0) {
        return
    }

    $dockerDesktop = Join-Path $env:ProgramFiles "Docker\Docker\Docker Desktop.exe"
    if (-not (Test-Path $dockerDesktop)) {
        throw "Docker Desktop is not running and its executable was not found."
    }

    Write-Host "Starting Docker Desktop..."
    Start-Process -FilePath $dockerDesktop | Out-Null

    $deadline = (Get-Date).AddSeconds(90)
    while ((Get-Date) -lt $deadline) {
        Start-Sleep -Seconds 2
        & docker info *> $null
        if ($LASTEXITCODE -eq 0) {
            Write-Host "Docker Desktop is ready." -ForegroundColor Green
            return
        }
    }

    throw "Docker Desktop did not become ready within 90 seconds."
}

function Start-Postgres {
    Push-Location $repoRoot
    try {
        & docker compose up -d postgres
        if ($LASTEXITCODE -ne 0) {
            throw "Could not start PostgreSQL with docker compose."
        }
    }
    finally {
        Pop-Location
    }

    $deadline = (Get-Date).AddSeconds(60)
    while ((Get-Date) -lt $deadline) {
        & docker exec restaurant-platform-postgres pg_isready -U restaurant -d restaurant_local *> $null
        if ($LASTEXITCODE -eq 0) {
            Write-Host "PostgreSQL is ready." -ForegroundColor Green
            return
        }
        Start-Sleep -Seconds 1
    }

    throw "PostgreSQL did not become ready within 60 seconds."
}

function Ensure-PosProject {
    if (Test-Path (Join-Path $posDir "pubspec.yaml")) {
        return
    }

    Write-Host "Generated Flutter POS is missing. Creating apps\pos..."
    & powershell.exe -NoProfile -ExecutionPolicy Bypass -File (Join-Path $repoRoot "scripts\create-pos.ps1")
    if ($LASTEXITCODE -ne 0) {
        throw "Could not generate Flutter POS."
    }
}

function Show-Status {
    Write-Host ""
    Write-Host "Restaurant Platform status" -ForegroundColor Cyan
    Write-Host "--------------------------"

    $dockerRunning = $false
    if (Test-Command "docker") {
        & docker info *> $null
        $dockerRunning = $LASTEXITCODE -eq 0
    }
    Write-State "Docker Desktop" $dockerRunning

    $postgresRunning = $false
    if ($dockerRunning) {
        $postgresState = & docker inspect -f "{{.State.Running}}" restaurant-platform-postgres 2>$null
        $postgresRunning = $postgresState -eq "true"
    }
    Write-State "PostgreSQL" $postgresRunning "localhost:5432"

    Write-State "Restaurant Node" (Test-Http "http://127.0.0.1:8080/health") "http://127.0.0.1:8080"
    Write-State "POS Agent" (Test-Http "http://127.0.0.1:8791/health") "http://127.0.0.1:8791"
    Write-State "BackOffice" (Test-Http "http://127.0.0.1:5173") "http://127.0.0.1:5173"

    $posRunning = $null -ne (Get-Process "restaurant_pos" -ErrorAction SilentlyContinue | Select-Object -First 1)
    Write-State "Flutter POS" $posRunning

    Write-Host ""
    Write-Host "Logs: $logsDir"
}

function Start-Platform {
    Ensure-Directory $logsDir
    Ensure-Directory $pidsDir

    foreach ($command in @("dotnet", "npm.cmd", "flutter")) {
        if (-not (Test-Command $command)) {
            throw "Required command '$command' was not found in PATH."
        }
    }

    Ensure-Docker
    Start-Postgres
    Ensure-PosProject

    Stop-Port 5173
    Stop-Port 8791
    Stop-Port 8080
    Stop-SavedProcess "flutter-pos"
    Get-Process "restaurant_pos" -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue

    Write-Host "Starting Restaurant Node..."
    $env:ConnectionStrings__RestaurantDb = $dbConnection
    $env:Jwt__Key = $jwtKey
    $env:Seed__AdminPin = "1234"
    $env:Agent__SharedKey = $agentKey
    $env:ASPNETCORE_URLS = "http://0.0.0.0:8080"
    Start-LoggedProcess -name "restaurant-node" -filePath "dotnet" -argumentList @("run", "--no-launch-profile") -workingDirectory $nodeDir | Out-Null

    if (-not (Wait-Http "http://127.0.0.1:8080/health" 45)) {
        throw "Restaurant Node did not start. Check .devlogs\restaurant-node.err.log"
    }
    Write-Host "Restaurant Node is ready." -ForegroundColor Green

    Write-Host "Starting POS Agent..."
    $env:RestaurantNode__BaseUrl = "http://127.0.0.1:8080"
    $env:Restaurant__Id = $restaurantId
    $env:Device__Id = $deviceId
    $env:Agent__SharedKey = $agentKey
    $env:ASPNETCORE_URLS = "http://127.0.0.1:8791"
    Start-LoggedProcess -name "pos-agent" -filePath "dotnet" -argumentList @("run", "--no-launch-profile") -workingDirectory $agentDir | Out-Null

    if (-not (Wait-Http "http://127.0.0.1:8791/health" 45)) {
        throw "POS Agent did not start. Check .devlogs\pos-agent.err.log"
    }
    Write-Host "POS Agent is ready." -ForegroundColor Green

    if (-not (Test-Path (Join-Path $backofficeDir "node_modules"))) {
        Write-Host "Installing BackOffice dependencies (first run only)..."
        Push-Location $backofficeDir
        try {
            & npm.cmd install
            if ($LASTEXITCODE -ne 0) {
                throw "npm install failed for BackOffice."
            }
        }
        finally {
            Pop-Location
        }
    }

    Write-Host "Starting BackOffice..."
    Start-LoggedProcess -name "backoffice" -filePath "cmd.exe" -argumentList @("/d", "/c", "npm.cmd run dev -- --host 127.0.0.1") -workingDirectory $backofficeDir | Out-Null

    if (-not (Wait-Http "http://127.0.0.1:5173" 45)) {
        throw "BackOffice did not start. Check .devlogs\backoffice.err.log"
    }
    Write-Host "BackOffice is ready." -ForegroundColor Green

    Write-Host "Starting Flutter POS..."
    $flutterCommand = "flutter run -d windows --dart-define=API_BASE_URL=http://127.0.0.1:8080 --dart-define=POS_AGENT_BASE_URL=http://127.0.0.1:8791 --dart-define=POS_DEVICE_ID=$deviceId"
    Start-LoggedProcess -name "flutter-pos" -filePath "cmd.exe" -argumentList @("/d", "/c", $flutterCommand) -workingDirectory $posDir | Out-Null

    Start-Sleep -Seconds 2
    Start-Process "http://127.0.0.1:5173" | Out-Null

    Show-Status

    Write-Host ""
    Write-Host "Platform started. You can close this window." -ForegroundColor Green
    Write-Host "PIN: 1234"
}

function Stop-Platform {
    Ensure-Directory $pidsDir

    Write-Host "Stopping Restaurant Platform..."

    Stop-SavedProcess "flutter-pos"
    Get-Process "restaurant_pos" -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue

    Stop-SavedProcess "backoffice"
    Stop-SavedProcess "pos-agent"
    Stop-SavedProcess "restaurant-node"

    Stop-Port 5173
    Stop-Port 8791
    Stop-Port 8080

    if (Test-Command "docker") {
        & docker info *> $null
        if ($LASTEXITCODE -eq 0) {
            Push-Location $repoRoot
            try {
                & docker compose stop postgres | Out-Host
            }
            finally {
                Pop-Location
            }
        }
    }

    Write-Host "Restaurant Platform stopped." -ForegroundColor Green
}

try {
    switch ($Action) {
        "Start" { Start-Platform }
        "Stop" { Stop-Platform }
        "Status" { Show-Status }
    }
}
catch {
    Write-Host ""
    Write-Host ("ERROR: " + $_.Exception.Message) -ForegroundColor Red
    Write-Host ("Logs: " + $logsDir)
    exit 1
}
