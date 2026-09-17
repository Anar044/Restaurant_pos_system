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

# Apply small generated-client patches until they are folded into the template source.
# Explicit UTF-8 handling is required because Windows PowerShell 5.1 otherwise
# corrupts Cyrillic UI strings.
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

$mainText = Replace-Required $mainText `
    'setState(() => hallsFuture = widget.api.getHalls());' `
    "setState(() {`n      hallsFuture = widget.api.getHalls();`n    });" `
    'hall refresh'

$mainText = Replace-Required $mainText `
    'onRetry: () => setState(() => menuFuture = widget.api.getMenu()),' `
    "onRetry: () {`n                setState(() {`n                  menuFuture = widget.api.getMenu();`n                });`n              }," `
    'menu retry'

# Add send-to-kitchen action to the order screen.
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
    "          const SnackBar(content: Text('Заказ отправлен на кухню')),",
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

$mainText = Replace-Required $mainText $orderBuildAnchor $sendMethodAndAnchor 'send-to-kitchen method'

$mainText = Replace-Required $mainText `
    '                onMinus: decrementGroup,' `
    "                onMinus: decrementGroup,`n                onSend: sendToKitchen," `
    'order pane send callback'

$mainText = Replace-Required $mainText `
    '    required this.onMinus,' `
    "    required this.onMinus,`n    required this.onSend," `
    'order pane constructor'

$mainText = Replace-Required $mainText `
    '  final ValueChanged<CartGroup> onMinus;' `
    "  final ValueChanged<CartGroup> onMinus;`n  final Future<void> Function() onSend;" `
    'order pane send field'

$paymentBlock = @(
    '            FilledButton.icon(',
    '              onPressed: null,',
    '              icon: const Icon(Icons.payments_outlined),',
    '              label: const Padding(',
    '                padding: EdgeInsets.symmetric(vertical: 14),',
    "                child: Text('Оплата — следующий этап'),",
    '              ),',
    '            ),'
) -join "`n"

$kitchenAndPaymentBlock = @(
    '            FilledButton.icon(',
    '              onPressed: busy || order == null || !order!.hasNewItems',
    '                  ? null',
    '                  : () async => onSend(),',
    '              icon: const Icon(Icons.soup_kitchen_outlined),',
    '              label: const Padding(',
    '                padding: EdgeInsets.symmetric(vertical: 14),',
    "                child: Text('Отправить на кухню'),",
    '              ),',
    '            ),',
    '            const SizedBox(height: 10),',
    '            FilledButton.icon(',
    '              onPressed: null,',
    '              icon: const Icon(Icons.payments_outlined),',
    '              label: const Padding(',
    '                padding: EdgeInsets.symmetric(vertical: 14),',
    "                child: Text('Оплата — следующий этап'),",
    '              ),',
    '            ),'
) -join "`n"

$mainText = Replace-Required $mainText $paymentBlock $kitchenAndPaymentBlock 'kitchen button'

# Show the item state clearly in the cart. NEW lines can still be changed; SENT lines cannot.
$equationBlock = @(
    '                                    Text(',
    '                                      ''${group.quantity.g} × ${group.unitPrice.toStringAsFixed(2)} = ${group.total.toStringAsFixed(2)}'',' ,',
    '                                    ),'
) -join "`n"
$equationBlock = $equationBlock.Replace("'',", "',").Replace("' ,", "'")

$statusBlock = @(
    '                                    Text(',
    '                                      ''${group.quantity.g} × ${group.unitPrice.toStringAsFixed(2)} = ${group.total.toStringAsFixed(2)}'',' ,',
    '                                    ),',
    '                                    const SizedBox(height: 3),',
    '                                    Text(',
    "                                      group.status == 'SENT' ? 'Отправлено на кухню' : 'Не отправлено',",
    '                                      style: TextStyle(',
    "                                        color: group.status == 'SENT'",
    '                                            ? Theme.of(context).colorScheme.primary',
    '                                            : Theme.of(context).colorScheme.tertiary,',
    '                                        fontSize: 12,',
    '                                        fontWeight: FontWeight.w600,',
    '                                      ),',
    '                                    ),'
) -join "`n"
$statusBlock = $statusBlock.Replace("'',", "',").Replace("' ,", "'")

$mainText = Replace-Required $mainText $equationBlock $statusBlock 'cart item kitchen status'

[System.IO.File]::WriteAllText($mainFile, $mainText, $utf8NoBom)

Push-Location $target
flutter pub get
dart format lib
Pop-Location

Write-Host "POS created at apps/pos"
Write-Host "Windows example: flutter run -d windows --dart-define=API_BASE_URL=http://127.0.0.1:8080"
