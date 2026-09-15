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

flutter create --org com.restaurantplatform --project-name restaurant_pos --platforms=windows,android,ios $target

Copy-Item (Join-Path $template "pubspec.yaml") (Join-Path $target "pubspec.yaml") -Force
Remove-Item -Recurse -Force (Join-Path $target "lib")
Copy-Item (Join-Path $template "lib") (Join-Path $target "lib") -Recurse -Force

Push-Location $target
flutter pub get
Pop-Location

Write-Host "POS created at apps/pos"
Write-Host "Windows example: flutter run -d windows --dart-define=API_BASE_URL=http://127.0.0.1:8080"
