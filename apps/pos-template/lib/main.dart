import 'package:flutter/material.dart';

import 'api_client.dart';
import 'config.dart';
import 'local_print_client.dart';

void main() {
  runApp(const RestaurantPosApp());
}

class RestaurantPosApp extends StatefulWidget {
  const RestaurantPosApp({super.key});

  @override
  State<RestaurantPosApp> createState() => _RestaurantPosAppState();
}

class _RestaurantPosAppState extends State<RestaurantPosApp> {
  late final PosApiClient api = PosApiClient(AppConfig.apiBaseUrl);
  late final PosAgentClient printer = PosAgentClient(AppConfig.posAgentBaseUrl);
  AuthSession? session;

  @override
  Widget build(BuildContext context) {
    return MaterialApp(
      debugShowCheckedModeBanner: false,
      title: 'Restaurant POS',
      theme: ThemeData(
        colorScheme: ColorScheme.fromSeed(
          seedColor: const Color(0xFF243447),
          brightness: Brightness.light,
        ),
        scaffoldBackgroundColor: const Color(0xFFF4F6FA),
        useMaterial3: true,
      ),
      home: session == null
          ? LoginPage(
              api: api,
              onLoggedIn: (value) => setState(() => session = value),
            )
          : ShiftGate(
              api: api,
              printer: printer,
              session: session!,
            ),
    );
  }
}

class LoginPage extends StatefulWidget {
  const LoginPage({super.key, required this.api, required this.onLoggedIn});

  final PosApiClient api;
  final ValueChanged<AuthSession> onLoggedIn;

  @override
  State<LoginPage> createState() => _LoginPageState();
}

class _LoginPageState extends State<LoginPage> {
  String pin = '';
  bool loading = false;
  String? error;

  Future<void> submit() async {
    if (pin.length < 4 || loading) return;
    setState(() {
      loading = true;
      error = null;
    });
    try {
      final session = await widget.api.loginWithPin(
        AppConfig.developmentRestaurantId,
        pin,
      );
      widget.onLoggedIn(session);
    } catch (e) {
      setState(() => error = e.toString());
    } finally {
      if (mounted) setState(() => loading = false);
    }
  }

  void digit(String value) {
    if (pin.length >= 8 || loading) return;
    setState(() => pin += value);
  }

  @override
  Widget build(BuildContext context) {
    return Scaffold(
      body: Center(
        child: ConstrainedBox(
          constraints: const BoxConstraints(maxWidth: 420),
          child: Card(
            margin: const EdgeInsets.all(24),
            child: Padding(
              padding: const EdgeInsets.all(28),
              child: Column(
                mainAxisSize: MainAxisSize.min,
                children: [
                  const Icon(Icons.point_of_sale_rounded, size: 64),
                  const SizedBox(height: 14),
                  Text(
                    'Restaurant POS',
                    style: Theme.of(context).textTheme.headlineMedium,
                  ),
                  const SizedBox(height: 6),
                  Text('Введите PIN', style: Theme.of(context).textTheme.titleMedium),
                  const SizedBox(height: 18),
                  SizedBox(
                    height: 48,
                    child: Center(
                      child: Text(
                        List.filled(pin.length, '•').join(' '),
                        style: Theme.of(context).textTheme.headlineLarge,
                      ),
                    ),
                  ),
                  if (error != null) ...[
                    const SizedBox(height: 8),
                    Text(
                      error!,
                      textAlign: TextAlign.center,
                      style: TextStyle(color: Theme.of(context).colorScheme.error),
                    ),
                  ],
                  const SizedBox(height: 18),
                  GridView.count(
                    shrinkWrap: true,
                    crossAxisCount: 3,
                    mainAxisSpacing: 10,
                    crossAxisSpacing: 10,
                    childAspectRatio: 1.7,
                    physics: const NeverScrollableScrollPhysics(),
                    children: [
                      for (final d in ['1', '2', '3', '4', '5', '6', '7', '8', '9'])
                        FilledButton.tonal(
                          onPressed: () => digit(d),
                          child: Text(d, style: const TextStyle(fontSize: 22)),
                        ),
                      OutlinedButton(
                        onPressed: () => setState(() => pin = ''),
                        child: const Icon(Icons.clear),
                      ),
                      FilledButton.tonal(
                        onPressed: () => digit('0'),
                        child: const Text('0', style: TextStyle(fontSize: 22)),
                      ),
                      FilledButton(
                        onPressed: loading ? null : submit,
                        child: loading
                            ? const SizedBox(
                                width: 22,
                                height: 22,
                                child: CircularProgressIndicator(strokeWidth: 2),
                              )
                            : const Icon(Icons.arrow_forward),
                      ),
                    ],
                  ),
                ],
              ),
            ),
          ),
        ),
      ),
    );
  }
}

class ShiftGate extends StatefulWidget {
  const ShiftGate({
    super.key,
    required this.api,
    required this.printer,
    required this.session,
  });

  final PosApiClient api;
  final PosAgentClient printer;
  final AuthSession session;

  @override
  State<ShiftGate> createState() => _ShiftGateState();
}

class _ShiftGateState extends State<ShiftGate> {
  ShiftDto? shift;
  bool loading = true;
  String? error;

  @override
  void initState() {
    super.initState();
    load();
  }

  Future<void> load() async {
    setState(() {
      loading = true;
      error = null;
    });
    try {
      final current = await widget.api.getCurrentShift(AppConfig.posDeviceId);
      if (mounted) setState(() => shift = current);
    } catch (e) {
      if (mounted) setState(() => error = e.toString());
    } finally {
      if (mounted) setState(() => loading = false);
    }
  }

  Future<void> openShift(double openingCash) async {
    setState(() {
      loading = true;
      error = null;
    });
    try {
      final opened = await widget.api.openShift(
        AppConfig.posDeviceId,
        openingCash: openingCash,
      );
      if (mounted) setState(() => shift = opened);
    } catch (e) {
      if (mounted) setState(() => error = e.toString());
    } finally {
      if (mounted) setState(() => loading = false);
    }
  }

  void shiftClosed() {
    setState(() => shift = null);
  }

  @override
  Widget build(BuildContext context) {
    if (loading) {
      return const Scaffold(
        body: Center(child: CircularProgressIndicator()),
      );
    }

    if (error != null) {
      return Scaffold(
        body: _ErrorPane(message: error!, onRetry: load),
      );
    }

    if (shift == null) {
      return OpenShiftPage(
        employeeName: widget.session.employeeName,
        onOpen: openShift,
      );
    }

    return HallSelectionPage(
      api: widget.api,
      printer: widget.printer,
      session: widget.session,
      shift: shift!,
      onShiftClosed: shiftClosed,
    );
  }
}

class OpenShiftPage extends StatefulWidget {
  const OpenShiftPage({
    super.key,
    required this.employeeName,
    required this.onOpen,
  });

  final String employeeName;
  final Future<void> Function(double openingCash) onOpen;

  @override
  State<OpenShiftPage> createState() => _OpenShiftPageState();
}

class _OpenShiftPageState extends State<OpenShiftPage> {
  final controller = TextEditingController(text: '0.00');
  bool opening = false;
  String? error;

  @override
  void dispose() {
    controller.dispose();
    super.dispose();
  }

