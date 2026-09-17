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

# Keep this script ASCII-only so Windows PowerShell 5.1 can parse it reliably.
# The Dart source itself is read and written explicitly as UTF-8.
$mainFile = Join-Path $target "lib\main.dart"
$utf8NoBom = New-Object System.Text.UTF8Encoding($false)
$mainText = [System.IO.File]::ReadAllText($mainFile, [System.Text.Encoding]::UTF8)
$mainText = $mainText -replace "`r`n", "`n"

function Replace-Required([string]$text, [string]$oldValue, [string]$newValue, [string]$description) {
    if (-not $text.Contains($oldValue)) {
        throw "Could not patch generated POS: $description anchor was not found."
    }
    return $text.Replace($oldValue, $newValue)
}

# Flutter setState callbacks must not return a Future.
$mainText = Replace-Required $mainText `
    'setState(() => hallsFuture = widget.api.getHalls());' `
    "setState(() {`n      hallsFuture = widget.api.getHalls();`n    });" `
    'hall refresh'

$mainText = Replace-Required $mainText `
    'onRetry: () => setState(() => menuFuture = widget.api.getMenu()),' `
    "onRetry: () {`n                setState(() {`n                  menuFuture = widget.api.getMenu();`n                });`n              }," `
    'menu retry'

# Add send-to-kitchen method to OrderPage. Unicode UI text is written as Dart escapes.
$orderBuildAnchor = @(
    '  @override',
    '  Widget build(BuildContext context) {',
    '    return Scaffold(',
    '      appBar: AppBar(',
    '        title: Text(',
    '          order == null'
) -join "`n"

$sendMethodAndAnchor = @(
    '  Future<void> sendToKitchen() async {',
    '    if (mutating || order == null || !order!.hasNewItems) return;',
    '    setState(() {',
    '      mutating = true;',
    '      error = null;',
    '    });',
    '    try {',
    '      final updated = await widget.api.sendOrderToKitchen(order!.id);',
    '      if (mounted) {',
    '        setState(() => order = updated);',
    '        ScaffoldMessenger.of(context).showSnackBar(',
    "          const SnackBar(content: Text('\u0417\u0430\u043A\u0430\u0437 \u043E\u0442\u043F\u0440\u0430\u0432\u043B\u0435\u043D \u043D\u0430 \u043A\u0443\u0445\u043D\u044E')),",
    '        );',
    '      }',
    '    } catch (e) {',
    '      if (mounted) setState(() => error = e.toString());',
    '    } finally {',
    '      if (mounted) setState(() => mutating = false);',
    '    }',
    '  }',
    '',
    '  @override',
    '  Widget build(BuildContext context) {',
    '    return Scaffold(',
    '      appBar: AppBar(',
    '        title: Text(',
    '          order == null'
) -join "`n"

$mainText = Replace-Required $mainText $orderBuildAnchor $sendMethodAndAnchor 'send method'

$mainText = Replace-Required $mainText `
    '                onMinus: decrementGroup,' `
    "                onMinus: decrementGroup,`n                onSend: sendToKitchen," `
    'order pane callback'

$mainText = Replace-Required $mainText `
    '    required this.onMinus,' `
    "    required this.onMinus,`n    required this.onSend," `
    'order pane constructor'

$mainText = Replace-Required $mainText `
    '  final ValueChanged<CartGroup> onMinus;' `
    "  final ValueChanged<CartGroup> onMinus;`n  final Future<void> Function() onSend;" `
    'order pane field'

# Insert the kitchen button immediately before the existing payment button.
$paymentAnchor = @(
    '            FilledButton.icon(',
    '              onPressed: null,',
    '              icon: const Icon(Icons.payments_outlined),'
) -join "`n"

$kitchenAndPaymentAnchor = @(
    '            FilledButton.icon(',
    '              onPressed: busy || order == null || !order!.hasNewItems',
    '                  ? null',
    '                  : () async => onSend(),',
    '              icon: const Icon(Icons.soup_kitchen_outlined),',
    '              label: const Padding(',
    '                padding: EdgeInsets.symmetric(vertical: 14),',
    "                child: Text('\u041E\u0442\u043F\u0440\u0430\u0432\u0438\u0442\u044C \u043D\u0430 \u043A\u0443\u0445\u043D\u044E'),",
    '              ),',
    '            ),',
    '            const SizedBox(height: 10),',
    '            FilledButton.icon(',
    '              onPressed: null,',
    '              icon: const Icon(Icons.payments_outlined),'
) -join "`n"

$mainText = Replace-Required $mainText $paymentAnchor $kitchenAndPaymentAnchor 'kitchen button'

# Add a compact NEW/SENT indicator without widening the cart too much.
$minusAnchor = @(
    '                              IconButton.filledTonal(',
    '                                onPressed: busy || !canRemove ? null : () => onMinus(group),'
) -join "`n"

$statusAndMinusAnchor = @(
    '                              Tooltip(',
    "                                message: group.status == 'SENT'",
    "                                    ? '\u041E\u0442\u043F\u0440\u0430\u0432\u043B\u0435\u043D\u043E \u043D\u0430 \u043A\u0443\u0445\u043D\u044E'",
    "                                    : '\u041D\u0435 \u043E\u0442\u043F\u0440\u0430\u0432\u043B\u0435\u043D\u043E',",
    '                                child: Icon(',
    "                                  group.status == 'SENT'",
    '                                      ? Icons.check_circle_outline',
    '                                      : Icons.schedule_send_outlined,',
    '                                  size: 18,',
    '                                ),',
    '                              ),',
    '                              const SizedBox(width: 4),',
    '                              IconButton.filledTonal(',
    '                                onPressed: busy || !canRemove ? null : () => onMinus(group),'
) -join "`n"

$mainText = Replace-Required $mainText $minusAnchor $statusAndMinusAnchor 'item status indicator'

[System.IO.File]::WriteAllText($mainFile, $mainText, $utf8NoBom)

Push-Location $target
flutter pub get
dart format lib
Pop-Location

Write-Host "POS created at apps/pos"
Write-Host "Windows example: flutter run -d windows --dart-define=API_BASE_URL=http://127.0.0.1:8080"
