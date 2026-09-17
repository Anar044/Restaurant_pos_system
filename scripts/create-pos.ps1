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

# Flutter setState callbacks must not return a Future. Patch the generated copy
# until these two callbacks are folded into the template source itself.
# Read and write explicitly as UTF-8 so Windows PowerShell 5.1 does not corrupt
# Cyrillic UI strings while applying the patch.
$mainFile = Join-Path $target "lib\main.dart"
$utf8NoBom = New-Object System.Text.UTF8Encoding($false)
$mainText = [System.IO.File]::ReadAllText($mainFile, [System.Text.Encoding]::UTF8)
$mainText = $mainText.Replace(
    'setState(() => hallsFuture = widget.api.getHalls());',
    "setState(() {`r`n      hallsFuture = widget.api.getHalls();`r`n    });"
)
$mainText = $mainText.Replace(
    'onRetry: () => setState(() => menuFuture = widget.api.getMenu()),',
    "onRetry: () {`r`n                setState(() {`r`n                  menuFuture = widget.api.getMenu();`r`n                });`r`n              },"
)
[System.IO.File]::WriteAllText($mainFile, $mainText, $utf8NoBom)

Push-Location $target
flutter pub get
dart format lib
Pop-Location

Write-Host "POS created at apps/pos"
Write-Host "Windows example: flutter run -d windows --dart-define=API_BASE_URL=http://127.0.0.1:8080"