  Future<void> submit() async {
    if (opening) return;
    final normalized = controller.text.trim().replaceAll(',', '.');
    final amount = double.tryParse(normalized);
    if (amount == null || amount < 0) {
      setState(() => error = 'Введите корректную сумму наличных в кассе.');
      return;
    }

    setState(() {
      opening = true;
      error = null;
    });
    try {
      await widget.onOpen(amount);
    } catch (e) {
      if (mounted) setState(() => error = e.toString());
    } finally {
      if (mounted) setState(() => opening = false);
    }
  }

  @override
  Widget build(BuildContext context) {
    return Scaffold(
      body: Center(
        child: ConstrainedBox(
          constraints: const BoxConstraints(maxWidth: 460),
          child: Card(
            margin: const EdgeInsets.all(24),
            child: Padding(
              padding: const EdgeInsets.all(28),
              child: Column(
                mainAxisSize: MainAxisSize.min,
                crossAxisAlignment: CrossAxisAlignment.stretch,
                children: [
                  const Icon(Icons.point_of_sale_rounded, size: 58),
                  const SizedBox(height: 12),
                  Text(
                    'Открытие смены',
                    textAlign: TextAlign.center,
                    style: Theme.of(context).textTheme.headlineSmall,
                  ),
                  const SizedBox(height: 6),
                  Text(
                    widget.employeeName,
                    textAlign: TextAlign.center,
                  ),
                  const SizedBox(height: 24),
                  TextField(
                    controller: controller,
                    keyboardType: const TextInputType.numberWithOptions(
                      decimal: true,
                    ),
                    decoration: const InputDecoration(
                      labelText: 'Наличные в кассе при открытии',
                      suffixText: 'AZN',
                      border: OutlineInputBorder(),
                    ),
                    onSubmitted: (_) => submit(),
                  ),
                  if (error != null) ...[
                    const SizedBox(height: 10),
                    Text(
                      error!,
                      style: TextStyle(
                        color: Theme.of(context).colorScheme.error,
                      ),
                    ),
                  ],
                  const SizedBox(height: 18),
                  FilledButton.icon(
                    onPressed: opening ? null : submit,
                    icon: const Icon(Icons.lock_open_outlined),
                    label: Padding(
                      padding: const EdgeInsets.symmetric(vertical: 14),
                      child: Text(
                        opening ? 'Открываем…' : 'Открыть смену',
                      ),
                    ),
                  ),
                ],
              ),
            ),
          ),
        ),
      ),
    );
  }
}

class HallSelectionPage extends StatefulWidget {
  const HallSelectionPage({
    super.key,
    required this.api,
    required this.printer,
    required this.session,
    required this.shift,
    required this.onShiftClosed,
  });

  final PosApiClient api;
  final PosAgentClient printer;
  final AuthSession session;
  final ShiftDto shift;
  final VoidCallback onShiftClosed;

  @override
  State<HallSelectionPage> createState() => _HallSelectionPageState();
}

class _HallSelectionPageState extends State<HallSelectionPage> {
  late Future<List<HallDto>> hallsFuture;
  String? selectedHallId;
  String? loadingTableId;

  @override
  void initState() {
    super.initState();
    hallsFuture = widget.api.getHalls();
  }

  void refresh() {
    setState(() => hallsFuture = widget.api.getHalls());
  }

  Future<void> openTable(HallDto hall, DiningTableDto table) async {
    if (loadingTableId != null) return;
    setState(() => loadingTableId = table.id);
    try {
      OrderDto? existingOrder;
      if (table.openOrder != null) {
        existingOrder = await widget.api.getOrder(table.openOrder!.id);
      }
      if (!mounted) return;
      await Navigator.of(context).push(
        MaterialPageRoute(
          builder: (_) => OrderPage(
            api: widget.api,
            printer: widget.printer,
            session: widget.session,
            shift: widget.shift,
            hallName: hall.name,
            table: table,
            initialOrder: existingOrder,
          ),
        ),
      );
      if (mounted) refresh();
    } catch (e) {
      if (!mounted) return;
      ScaffoldMessenger.of(context).showSnackBar(
        SnackBar(content: Text(e.toString())),
      );
    } finally {
      if (mounted) setState(() => loadingTableId = null);
    }
  }

  Future<void> showOrderHistory() async {
    try {
      final history = await widget.api.getOrderHistory(
        shiftId: widget.shift.id,
        take: 50,
      );
      if (!mounted) return;
      await showDialog<void>(
        context: context,
        builder: (_) => _OrderHistoryDialog(
          orders: history,
          onReprint: reprintHistoryOrder,
        ),
      );
    } catch (e) {
      if (!mounted) return;
      ScaffoldMessenger.of(context).showSnackBar(
        SnackBar(content: Text('История заказов: $e')),
      );
    }
  }

  Future<void> reprintHistoryOrder(OrderHistoryItemDto item) async {
    final paidOrder = item.order;
    final completedPayments = paidOrder.payments
        .where(
          (payment) =>
              payment.status == 'COMPLETED' || payment.status == 'REFUNDED',
        )
        .toList();

    if (completedPayments.isEmpty) {
      throw StateError('У заказа нет завершённой оплаты.');
    }

    final totals = <String, double>{};
    for (final payment in completedPayments) {
      totals.update(
        payment.method,
        (value) => value + payment.amount,
        ifAbsent: () => payment.amount,
      );
    }
    final paymentSummary = totals.entries
        .map((entry) => '${entry.key} ${entry.value.toStringAsFixed(2)}')
        .join(' + ');

    PaymentDto? cashPayment;
    for (final payment in completedPayments.reversed) {
      if (payment.method == 'CASH' && payment.tenderedAmount != null) {
        cashPayment = payment;
        break;
      }
    }

    final groups = CartGroup.fromOrder(paidOrder);
    final result = await widget.printer.printReceipt(
      restaurantName: AppConfig.restaurantDisplayName,
      orderNumber: paidOrder.displayNumber,
      hallName: item.hallName,
      tableName: item.tableName,
      cashierName: item.cashierName,
      guestCount: paidOrder.guestCount,
      currencyCode: AppConfig.currencyCode,
      total: paidOrder.total,
      paymentMethod: paymentSummary,
      paidAmount: paidOrder.paidTotal,
      cashReceived: cashPayment?.tenderedAmount,
      changeAmount:
          (cashPayment?.changeAmount ?? 0) > 0 ? cashPayment!.changeAmount : null,
      completedAt: completedPayments.last.createdAt,
      isCopy: true,
      items: groups
          .map(
            (group) => ReceiptPrintItem(
              name: group.productName,
              quantity: group.quantity,
              unitPrice: group.unitPrice,
              lineTotal: group.total,
            ),
          )
          .toList(),
    );

    if (!mounted) return;
    ScaffoldMessenger.of(context).showSnackBar(
      SnackBar(
        content: Text(
          'Копия чека #${result.orderNumber} отправлена на ${result.printerName}',
        ),
      ),
    );
  }

  Future<void> showXReport() async {
    try {
      final report = await widget.api.getShiftReport(widget.shift.id);
      if (!mounted) return;
      await showDialog<void>(
        context: context,
        builder: (_) => _ShiftReportDialog(
          title: 'X-отчёт',
          report: report,
        ),
      );
    } catch (e) {
      if (!mounted) return;
      ScaffoldMessenger.of(context).showSnackBar(
        SnackBar(content: Text('X-отчёт: $e')),
      );
    }
  }

