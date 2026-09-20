$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
$template = Join-Path $repoRoot "apps\pos-template"
$target = Join-Path $repoRoot "apps\pos"

if (-not (Get-Command flutter -ErrorAction SilentlyContinue)) {
    throw "Flutter SDK was not found in PATH. Install Flutter first, then run this script again."
}

if (Test-Path $target) {
    Write-Host "apps/pos already exists. Removing it before regeneration..."
    Remove-Item -Recurse -Force $target
}

Write-Host "Creating project apps\pos..."
flutter create --org com.restaurantplatform --project-name restaurant_pos --platforms=windows,android,ios $target
if ($LASTEXITCODE -ne 0) {
    throw "flutter create failed."
}

Copy-Item (Join-Path $template "pubspec.yaml") (Join-Path $target "pubspec.yaml") -Force
Remove-Item -Recurse -Force (Join-Path $target "lib")
Copy-Item (Join-Path $template "lib") (Join-Path $target "lib") -Recurse -Force

if (Test-Path (Join-Path $template ".template-version")) {
    Copy-Item (Join-Path $template ".template-version") (Join-Path $target ".template-version") -Force
}

Push-Location $target
try {
    flutter pub get
    if ($LASTEXITCODE -ne 0) {
        throw "flutter pub get failed."
    }

    dart format lib
    if ($LASTEXITCODE -ne 0) {
        throw "dart format failed."
    }
}
finally {
    Pop-Location
}

Write-Host "POS created at apps/pos"
Write-Host "Windows example: flutter run -d windows --dart-define=API_BASE_URL=http://127.0.0.1:8080 --dart-define=POS_AGENT_BASE_URL=http://127.0.0.1:8791 --dart-define=POS_DEVICE_ID=01a0b072-5a20-7a10-add0-4f890c477588"
