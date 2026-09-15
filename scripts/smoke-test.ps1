param(
    [string]$ApiBaseUrl = "http://127.0.0.1:8080",
    [string]$RestaurantId = "11111111-1111-1111-1111-111111111111",
    [string]$Pin = "1234"
)

$ErrorActionPreference = "Stop"

Write-Host "1/5 Health"
Invoke-RestMethod "$ApiBaseUrl/health" | ConvertTo-Json

Write-Host "2/5 PIN login"
$auth = Invoke-RestMethod -Method Post -Uri "$ApiBaseUrl/api/v1/auth/pin" -ContentType "application/json" -Body (@{
    restaurantId = $RestaurantId
    pin = $Pin
} | ConvertTo-Json)
$headers = @{ Authorization = "Bearer $($auth.token)" }
Write-Host "Logged in as $($auth.employeeName)"

Write-Host "3/5 Menu"
$menu = Invoke-RestMethod -Headers $headers -Uri "$ApiBaseUrl/api/v1/menu"
$product = $menu.categories | ForEach-Object { $_.products } | Select-Object -First 1
if (-not $product) { throw "Seed menu has no products." }
Write-Host "Using product: $($product.name) / $($product.price) $($product.currencyCode)"

Write-Host "4/5 Create order"
$order = Invoke-RestMethod -Method Post -Headers $headers -Uri "$ApiBaseUrl/api/v1/orders" -ContentType "application/json" -Body '{"guestCount":1}'
Write-Host "Created order #$($order.displayNumber) ($($order.id))"

Write-Host "5/5 Add product"
$order = Invoke-RestMethod -Method Post -Headers $headers -Uri "$ApiBaseUrl/api/v1/orders/$($order.id)/items" -ContentType "application/json" -Body (@{
    productId = $product.id
    quantity = 1
} | ConvertTo-Json)
$order | ConvertTo-Json -Depth 10

Write-Host "SMOKE TEST PASSED"