  Future<void> addCashMovement(String type) async {
    final request = await showDialog<_CashMovementRequest>(
      context: context,
      builder: (_) => _CashMovementDialog(type: type),
    );
    if (request == null || !mounted) return;

    try {
      await widget.api.addCashTransaction(
        shiftId: widget.shift.id,
        type: type,
        amount: request.amount,
        reason: request.reason,
      );
      if (!mounted) return;
      ScaffoldMessenger.of(context).showSnackBar(
        SnackBar(
          content: Text(
            type == 'DEPOSIT'
                ? 'Внесение ${request.amount.toStringAsFixed(2)} AZN сохранено.'
                : 'Изъятие ${request.amount.toStringAsFixed(2)} AZN сохранено.',
          ),
        ),
      );
    } catch (e) {
      if (!mounted) return;
      ScaffoldMessenger.of(context).showSnackBar(
        SnackBar(content: Text('Операция с кассой: $e')),
      );
    }
  }

  Future<void> closeShift() async {
    final controller = TextEditingController(text: '0.00');
    final closingCash = await showDialog<double>(
      context: context,
      builder: (dialogContext) => AlertDialog(
        title: const Text('Закрытие смены'),
        content: TextField(
          controller: controller,
          autofocus: true,
          keyboardType: const TextInputType.numberWithOptions(decimal: true),
          decoration: const InputDecoration(
            labelText: 'Фактические наличные в кассе',
            suffixText: 'AZN',
            border: OutlineInputBorder(),
          ),
        ),
        actions: [
          TextButton(
            onPressed: () => Navigator.pop(dialogContext),
            child: const Text('Отмена'),
          ),
          FilledButton(
            onPressed: () {
              final value = double.tryParse(
                controller.text.trim().replaceAll(',', '.'),
              );
              if (value != null && value >= 0) {
                Navigator.pop(dialogContext, value);
              }
            },
            child: const Text('Закрыть смену'),
          ),
        ],
      ),
    );
    controller.dispose();

    if (closingCash == null || !mounted) return;

    try {
      final result = await widget.api.closeShift(
        widget.shift.id,
        closingCash: closingCash,
      );
      if (!mounted) return;

      if (result.report != null) {
        await showDialog<void>(
          context: context,
          barrierDismissible: false,
          builder: (_) => _ShiftReportDialog(
            title: 'Z-отчёт · смена закрыта',
            report: result.report!,
          ),
        );
      }

      if (!mounted) return;
      ScaffoldMessenger.of(context).showSnackBar(
        SnackBar(
          content: Text(
            'Смена закрыта. Ожидалось наличных: '
            '${result.expectedCash.toStringAsFixed(2)} AZN, '
            'разница: ${result.difference.toStringAsFixed(2)} AZN',
          ),
        ),
      );
      widget.onShiftClosed();
    } catch (e) {
      if (!mounted) return;
      ScaffoldMessenger.of(context).showSnackBar(
        SnackBar(content: Text('Закрытие смены: $e')),
      );
    }
  }

  @override
  Widget build(BuildContext context) {
    return Scaffold(
      appBar: AppBar(
        title: const Text('Залы и столы'),
        actions: [
          Center(
            child: Text(
              'Смена с ${TimeOfDay.fromDateTime(widget.shift.openedAt.toLocal()).format(context)}',
            ),
          ),
          const SizedBox(width: 8),
          TextButton.icon(
            onPressed: showOrderHistory,
            icon: const Icon(Icons.history),
            label: const Text('История'),
          ),
          TextButton.icon(
            onPressed: showXReport,
            icon: const Icon(Icons.summarize_outlined),
            label: const Text('X-отчёт'),
          ),
          PopupMenuButton<String>(
            tooltip: 'Операции с кассой',
            onSelected: (value) {
              if (value == 'DEPOSIT' || value == 'WITHDRAWAL') {
                addCashMovement(value);
              }
            },
            itemBuilder: (_) => const [
              PopupMenuItem(
                value: 'DEPOSIT',
                child: Text('Внесение наличных'),
              ),
              PopupMenuItem(
                value: 'WITHDRAWAL',
                child: Text('Изъятие наличных'),
              ),
            ],
            icon: const Icon(Icons.account_balance_wallet_outlined),
          ),
          TextButton.icon(
            onPressed: closeShift,
            icon: const Icon(Icons.lock_outline),
            label: const Text('Закрыть смену'),
          ),
          Padding(
            padding: const EdgeInsets.symmetric(horizontal: 20),
            child: Center(
              child: Text(
                '${widget.session.employeeName} · ${widget.session.roleName}',
              ),
            ),
          ),
        ],
      ),
      body: FutureBuilder<List<HallDto>>(
        future: hallsFuture,
        builder: (context, snapshot) {
          if (snapshot.connectionState != ConnectionState.done) {
            return const Center(child: CircularProgressIndicator());
          }
          if (snapshot.hasError) {
            return _ErrorPane(message: snapshot.error.toString(), onRetry: refresh);
          }

          final halls = snapshot.data ?? const <HallDto>[];
          if (halls.isEmpty) {
            return const Center(child: Text('Нет доступных залов'));
          }

          HallDto selectedHall = halls.first;
          for (final hall in halls) {
            if (hall.id == selectedHallId) {
              selectedHall = hall;
              break;
            }
          }

          return Padding(
            padding: const EdgeInsets.all(20),
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                Wrap(
                  spacing: 10,
                  runSpacing: 10,
                  children: [
                    for (final hall in halls)
                      ChoiceChip(
                        label: Text(hall.name),
                        selected: hall.id == selectedHall.id,
                        onSelected: (_) => setState(() => selectedHallId = hall.id),
                      ),
                  ],
                ),
                const SizedBox(height: 20),
                Row(
                  children: [
                    Text(
                      selectedHall.name,
                      style: Theme.of(context).textTheme.headlineSmall,
                    ),
                    const Spacer(),
                    const _LegendDot(label: 'Свободен', occupied: false),
                    const SizedBox(width: 18),
                    const _LegendDot(label: 'Занят', occupied: true),
                    const SizedBox(width: 8),
                    IconButton(onPressed: refresh, icon: const Icon(Icons.refresh)),
                  ],
                ),
                const SizedBox(height: 12),
                Expanded(
                  child: GridView.builder(
                    gridDelegate: const SliverGridDelegateWithMaxCrossAxisExtent(
                      maxCrossAxisExtent: 240,
                      childAspectRatio: 1.35,
                      crossAxisSpacing: 16,
                      mainAxisSpacing: 16,
                    ),
                    itemCount: selectedHall.tables.length,
                    itemBuilder: (context, index) {
                      final table = selectedHall.tables[index];
                      return _TableCard(
                        table: table,
                        loading: loadingTableId == table.id,
                        onTap: () => openTable(selectedHall, table),
                      );
                    },
                  ),
                ),
              ],
            ),
          );
        },
      ),
    );
  }
}

class _OrderHistoryDialog extends StatefulWidget {
  const _OrderHistoryDialog({
    required this.orders,
    required this.onReprint,
  });

  final List<OrderHistoryItemDto> orders;
  final Future<void> Function(OrderHistoryItemDto item) onReprint;

  @override
  State<_OrderHistoryDialog> createState() => _OrderHistoryDialogState();
}

