$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
$project = Join-Path $repoRoot "services\restaurant-node\src\RestaurantNode.Api"
$migrations = Join-Path $project "Infrastructure\Migrations"

if (Test-Path $migrations) {
    throw "Infrastructure/Migrations already exists. Refusing to create a duplicate initial migration."
}

Push-Location $repoRoot
try {
    dotnet tool restore
}
finally {
    Pop-Location
}

if (-not $env:ConnectionStrings__RestaurantDb) {
    $env:ConnectionStrings__RestaurantDb = "Host=localhost;Port=5432;Database=restaurant_local;Username=restaurant;Password=restaurant_dev_password"
}
if (-not $env:Jwt__Key) {
    $env:Jwt__Key = "restaurant-pos-development-jwt-key-change-me-2026-123456789"
}
if (-not $env:Seed__AdminPin) {
    $env:Seed__AdminPin = "1234"
}

Push-Location $project
try {
    dotnet ef migrations add InitialFoundation --output-dir Infrastructure/Migrations
}
finally {
    Pop-Location
}

Write-Host "Initial migration created in services/restaurant-node/src/RestaurantNode.Api/Infrastructure/Migrations"
Write-Host "Review it, then commit the generated migration files before switching runtime startup to Database.Migrate()."