class _OrderHistoryDialogState extends State<_OrderHistoryDialog> {
  String? printingId;
  String? error;

  Future<void> reprint(OrderHistoryItemDto item) async {
    if (printingId != null) return;
    setState(() {
      printingId = item.order.id;
      error = null;
    });
    try {
      await widget.onReprint(item);
    } catch (e) {
      if (mounted) setState(() => error = e.toString());
    } finally {
      if (mounted) setState(() => printingId = null);
    }
  }

  @override
  Widget build(BuildContext context) {
    return AlertDialog(
      title: const Text('История заказов этой смены'),
      content: SizedBox(
        width: 650,
        height: 480,
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.stretch,
          children: [
            if (error != null) ...[
              Text(
                error!,
                style: TextStyle(color: Theme.of(context).colorScheme.error),
              ),
              const SizedBox(height: 8),
            ],
            Expanded(
              child: widget.orders.isEmpty
                  ? const Center(child: Text('Закрытых заказов пока нет.'))
                  : ListView.separated(
                      itemCount: widget.orders.length,
                      separatorBuilder: (_, __) => const Divider(height: 1),
                      itemBuilder: (context, index) {
                        final item = widget.orders[index];
                        final order = item.order;
                        return ListTile(
                          leading: CircleAvatar(
                            child: Text('#${order.displayNumber}'),
                          ),
                          title: Text(
                            'Заказ #${order.displayNumber} · '
                            '${order.total.toStringAsFixed(2)} AZN',
                          ),
                          subtitle: Text(
                            [
                              if (item.hallName != null) item.hallName!,
                              if (item.tableName != null)
                                'Стол ${item.tableName}',
                              item.cashierName,
                              if (order.closedAt != null)
                                order.closedAt!
                                    .toLocal()
                                    .toString()
                                    .substring(0, 19),
                            ].join(' · '),
                          ),
                          trailing: FilledButton.tonalIcon(
                            onPressed: printingId == null
                                ? () => reprint(item)
                                : null,
                            icon: printingId == order.id
                                ? const SizedBox(
                                    width: 16,
                                    height: 16,
                                    child: CircularProgressIndicator(
                                      strokeWidth: 2,
                                    ),
                                  )
                                : const Icon(Icons.print_outlined),
                            label: const Text('Повторить чек'),
                          ),
                        );
                      },
                    ),
            ),
          ],
        ),
      ),
      actions: [
        FilledButton(
          onPressed: printingId == null
              ? () => Navigator.of(context).pop()
              : null,
          child: const Text('Закрыть'),
        ),
      ],
    );
  }
}

class _CashMovementRequest {
  const _CashMovementRequest({
    required this.amount,
    required this.reason,
  });

  final double amount;
  final String reason;
}

class _CashMovementDialog extends StatefulWidget {
  const _CashMovementDialog({required this.type});

  final String type;

  @override
  State<_CashMovementDialog> createState() => _CashMovementDialogState();
}

class _CashMovementDialogState extends State<_CashMovementDialog> {
  final amountController = TextEditingController();
  final reasonController = TextEditingController();
  String? error;

  @override
  void dispose() {
    amountController.dispose();
    reasonController.dispose();
    super.dispose();
  }

  void submit() {
    final amount = double.tryParse(
      amountController.text.trim().replaceAll(',', '.'),
    );
    final reason = reasonController.text.trim();

    if (amount == null || amount <= 0) {
      setState(() => error = 'Введите корректную сумму.');
      return;
    }
    if (reason.isEmpty) {
      setState(() => error = 'Укажите причину.');
      return;
    }

    Navigator.of(context).pop(
      _CashMovementRequest(amount: amount, reason: reason),
    );
  }

  @override
  Widget build(BuildContext context) {
    final deposit = widget.type == 'DEPOSIT';
    return AlertDialog(
      title: Text(deposit ? 'Внесение наличных' : 'Изъятие наличных'),
      content: SizedBox(
        width: 380,
        child: Column(
          mainAxisSize: MainAxisSize.min,
          children: [
            TextField(
              controller: amountController,
              autofocus: true,
              keyboardType:
                  const TextInputType.numberWithOptions(decimal: true),
              decoration: const InputDecoration(
                labelText: 'Сумма',
                suffixText: 'AZN',
                border: OutlineInputBorder(),
              ),
            ),
            const SizedBox(height: 12),
            TextField(
              controller: reasonController,
              maxLength: 500,
              decoration: const InputDecoration(
                labelText: 'Причина',
                border: OutlineInputBorder(),
              ),
            ),
            if (error != null)
              Align(
                alignment: Alignment.centerLeft,
                child: Text(
                  error!,
                  style: TextStyle(
                    color: Theme.of(context).colorScheme.error,
                  ),
                ),
              ),
          ],
        ),
      ),
      actions: [
        TextButton(
          onPressed: () => Navigator.of(context).pop(),
          child: const Text('Отмена'),
        ),
        FilledButton(
          onPressed: submit,
          child: Text(deposit ? 'Внести' : 'Изъять'),
        ),
      ],
    );
  }
}

class _ShiftReportDialog extends StatelessWidget {
  const _ShiftReportDialog({
    required this.title,
    required this.report,
  });

  final String title;
  final ShiftReportDto report;

  String _methodName(String method) {
    switch (method) {
      case 'CASH':
        return 'Наличные';
      case 'CARD':
        return 'Карта';
      default:
        return 'Другое';
    }
  }

  @override
  Widget build(BuildContext context) {
    return AlertDialog(
      title: Text(title),
      content: SizedBox(
        width: 520,
        child: SingleChildScrollView(
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.stretch,
            children: [
              _ReportRow(
                label: 'Открытие',
                value:
                    '${report.openedAt.toLocal().toString().substring(0, 19)}',
              ),
              _ReportRow(
                label: 'Заказов',
                value: report.ordersCount.toString(),
              ),
              _ReportRow(
                label: 'Оплат',
                value: report.paymentsCount.toString(),
              ),
              const Divider(height: 24),
              for (final payment in report.payments)
                _ReportRow(
                  label: _methodName(payment.method),
                  value:
                      '${payment.net.toStringAsFixed(2)} AZN '
                      '(продажи ${payment.gross.toStringAsFixed(2)}, '
                      'возвраты ${payment.refunds.toStringAsFixed(2)})',
                ),
              const Divider(height: 24),
              _ReportRow(
                label: 'Продажи',
                value: '${report.grossSales.toStringAsFixed(2)} AZN',
              ),
              _ReportRow(
                label: 'Возвраты',
                value: '${report.refunds.toStringAsFixed(2)} AZN',
              ),
              _ReportRow(
                label: 'Нетто',
                value: '${report.netSales.toStringAsFixed(2)} AZN',
                strong: true,
              ),
              const Divider(height: 24),
              _ReportRow(
                label: 'Наличные при открытии',
                value: '${report.openingCash.toStringAsFixed(2)} AZN',
              ),
              _ReportRow(
                label: 'Внесения',
                value: '${report.deposits.toStringAsFixed(2)} AZN',
              ),
              _ReportRow(
                label: 'Изъятия',
                value: '${report.withdrawals.toStringAsFixed(2)} AZN',
              ),
              _ReportRow(
                label: 'Ожидается в кассе',
                value: '${report.expectedCash.toStringAsFixed(2)} AZN',
                strong: true,
              ),
              if (report.closingCash != null)
                _ReportRow(
                  label: 'Фактически в кассе',
                  value: '${report.closingCash!.toStringAsFixed(2)} AZN',
                ),
              if (report.cashDifference != null)
                _ReportRow(
                  label: 'Разница',
                  value: '${report.cashDifference!.toStringAsFixed(2)} AZN',
                  strong: true,
                ),
            ],
          ),
        ),
      ),
      actions: [
        FilledButton(
          onPressed: () => Navigator.of(context).pop(),
          child: const Text('Закрыть'),
        ),
      ],
    );
  }
}

class _ReportRow extends StatelessWidget {
  const _ReportRow({
    required this.label,
    required this.value,
    this.strong = false,
  });

  final String label;
  final String value;
  final bool strong;

  @override
  Widget build(BuildContext context) {
    final style = strong
        ? const TextStyle(fontWeight: FontWeight.w800)
        : const TextStyle();
    return Padding(
      padding: const EdgeInsets.symmetric(vertical: 5),
      child: Row(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Expanded(child: Text(label, style: style)),
          const SizedBox(width: 18),
          Flexible(
            child: Text(
              value,
              textAlign: TextAlign.right,
              style: style,
            ),
          ),
        ],
      ),
    );
  }
}

class _LegendDot extends StatelessWidget {
  const _LegendDot({required this.label, required this.occupied});

  final String label;
  final bool occupied;

  @override
  Widget build(BuildContext context) {
    final color = occupied
        ? Theme.of(context).colorScheme.primary
        : Theme.of(context).colorScheme.outlineVariant;
    return Row(
      mainAxisSize: MainAxisSize.min,
      children: [
        Container(
          width: 10,
          height: 10,
          decoration: BoxDecoration(color: color, shape: BoxShape.circle),
        ),
        const SizedBox(width: 6),
        Text(label),
      ],
    );
  }
}

class _TableCard extends StatelessWidget {
  const _TableCard({required this.table, required this.loading, required this.onTap});

  final DiningTableDto table;
  final bool loading;
  final VoidCallback onTap;

  @override
  Widget build(BuildContext context) {
    final occupied = table.occupied;
    final scheme = Theme.of(context).colorScheme;
    return Card(
      elevation: occupied ? 2 : 0,
      color: occupied ? scheme.primaryContainer : scheme.surface,
      child: InkWell(
        borderRadius: BorderRadius.circular(12),
        onTap: loading ? null : onTap,
        child: Padding(
          padding: const EdgeInsets.all(18),
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              Row(
                children: [
                  Icon(occupied ? Icons.table_restaurant : Icons.event_seat_outlined),
                  const Spacer(),
                  if (loading)
                    const SizedBox(
                      width: 18,
                      height: 18,
                      child: CircularProgressIndicator(strokeWidth: 2),
                    ),
                ],
              ),
              const Spacer(),
              Text('Стол ${table.name}', style: Theme.of(context).textTheme.titleLarge),
              Text('${table.seats} мест'),
              const SizedBox(height: 8),
              if (table.openOrder != null)
                Text(
                  'Заказ #${table.openOrder!.displayNumber} · ${table.openOrder!.total.toStringAsFixed(2)} AZN',
                  style: const TextStyle(fontWeight: FontWeight.w600),
                )
              else
                const Text('Свободен'),
            ],
          ),
        ),
      ),
    );
  }
}

class OrderPage extends StatefulWidget {
  const OrderPage({
    super.key,
    required this.api,
    required this.printer,
    required this.session,
    required this.shift,
    required this.hallName,
    required this.table,
    this.initialOrder,
  });

  final PosApiClient api;
  final PosAgentClient printer;
  final AuthSession session;
  final ShiftDto shift;
  final String hallName;
  final DiningTableDto table;
  final OrderDto? initialOrder;

  @override
  State<OrderPage> createState() => _OrderPageState();
}

class _OrderPageState extends State<OrderPage> {
  late Future<List<MenuCategory>> menuFuture;
  late OrderDto? order;
  String? selectedCategoryId;
  bool mutating = false;
  bool printing = false;
  bool paying = false;
  String? error;

  @override
  void initState() {
    super.initState();
    order = widget.initialOrder;
    menuFuture = widget.api.getMenu();
  }

  Future<void> addProduct(MenuProduct product) async {
    if (mutating || printing || paying) return;
    setState(() {
      mutating = true;
      error = null;
    });
    try {
      var current = order ?? await widget.api.createOrder(tableId: widget.table.id);
      current = await widget.api.addItem(current.id, product.id);
      if (mounted) setState(() => order = current);
    } catch (e) {
      if (mounted) setState(() => error = e.toString());
    } finally {
      if (mounted) setState(() => mutating = false);
    }
  }

  Future<void> incrementGroup(CartGroup group) async {
    if (mutating || printing || paying || order == null) return;
    setState(() {
      mutating = true;
      error = null;
    });
    try {
      final updated = await widget.api.addItem(order!.id, group.productId);
      if (mounted) setState(() => order = updated);
    } catch (e) {
      if (mounted) setState(() => error = e.toString());
    } finally {
      if (mounted) setState(() => mutating = false);
    }
  }

  Future<void> decrementGroup(CartGroup group) async {
    if (mutating || printing || paying || order == null) return;
    OrderLineDto? removable;
    for (final line in group.lines.reversed) {
      if (line.status == 'NEW') {
        removable = line;
        break;
      }
    }
    if (removable == null) return;

    setState(() {
      mutating = true;
      error = null;
    });
    try {
      final updated = await widget.api.deleteItem(order!.id, removable.id);
      if (mounted) setState(() => order = updated);
    } catch (e) {
      if (mounted) setState(() => error = e.toString());
    } finally {
      if (mounted) setState(() => mutating = false);
    }
  }

  Future<void> printPrecheck() async {
    final current = order;
    if (current == null || current.items.isEmpty || mutating || printing) return;

    setState(() {
      printing = true;
      error = null;
    });

    try {
      final groups = CartGroup.fromOrder(current);
      final result = await widget.printer.printReceipt(
        restaurantName: AppConfig.restaurantDisplayName,
        orderNumber: current.displayNumber,
        hallName: widget.hallName,
        tableName: widget.table.name,
        cashierName: widget.session.employeeName,
        guestCount: current.guestCount,
        currencyCode: AppConfig.currencyCode,
        total: current.total,
        items: groups
            .map(
              (group) => ReceiptPrintItem(
                name: group.productName,
                quantity: group.quantity,
                unitPrice: group.unitPrice,
                lineTotal: group.total,
              ),
            )
            .toList(),
      );

      if (!mounted) return;
      ScaffoldMessenger.of(context).showSnackBar(
        SnackBar(
          content: Text(
            'Предчек #${result.orderNumber} отправлен на ${result.printerName}',
          ),
        ),
      );
    } catch (e) {
      if (mounted) setState(() => error = 'Печать: $e');
    } finally {
      if (mounted) setState(() => printing = false);
    }
  }

  Future<void> openPayment() async {
    final current = order;
    if (current == null ||
        current.items.isEmpty ||
        current.hasNewItems ||
        current.isPaid ||
        current.remaining <= 0 ||
        mutating ||
        printing ||
        paying) {
      return;
    }

    final request = await showDialog<_PaymentRequest>(
      context: context,
      builder: (dialogContext) => _PaymentDialog(
        orderNumber: current.displayNumber,
        total: current.total,
        paid: current.paidTotal,
        remaining: current.remaining,
      ),
    );

    if (request == null || !mounted) return;
    await pay(request);
  }

  Future<void> pay(_PaymentRequest request) async {
    final current = order;
    if (current == null || current.remaining <= 0 || paying) return;

    setState(() {
      paying = true;
      error = null;
    });

    try {
      final result = await widget.api.payOrder(
        orderId: current.id,
        shiftId: widget.shift.id,
        method: request.method,
        amount: request.amount,
        tenderedAmount: request.tenderedAmount,
      );

      if (!mounted) return;
      setState(() => order = result.order);

      if (result.order.status == 'PAID') {
        await printPaidReceiptAndClose(result.order, result.payment);
      } else {
        ScaffoldMessenger.of(context).showSnackBar(
          SnackBar(
            content: Text(
              'Оплата ${result.payment.amount.toStringAsFixed(2)} AZN сохранена. '
              'Осталось: ${result.remaining.toStringAsFixed(2)} AZN',
            ),
          ),
        );
      }
    } catch (e) {
      if (mounted) setState(() => error = 'Оплата: $e');
    } finally {
      if (mounted) setState(() => paying = false);
    }
  }

  String _paymentSummary(OrderDto paidOrder) {
    final totals = <String, double>{};
    for (final entry in paidOrder.payments) {
      if (entry.status != 'COMPLETED' && entry.status != 'REFUNDED') continue;
      totals.update(
        entry.method,
        (value) => value + entry.amount,
        ifAbsent: () => entry.amount,
      );
    }

    if (totals.isEmpty) return 'PAID';
    return totals.entries
        .map((entry) => '${entry.key} ${entry.value.toStringAsFixed(2)}')
        .join(' + ');
  }

  PaymentDto? _lastCashPayment(OrderDto paidOrder) {
    for (final entry in paidOrder.payments.reversed) {
      if (entry.method == 'CASH' &&
          (entry.status == 'COMPLETED' || entry.status == 'REFUNDED') &&
          entry.tenderedAmount != null) {
        return entry;
      }
    }
    return null;
  }

  double? _cashReceivedForReceipt(OrderDto paidOrder) =>
      _lastCashPayment(paidOrder)?.tenderedAmount;

  double? _changeForReceipt(OrderDto paidOrder) {
    final value = _lastCashPayment(paidOrder)?.changeAmount ?? 0;
    return value > 0 ? value : null;
  }

  Future<void> retryPaidFinalize() async {
    final current = order;
    if (current == null || current.status != 'PAID') return;
    final payment = current.latestCompletedPayment;
    if (payment == null) {
      setState(() => error = 'Не найдена завершённая оплата для этого заказа.');
      return;
    }
    await printPaidReceiptAndClose(current, payment);
  }

  Future<void> printPaidReceiptAndClose(
    OrderDto paidOrder,
    PaymentDto payment,
  ) async {
    if (printing) return;

    setState(() {
      printing = true;
      error = null;
    });

    try {
      final groups = CartGroup.fromOrder(paidOrder);
      await widget.printer.printReceipt(
        restaurantName: AppConfig.restaurantDisplayName,
        orderNumber: paidOrder.displayNumber,
        hallName: widget.hallName,
        tableName: widget.table.name,
        cashierName: widget.session.employeeName,
        guestCount: paidOrder.guestCount,
        currencyCode: AppConfig.currencyCode,
        total: paidOrder.total,
        paymentMethod: _paymentSummary(paidOrder),
        paidAmount: paidOrder.paidTotal,
        cashReceived: _cashReceivedForReceipt(paidOrder),
        changeAmount: _changeForReceipt(paidOrder),
        completedAt: payment.createdAt,
        items: groups
            .map(
              (group) => ReceiptPrintItem(
                name: group.productName,
                quantity: group.quantity,
                unitPrice: group.unitPrice,
                lineTotal: group.total,
              ),
            )
            .toList(),
      );

      final closed = await widget.api.closeOrder(paidOrder.id);
      if (!mounted) return;
      setState(() => order = closed);
      ScaffoldMessenger.of(context).showSnackBar(
        SnackBar(
          content: Text(
            'Заказ #${closed.displayNumber} оплачен, чек напечатан и заказ закрыт.',
          ),
        ),
      );
      Navigator.of(context).pop(true);
    } catch (e) {
      if (mounted) {
        setState(() {
          order = paidOrder;
          error =
              'Оплата сохранена. Не удалось напечатать чек или закрыть заказ: $e';
        });
      }
    } finally {
      if (mounted) setState(() => printing = false);
    }
  }

  @override
  Widget build(BuildContext context) {
    return Scaffold(
      appBar: AppBar(
        title: Text(
          order == null
              ? 'Стол ${widget.table.name} · Новый заказ'
              : 'Стол ${widget.table.name} · Заказ #${order!.displayNumber}',
        ),
        actions: [
          Padding(
            padding: const EdgeInsets.symmetric(horizontal: 18),
            child: Center(child: Text(widget.session.employeeName)),
          ),
        ],
      ),
      body: FutureBuilder<List<MenuCategory>>(
        future: menuFuture,
        builder: (context, snapshot) {
          if (snapshot.connectionState != ConnectionState.done) {
            return const Center(child: CircularProgressIndicator());
          }
          if (snapshot.hasError) {
            return _ErrorPane(
              message: snapshot.error.toString(),
              onRetry: () => setState(() => menuFuture = widget.api.getMenu()),
            );
          }

          final categories = snapshot.data ?? const <MenuCategory>[];
          if (categories.isEmpty) return const Center(child: Text('Меню пустое'));

          MenuCategory selectedCategory = categories.first;
          for (final category in categories) {
            if (category.id == selectedCategoryId) {
              selectedCategory = category;
              break;
            }
          }

          final busy = mutating || printing || paying;
          final editingLocked =
              order?.status == 'PARTIALLY_PAID' || (order?.isPaid ?? false);
          return LayoutBuilder(
            builder: (context, constraints) {
              final wide = constraints.maxWidth >= 1000;
              final menu = _MenuArea(
                categories: categories,
                selectedCategory: selectedCategory,
                onCategory: (category) =>
                    setState(() => selectedCategoryId = category.id),
                onProduct: addProduct,
                disabled: busy || editingLocked,
              );
              final cart = _OrderPane(
                order: order,
                busy: busy,
                printing: printing,
                paying: paying,
                error: error,
                onPlus: incrementGroup,
                onMinus: decrementGroup,
                onPrintPrecheck: printPrecheck,
                onPay: openPayment,
                onFinalizePaid: retryPaidFinalize,
              );

              return wide
                  ? Row(
                      children: [
                        Expanded(child: menu),
                        SizedBox(width: 430, child: cart),
                      ],
                    )
                  : Column(
                      children: [
                        Expanded(child: menu),
                        SizedBox(height: 350, child: cart),
                      ],
                    );
            },
          );
        },
      ),
    );
  }
}

class _PaymentRequest {
  const _PaymentRequest({
    required this.method,
    required this.amount,
    this.tenderedAmount,
  });

  final String method;
  final double amount;
  final double? tenderedAmount;
}

class _PaymentDialog extends StatefulWidget {
  const _PaymentDialog({
    required this.orderNumber,
    required this.total,
    required this.paid,
    required this.remaining,
  });

  final int orderNumber;
  final double total;
  final double paid;
  final double remaining;

  @override
  State<_PaymentDialog> createState() => _PaymentDialogState();
}

class _PaymentDialogState extends State<_PaymentDialog> {
  late final TextEditingController amountController;
  late final TextEditingController cashController;
  String method = 'CASH';
  String? error;

  @override
  void initState() {
    super.initState();
    final initial = widget.remaining.toStringAsFixed(2);
    amountController = TextEditingController(text: initial);
    cashController = TextEditingController(text: initial);
    amountController.addListener(_refresh);
    cashController.addListener(_refresh);
  }

  @override
  void dispose() {
    amountController
      ..removeListener(_refresh)
      ..dispose();
    cashController
      ..removeListener(_refresh)
      ..dispose();
    super.dispose();
  }

  void _refresh() {
    if (mounted) setState(() => error = null);
  }

  double? _parse(TextEditingController controller) =>
      double.tryParse(controller.text.trim().replaceAll(',', '.'));

  double get _amount => _parse(amountController) ?? 0;
  double get _cashReceived => _parse(cashController) ?? 0;
  double get _change =>
      method == 'CASH' && _cashReceived > _amount
          ? _cashReceived - _amount
          : 0;

  void submit() {
    final amount = _amount;
    if (amount <= 0 || amount > widget.remaining + 0.0001) {
      setState(() {
        error =
            'Сумма оплаты должна быть больше 0 и не превышать остаток '
            '${widget.remaining.toStringAsFixed(2)} AZN.';
      });
      return;
    }

    double? tendered;
    if (method == 'CASH') {
      tendered = _cashReceived;
      if (tendered < amount) {
        setState(() => error = 'Получено наличными меньше суммы оплаты.');
        return;
      }
    }

    Navigator.of(context).pop(
      _PaymentRequest(
        method: method,
        amount: amount,
        tenderedAmount: tendered,
      ),
    );
  }

  @override
  Widget build(BuildContext context) {
    return AlertDialog(
      title: Text('Оплата заказа #${widget.orderNumber}'),
      content: SizedBox(
        width: 420,
        child: Column(
          mainAxisSize: MainAxisSize.min,
          crossAxisAlignment: CrossAxisAlignment.stretch,
          children: [
            Row(
              children: [
                const Text('Итого'),
                const Spacer(),
                Text('${widget.total.toStringAsFixed(2)} AZN'),
              ],
            ),
            if (widget.paid > 0) ...[
              const SizedBox(height: 5),
              Row(
                children: [
                  const Text('Уже оплачено'),
                  const Spacer(),
                  Text('${widget.paid.toStringAsFixed(2)} AZN'),
                ],
              ),
            ],
            const SizedBox(height: 5),
            Row(
              children: [
                const Text(
                  'Осталось',
                  style: TextStyle(fontWeight: FontWeight.w700),
                ),
                const Spacer(),
                Text(
                  '${widget.remaining.toStringAsFixed(2)} AZN',
                  style: const TextStyle(fontWeight: FontWeight.w700),
                ),
              ],
            ),
            const SizedBox(height: 18),
            SegmentedButton<String>(
              segments: const [
                ButtonSegment(
                  value: 'CASH',
                  icon: Icon(Icons.payments_outlined),
                  label: Text('Наличные'),
                ),
                ButtonSegment(
                  value: 'CARD',
                  icon: Icon(Icons.credit_card),
                  label: Text('Карта'),
                ),
              ],
              selected: {method},
              onSelectionChanged: (selection) {
                setState(() {
                  method = selection.first;
                  error = null;
                  if (method == 'CASH' && _cashReceived < _amount) {
                    cashController.text = amountController.text;
                  }
                });
              },
            ),
            const SizedBox(height: 16),
            TextField(
              controller: amountController,
              keyboardType:
                  const TextInputType.numberWithOptions(decimal: true),
              decoration: const InputDecoration(
                labelText: 'Сумма этой оплаты',
                suffixText: 'AZN',
                border: OutlineInputBorder(),
              ),
            ),
            if (method == 'CASH') ...[
              const SizedBox(height: 12),
              TextField(
                controller: cashController,
                keyboardType:
                    const TextInputType.numberWithOptions(decimal: true),
                decoration: const InputDecoration(
                  labelText: 'Получено от клиента',
                  suffixText: 'AZN',
                  border: OutlineInputBorder(),
                ),
              ),
              const SizedBox(height: 10),
              Row(
                children: [
                  const Text('Сдача'),
                  const Spacer(),
                  Text(
                    '${_change.toStringAsFixed(2)} AZN',
                    style: Theme.of(context).textTheme.titleMedium,
                  ),
                ],
              ),
            ],
            if (error != null) ...[
              const SizedBox(height: 10),
              Text(
                error!,
                style: TextStyle(color: Theme.of(context).colorScheme.error),
              ),
            ],
          ],
        ),
      ),
      actions: [
        TextButton(
          onPressed: () => Navigator.of(context).pop(),
          child: const Text('Отмена'),
        ),
        FilledButton.icon(
          onPressed: submit,
          icon: Icon(
            method == 'CASH'
                ? Icons.payments_outlined
                : Icons.credit_card,
          ),
          label: const Text('Оплатить'),
        ),
      ],
    );
  }
}

class _MenuArea extends StatelessWidget {
  const _MenuArea({
    required this.categories,
    required this.selectedCategory,
    required this.onCategory,
    required this.onProduct,
    required this.disabled,
  });

  final List<MenuCategory> categories;
  final MenuCategory selectedCategory;
  final ValueChanged<MenuCategory> onCategory;
  final ValueChanged<MenuProduct> onProduct;
  final bool disabled;

  @override
  Widget build(BuildContext context) {
    return Padding(
      padding: const EdgeInsets.all(16),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          SizedBox(
            height: 48,
            child: ListView.separated(
              scrollDirection: Axis.horizontal,
              itemCount: categories.length,
              separatorBuilder: (_, __) => const SizedBox(width: 10),
              itemBuilder: (context, index) {
                final category = categories[index];
                return ChoiceChip(
                  label: Padding(
                    padding: const EdgeInsets.symmetric(horizontal: 8),
                    child: Text(category.name),
                  ),
                  selected: category.id == selectedCategory.id,
                  onSelected: (_) => onCategory(category),
                );
              },
            ),
          ),
          const SizedBox(height: 14),
          Expanded(
            child: GridView.builder(
              gridDelegate: const SliverGridDelegateWithMaxCrossAxisExtent(
                maxCrossAxisExtent: 220,
                childAspectRatio: 1.35,
                crossAxisSpacing: 12,
                mainAxisSpacing: 12,
              ),
              itemCount: selectedCategory.products.length,
              itemBuilder: (context, index) {
                final product = selectedCategory.products[index];
                return Card(
                  child: InkWell(
                    borderRadius: BorderRadius.circular(12),
                    onTap: disabled ? null : () => onProduct(product),
                    child: Padding(
                      padding: const EdgeInsets.all(16),
                      child: Column(
                        crossAxisAlignment: CrossAxisAlignment.start,
                        children: [
                          Expanded(
                            child: Text(
                              product.name,
                              style: Theme.of(context).textTheme.titleMedium,
                            ),
                          ),
                          Text(
                            '${product.price.toStringAsFixed(2)} ${product.currencyCode}',
                            style: Theme.of(context).textTheme.titleSmall,
                          ),
                        ],
                      ),
                    ),
                  ),
                );
              },
            ),
          ),
        ],
      ),
    );
  }
}

class _OrderPane extends StatelessWidget {
  const _OrderPane({
    required this.order,
    required this.busy,
    required this.printing,
    required this.paying,
    required this.error,
    required this.onPlus,
    required this.onMinus,
    required this.onPrintPrecheck,
    required this.onPay,
    required this.onFinalizePaid,
  });

  final OrderDto? order;
  final bool busy;
  final bool printing;
  final bool paying;
  final String? error;
  final ValueChanged<CartGroup> onPlus;
  final ValueChanged<CartGroup> onMinus;
  final VoidCallback onPrintPrecheck;
  final VoidCallback onPay;
  final VoidCallback onFinalizePaid;

  @override
  Widget build(BuildContext context) {
    final groups = CartGroup.fromOrder(order);
    final editingLocked =
        order?.status == 'PARTIALLY_PAID' || (order?.isPaid ?? false);
    return Material(
      color: Theme.of(context).colorScheme.surface,
      elevation: 3,
      child: Padding(
        padding: const EdgeInsets.all(18),
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.stretch,
          children: [
            Row(
              children: [
                Text(
                  order == null ? 'Новый заказ' : 'Заказ #${order!.displayNumber}',
                  style: Theme.of(context).textTheme.titleLarge,
                ),
                const Spacer(),
                if (busy)
                  const SizedBox(
                    width: 20,
                    height: 20,
                    child: CircularProgressIndicator(strokeWidth: 2),
                  ),
              ],
            ),
            if (error != null) ...[
              const SizedBox(height: 8),
              Text(error!, style: TextStyle(color: Theme.of(context).colorScheme.error)),
            ],
            const Divider(height: 24),
            Expanded(
              child: groups.isEmpty
                  ? const Center(child: Text('Выберите блюдо из меню'))
                  : ListView.separated(
                      itemCount: groups.length,
                      separatorBuilder: (_, __) => const Divider(height: 1),
                      itemBuilder: (context, index) {
                        final group = groups[index];
                        final canRemove = group.lines.any((x) => x.status == 'NEW');
                        return Padding(
                          padding: const EdgeInsets.symmetric(vertical: 10),
                          child: Row(
                            children: [
                              Expanded(
                                child: Column(
                                  crossAxisAlignment: CrossAxisAlignment.start,
                                  children: [
                                    Text(
                                      group.productName,
                                      style: const TextStyle(fontWeight: FontWeight.w600),
                                    ),
                                    const SizedBox(height: 3),
                                    Text(
                                      '${group.quantity.g} × ${group.unitPrice.toStringAsFixed(2)} = ${group.total.toStringAsFixed(2)}',
                                    ),
                                  ],
                                ),
                              ),
                              IconButton.filledTonal(
                                onPressed: busy || editingLocked || !canRemove ? null : () => onMinus(group),
                                icon: const Icon(Icons.remove),
                              ),
                              Padding(
                                padding: const EdgeInsets.symmetric(horizontal: 8),
                                child: Text(
                                  group.quantity.g,
                                  style: Theme.of(context).textTheme.titleMedium,
                                ),
                              ),
                              IconButton.filled(
                                onPressed: busy || editingLocked ? null : () => onPlus(group),
                                icon: const Icon(Icons.add),
                              ),
                            ],
                          ),
                        );
                      },
                    ),
            ),
            const Divider(height: 24),
            Row(
              children: [
                const Text('Итого'),
                const Spacer(),
                Text(
                  '${(order?.total ?? 0).toStringAsFixed(2)} AZN',
                  style: Theme.of(context).textTheme.headlineSmall,
                ),
              ],
            ),
            if ((order?.paidTotal ?? 0) > 0) ...[
              const SizedBox(height: 6),
              Row(
                children: [
                  const Text('Оплачено'),
                  const Spacer(),
                  Text('${order!.paidTotal.toStringAsFixed(2)} AZN'),
                ],
              ),
              const SizedBox(height: 3),
              Row(
                children: [
                  const Text('Осталось'),
                  const Spacer(),
                  Text(
                    '${order!.remaining.toStringAsFixed(2)} AZN',
                    style: const TextStyle(fontWeight: FontWeight.w700),
                  ),
                ],
              ),
            ],
            const SizedBox(height: 12),
            OutlinedButton.icon(
              onPressed: busy ||
                      order == null ||
                      order!.items.isEmpty ||
                      order!.isPaid
                  ? null
                  : onPrintPrecheck,
              icon: const Icon(Icons.print_outlined),
              label: Padding(
                padding: const EdgeInsets.symmetric(vertical: 12),
                child: Text(printing ? 'Печатаем…' : 'Печать предчека'),
              ),
            ),
            const SizedBox(height: 8),
            FilledButton.icon(
              onPressed: busy ||
                      order == null ||
                      order!.items.isEmpty ||
                      (order!.status != 'PAID' && order!.hasNewItems)
                  ? null
                  : order!.status == 'PAID'
                      ? onFinalizePaid
                      : onPay,
              icon: Icon(
                order?.status == 'PAID'
                    ? Icons.receipt_long_outlined
                    : Icons.payments_outlined,
              ),
              label: Padding(
                padding: const EdgeInsets.symmetric(vertical: 14),
                child: Text(
                  order?.status == 'PAID'
                      ? 'Печать чека и закрыть'
                      : paying
                          ? 'Оплата…'
                          : 'Оплата',
                ),
              ),
            ),
          ],
        ),
      ),
    );
  }
}

class CartGroup {
  CartGroup({
    required this.productId,
    required this.productName,
    required this.unitPrice,
    required this.status,
  });

  final String productId;
  final String productName;
  final double unitPrice;
  final String status;
  final List<OrderLineDto> lines = [];

  double get quantity => lines.fold(0, (sum, line) => sum + line.quantity);
  double get total => lines.fold(0, (sum, line) => sum + line.lineTotal);

  static List<CartGroup> fromOrder(OrderDto? order) {
    if (order == null) return const [];
    final map = <String, CartGroup>{};
    for (final line in order.items) {
      final key = '${line.productId}|${line.unitPrice}|${line.status}';
      final group = map.putIfAbsent(
        key,
        () => CartGroup(
          productId: line.productId,
          productName: line.productName,
          unitPrice: line.unitPrice,
          status: line.status,
        ),
      );
      group.lines.add(line);
    }
    return map.values.toList();
  }
}

class _ErrorPane extends StatelessWidget {
  const _ErrorPane({required this.message, required this.onRetry});

  final String message;
  final VoidCallback onRetry;

  @override
  Widget build(BuildContext context) {
    return Center(
      child: Padding(
        padding: const EdgeInsets.all(24),
        child: Column(
          mainAxisSize: MainAxisSize.min,
          children: [
            const Icon(Icons.error_outline, size: 48),
            const SizedBox(height: 12),
            Text(message, textAlign: TextAlign.center),
            const SizedBox(height: 12),
            FilledButton.tonal(onPressed: onRetry, child: const Text('Повторить')),
          ],
        ),
      ),
    );
  }
}

extension PrettyNum on double {
  String get g => this == roundToDouble() ? toInt().toString() : toString();
}
