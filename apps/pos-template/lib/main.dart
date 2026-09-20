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
    setState(() {
      hallsFuture = widget.api.getHalls();
    });
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
      final history = await widget.api.getOrderHistory(take: 200);
      if (!mounted) return;
      await showDialog<void>(
        context: context,
        builder: (_) => _OrderHistoryDialog(
          orders: history,
          canRefund: widget.session.hasPermission('payments.refund'),
          onSearchOrderNumber: (orderNumber) => widget.api.getOrderHistory(
            orderNumber: orderNumber,
            take: 20,
          ),
          onReprint: reprintHistoryOrder,
          onRefund: refundHistoryOrder,
        ),
      );
    } catch (e) {
      if (!mounted) return;
      ScaffoldMessenger.of(context).showSnackBar(
        SnackBar(content: Text('История заказов: $e')),
      );
    }
  }

  Future<void> refundHistoryOrder(OrderHistoryItemDto item) async {
    if (!widget.session.hasPermission('payments.refund')) {
      throw StateError('У сотрудника нет права на возврат оплат.');
    }

    final journal = await widget.api.getOrderPayments(item.order.id);
    final refundable = journal.payments
        .where((payment) => (payment.refundableAmount ?? 0) > 0.005)
        .toList();

    if (refundable.isEmpty) {
      throw StateError('По этому заказу больше нет суммы для возврата.');
    }

    if (!mounted) return;
    final request = await showDialog<_PaymentRefundRequest>(
      context: context,
      barrierDismissible: false,
      builder: (_) => _PaymentRefundDialog(
        orderNumber: item.order.displayNumber,
        payments: refundable,
        currentShift: widget.shift,
      ),
    );

    if (request == null || !mounted) return;

    final result = await widget.api.refundPayment(
      paymentId: request.paymentId,
      shiftId: widget.shift.id,
      amount: request.amount,
      reason: request.reason,
    );

    if (!mounted) return;
    ScaffoldMessenger.of(context).showSnackBar(
      SnackBar(
        content: Text(
          'Возврат ${result.refund.amount.toStringAsFixed(2)} '
          '${result.refund.currencyCode} проведён в текущую смену.',
        ),
      ),
    );
  }

  Future<void> reprintHistoryOrder(OrderHistoryItemDto item) async {
    final paidOrder = item.order;
    final seen = <String>{};
    final completedPayments = paidOrder.payments
        .where(
          (payment) =>
              (payment.status == 'COMPLETED' ||
                  payment.status == 'REFUNDED') &&
              seen.add(payment.id),
        )
        .toList();

    if (completedPayments.isEmpty) {
      throw StateError('У заказа нет завершённой оплаты.');
    }

    final totals = <String, double>{};
    var cashReceived = 0.0;
    var hasCashReceived = false;
    var changeAmount = 0.0;

    for (final payment in completedPayments) {
      totals.update(
        payment.method,
        (value) => value + payment.amount,
        ifAbsent: () => payment.amount,
      );

      if (payment.method == 'CASH') {
        if (payment.tenderedAmount != null) {
          cashReceived += payment.tenderedAmount!;
          hasCashReceived = true;
        }
        changeAmount += payment.changeAmount;
      }
    }

    final paymentSummary = totals.entries
        .map((entry) => '${entry.key} ${entry.value.toStringAsFixed(2)}')
        .join(' + ');

    final paymentParts = totals.entries
        .map(
          (entry) => ReceiptPaymentPart(
            method: entry.key,
            amount: entry.value,
          ),
        )
        .toList();

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
      payments: paymentParts,
      paidAmount: paidOrder.paidTotal,
      cashReceived: hasCashReceived ? cashReceived : null,
      changeAmount: changeAmount > 0.005 ? changeAmount : null,
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
    required this.canRefund,
    required this.onSearchOrderNumber,
    required this.onReprint,
    required this.onRefund,
  });

  final List<OrderHistoryItemDto> orders;
  final bool canRefund;
  final Future<List<OrderHistoryItemDto>> Function(int orderNumber)
      onSearchOrderNumber;
  final Future<void> Function(OrderHistoryItemDto item) onReprint;
  final Future<void> Function(OrderHistoryItemDto item) onRefund;

  @override
  State<_OrderHistoryDialog> createState() => _OrderHistoryDialogState();
}

class _OrderHistoryDialogState extends State<_OrderHistoryDialog> {
  final searchController = TextEditingController();
  List<OrderHistoryItemDto>? remoteResults;
  bool searching = false;
  String? busyOrderId;
  String? error;

  @override
  void initState() {
    super.initState();
    searchController.addListener(_refresh);
  }

  @override
  void dispose() {
    searchController
      ..removeListener(_refresh)
      ..dispose();
    super.dispose();
  }

  void _refresh() {
    if (mounted) {
      setState(() {
        remoteResults = null;
        error = null;
      });
    }
  }

  List<OrderHistoryItemDto> get filteredOrders {
    final query = searchController.text.trim().toLowerCase();
    if (query.isEmpty) return widget.orders;
    if (remoteResults != null) return remoteResults!;

    return widget.orders.where((item) {
      final order = item.order;
      final haystack = [
        order.displayNumber.toString(),
        item.hallName ?? '',
        item.tableName ?? '',
        item.cashierName,
        order.closedAt?.toLocal().toString() ?? '',
      ].join(' ').toLowerCase();
      return haystack.contains(query);
    }).toList();
  }

  Future<void> searchOldOrder() async {
    final value = int.tryParse(searchController.text.trim());
    if (value == null || value <= 0) {
      setState(() {
        error = 'Для поиска по старым сменам введите номер заказа.';
        remoteResults = null;
      });
      return;
    }

    setState(() {
      searching = true;
      error = null;
    });
    try {
      final result = await widget.onSearchOrderNumber(value);
      if (!mounted) return;
      setState(() => remoteResults = result);
    } catch (e) {
      if (mounted) setState(() => error = e.toString());
    } finally {
      if (mounted) setState(() => searching = false);
    }
  }

  Future<void> runAction(
    OrderHistoryItemDto item,
    Future<void> Function(OrderHistoryItemDto item) action,
  ) async {
    if (busyOrderId != null) return;
    setState(() {
      busyOrderId = item.order.id;
      error = null;
    });
    try {
      await action(item);
    } catch (e) {
      if (mounted) setState(() => error = e.toString());
    } finally {
      if (mounted) setState(() => busyOrderId = null);
    }
  }

  @override
  Widget build(BuildContext context) {
    final items = filteredOrders;
    final scheme = Theme.of(context).colorScheme;

    return AlertDialog(
      title: const Text('История заказов'),
      content: SizedBox(
        width: 820,
        height: 560,
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.stretch,
          children: [
            TextField(
              controller: searchController,
              onSubmitted: (_) => searchOldOrder(),
              decoration: InputDecoration(
                prefixIcon: const Icon(Icons.search),
                labelText: 'Найти заказ',
                hintText:
                    'В последних заказах — любой текст; старый заказ — по номеру',
                border: const OutlineInputBorder(),
                suffixIcon: searching
                    ? const Padding(
                        padding: EdgeInsets.all(12),
                        child: SizedBox(
                          width: 18,
                          height: 18,
                          child: CircularProgressIndicator(strokeWidth: 2),
                        ),
                      )
                    : IconButton(
                        tooltip: 'Найти по номеру во всех сменах',
                        onPressed: searchOldOrder,
                        icon: const Icon(Icons.manage_search),
                      ),
              ),
            ),
            const SizedBox(height: 10),
            Container(
              padding: const EdgeInsets.all(10),
              decoration: BoxDecoration(
                color: scheme.surfaceContainerHighest,
                borderRadius: BorderRadius.circular(10),
              ),
              child: Text(
                widget.canRefund
                    ? 'Возврат можно провести по заказу из текущей или старой смены. '
                      'Возврат всегда учитывается в текущей открытой смене.'
                    : 'У вашей роли нет права «Возврат оплат». '
                      'Администратор может включить его в настройках роли.',
                style: const TextStyle(fontSize: 12),
              ),
            ),
            if (error != null) ...[
              const SizedBox(height: 8),
              Text(
                error!,
                style: TextStyle(color: scheme.error),
              ),
            ],
            const SizedBox(height: 8),
            Expanded(
              child: items.isEmpty
                  ? const Center(child: Text('Заказы не найдены.'))
                  : ListView.separated(
                      itemCount: items.length,
                      separatorBuilder: (_, __) => const Divider(height: 1),
                      itemBuilder: (context, index) {
                        final item = items[index];
                        final order = item.order;
                        final busy = busyOrderId == order.id;
                        return ListTile(
                          contentPadding:
                              const EdgeInsets.symmetric(horizontal: 4),
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
                          trailing: Row(
                            mainAxisSize: MainAxisSize.min,
                            children: [
                              FilledButton.tonalIcon(
                                onPressed: busyOrderId == null
                                    ? () => runAction(
                                          item,
                                          widget.onReprint,
                                        )
                                    : null,
                                icon: busy
                                    ? const SizedBox(
                                        width: 16,
                                        height: 16,
                                        child: CircularProgressIndicator(
                                          strokeWidth: 2,
                                        ),
                                      )
                                    : const Icon(Icons.print_outlined),
                                label: const Text('Чек'),
                              ),
                              if (widget.canRefund) ...[
                                const SizedBox(width: 8),
                                FilledButton.tonalIcon(
                                  onPressed: busyOrderId == null
                                      ? () => runAction(
                                            item,
                                            widget.onRefund,
                                          )
                                      : null,
                                  icon: const Icon(Icons.undo),
                                  label: const Text('Возврат'),
                                ),
                              ],
                            ],
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
          onPressed:
              busyOrderId == null ? () => Navigator.of(context).pop() : null,
          child: const Text('Закрыть'),
        ),
      ],
    );
  }
}

class _PaymentRefundRequest {
  const _PaymentRefundRequest({
    required this.paymentId,
    required this.amount,
    required this.reason,
  });

  final String paymentId;
  final double amount;
  final String reason;
}

class _PaymentRefundDialog extends StatefulWidget {
  const _PaymentRefundDialog({
    required this.orderNumber,
    required this.payments,
    required this.currentShift,
  });

  final int orderNumber;
  final List<PaymentDto> payments;
  final ShiftDto currentShift;

  @override
  State<_PaymentRefundDialog> createState() => _PaymentRefundDialogState();
}

class _PaymentRefundDialogState extends State<_PaymentRefundDialog> {
  late String paymentId;
  late final TextEditingController amountController;
  final reasonController = TextEditingController();
  String? error;

  @override
  void initState() {
    super.initState();
    paymentId = widget.payments.first.id;
    amountController = TextEditingController(
      text: _selected.refundableAmount!.toStringAsFixed(2),
    );
  }

  PaymentDto get _selected =>
      widget.payments.firstWhere((payment) => payment.id == paymentId);

  String _methodLabel(String method) =>
      method == 'CASH' ? 'Наличные' : method == 'CARD' ? 'Карта' : method;

  void _selectPayment(String id) {
    setState(() {
      paymentId = id;
      amountController.text =
          _selected.refundableAmount!.toStringAsFixed(2);
      error = null;
    });
  }

  void submit() {
    final amount = double.tryParse(
      amountController.text.trim().replaceAll(',', '.'),
    );
    final reason = reasonController.text.trim();
    final maxAmount = _selected.refundableAmount ?? 0;

    if (amount == null || amount <= 0 || amount > maxAmount + 0.0001) {
      setState(() {
        error =
            'Сумма возврата должна быть от 0.01 до '
            '${maxAmount.toStringAsFixed(2)} ${_selected.currencyCode}.';
      });
      return;
    }

    if (reason.isEmpty) {
      setState(() => error = 'Укажите причину возврата.');
      return;
    }

    Navigator.of(context).pop(
      _PaymentRefundRequest(
        paymentId: paymentId,
        amount: amount,
        reason: reason,
      ),
    );
  }

  @override
  void dispose() {
    amountController.dispose();
    reasonController.dispose();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) {
    final selected = _selected;
    final scheme = Theme.of(context).colorScheme;

    return AlertDialog(
      title: Text('Возврат · заказ #${widget.orderNumber}'),
      content: SizedBox(
        width: 560,
        child: SingleChildScrollView(
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.stretch,
            children: [
              Container(
                padding: const EdgeInsets.all(12),
                decoration: BoxDecoration(
                  color: scheme.surfaceContainerHighest,
                  borderRadius: BorderRadius.circular(12),
                ),
                child: const Text(
                  'Возврат будет записан в текущую открытую смену, '
                  'даже если исходная продажа была в старой смене.',
                ),
              ),
              const SizedBox(height: 16),
              const Text(
                'Выберите исходную оплату',
                style: TextStyle(fontWeight: FontWeight.w800),
              ),
              const SizedBox(height: 8),
              for (final payment in widget.payments)
                RadioListTile<String>(
                  value: payment.id,
                  groupValue: paymentId,
                  onChanged: (value) {
                    if (value != null) _selectPayment(value);
                  },
                  title: Text(
                    '${_methodLabel(payment.method)} · '
                    '${payment.amount.toStringAsFixed(2)} '
                    '${payment.currencyCode}',
                  ),
                  subtitle: Text(
                    'Уже возвращено: '
                    '${payment.refundedAmount.toStringAsFixed(2)} · '
                    'Доступно: '
                    '${(payment.refundableAmount ?? 0).toStringAsFixed(2)}',
                  ),
                ),
              const SizedBox(height: 10),
              TextField(
                controller: amountController,
                keyboardType:
                    const TextInputType.numberWithOptions(decimal: true),
                decoration: InputDecoration(
                  labelText: 'Сумма возврата',
                  suffixText: selected.currencyCode,
                  border: const OutlineInputBorder(),
                ),
              ),
              const SizedBox(height: 12),
              TextField(
                controller: reasonController,
                maxLength: 500,
                minLines: 2,
                maxLines: 4,
                decoration: const InputDecoration(
                  labelText: 'Причина возврата',
                  hintText: 'Например: ошибка оплаты, возврат блюда',
                  border: OutlineInputBorder(),
                ),
              ),
              if (selected.method == 'CARD') ...[
                const SizedBox(height: 8),
                Container(
                  padding: const EdgeInsets.all(10),
                  decoration: BoxDecoration(
                    color: scheme.tertiaryContainer,
                    borderRadius: BorderRadius.circular(10),
                  ),
                  child: const Text(
                    'Карточный возврат пока фиксируется в Restaurant Platform. '
                    'Автоматическую команду банковскому терминалу добавим '
                    'при интеграции эквайринга.',
                  ),
                ),
              ],
              if (error != null) ...[
                const SizedBox(height: 8),
                Text(
                  error!,
                  style: TextStyle(color: scheme.error),
                ),
              ],
            ],
          ),
        ),
      ),
      actions: [
        TextButton(
          onPressed: () => Navigator.of(context).pop(),
          child: const Text('Отмена'),
        ),
        FilledButton.icon(
          onPressed: submit,
          icon: const Icon(Icons.undo),
          label: const Text('Провести возврат'),
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

    List<ModifierSelectionDto> selections = const [];
    if (product.hasModifiers) {
      final selected = await showModifierDialog(product);
      if (selected == null || !mounted) return;
      selections = selected;
    }

    setState(() {
      mutating = true;
      error = null;
    });
    try {
      var current =
          order ?? await widget.api.createOrder(tableId: widget.table.id);
      current = await widget.api.addItem(
        current.id,
        product.id,
        modifiers: selections,
      );
      if (mounted) setState(() => order = current);
    } catch (e) {
      if (mounted) setState(() => error = e.toString());
    } finally {
      if (mounted) setState(() => mutating = false);
    }
  }

  Future<List<ModifierSelectionDto>?> showModifierDialog(
    MenuProduct product,
  ) async {
    final quantities = <String, int>{};

    int selectedInGroup(MenuModifierGroup group) {
      var total = 0;
      for (final option in group.modifiers) {
        total += quantities['${group.id}:${option.id}'] ?? 0;
      }
      return total;
    }

    bool groupIsValid(MenuModifierGroup group) {
      final count = selectedInGroup(group);
      return count >= group.minSelections && count <= group.maxSelections;
    }

    bool allValid() => product.modifierGroups.every(groupIsValid);

    double selectedDelta() {
      var total = 0.0;
      for (final group in product.modifierGroups) {
        for (final option in group.modifiers) {
          final quantity = quantities['${group.id}:${option.id}'] ?? 0;
          total += option.priceDelta * quantity;
        }
      }
      return total;
    }

    return showDialog<List<ModifierSelectionDto>>(
      context: context,
      barrierDismissible: false,
      builder: (dialogContext) => StatefulBuilder(
        builder: (context, setDialogState) {
          final delta = selectedDelta();
          final finalPrice = product.price + delta;

          return AlertDialog(
            insetPadding: const EdgeInsets.all(18),
            titlePadding: const EdgeInsets.fromLTRB(24, 20, 12, 0),
            contentPadding: const EdgeInsets.fromLTRB(24, 14, 24, 8),
            actionsPadding: const EdgeInsets.fromLTRB(24, 8, 24, 20),
            title: Row(
              children: [
                Expanded(
                  child: Column(
                    crossAxisAlignment: CrossAxisAlignment.start,
                    children: [
                      Text(product.name),
                      const SizedBox(height: 3),
                      Text(
                        'Базовая цена: ${product.price.toStringAsFixed(2)} '
                        '${product.currencyCode}',
                        style: Theme.of(context).textTheme.bodySmall,
                      ),
                    ],
                  ),
                ),
                IconButton(
                  tooltip: 'Закрыть',
                  onPressed: () => Navigator.of(dialogContext).pop(),
                  icon: const Icon(Icons.close),
                ),
              ],
            ),
            content: SizedBox(
              width: 720,
              height: 570,
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.stretch,
                children: [
                  Expanded(
                    child: ListView.separated(
                      itemCount: product.modifierGroups.length,
                      separatorBuilder: (_, __) =>
                          const SizedBox(height: 16),
                      itemBuilder: (context, groupIndex) {
                        final group = product.modifierGroups[groupIndex];
                        final selectedCount = selectedInGroup(group);
                        final valid = groupIsValid(group);

                        return Container(
                          padding: const EdgeInsets.all(14),
                          decoration: BoxDecoration(
                            border: Border.all(
                              color: valid
                                  ? Theme.of(context)
                                      .colorScheme
                                      .outlineVariant
                                  : Theme.of(context).colorScheme.error,
                            ),
                            borderRadius: BorderRadius.circular(14),
                          ),
                          child: Column(
                            crossAxisAlignment: CrossAxisAlignment.stretch,
                            children: [
                              Row(
                                children: [
                                  Expanded(
                                    child: Text(
                                      group.name,
                                      style: const TextStyle(
                                        fontSize: 16,
                                        fontWeight: FontWeight.w800,
                                      ),
                                    ),
                                  ),
                                  Container(
                                    padding: const EdgeInsets.symmetric(
                                      horizontal: 9,
                                      vertical: 5,
                                    ),
                                    decoration: BoxDecoration(
                                      color: group.isRequired
                                          ? Theme.of(context)
                                              .colorScheme
                                              .errorContainer
                                          : Theme.of(context)
                                              .colorScheme
                                              .surfaceContainerHighest,
                                      borderRadius:
                                          BorderRadius.circular(999),
                                    ),
                                    child: Text(
                                      group.isRequired
                                          ? 'ОБЯЗАТЕЛЬНО'
                                          : 'НЕОБЯЗАТЕЛЬНО',
                                      style: const TextStyle(
                                        fontSize: 10,
                                        fontWeight: FontWeight.w800,
                                      ),
                                    ),
                                  ),
                                ],
                              ),
                              const SizedBox(height: 4),
                              Text(
                                group.minSelections == group.maxSelections
                                    ? 'Выберите: ${group.minSelections}'
                                    : 'Выберите от ${group.minSelections} '
                                      'до ${group.maxSelections} · '
                                      'сейчас $selectedCount',
                                style: TextStyle(
                                  color: valid
                                      ? Theme.of(context)
                                          .colorScheme
                                          .onSurfaceVariant
                                      : Theme.of(context).colorScheme.error,
                                  fontSize: 12,
                                  fontWeight: valid
                                      ? FontWeight.w500
                                      : FontWeight.w700,
                                ),
                              ),
                              const SizedBox(height: 10),
                              for (final option in group.modifiers) ...[
                                Builder(
                                  builder: (context) {
                                    final key =
                                        '${group.id}:${option.id}';
                                    final quantity =
                                        quantities[key] ?? 0;
                                    final singleChoice =
                                        group.maxSelections == 1;
                                    final canIncrease =
                                        selectedCount <
                                            group.maxSelections ||
                                        quantity > 0;

                                    return Padding(
                                      padding: const EdgeInsets.symmetric(
                                        vertical: 4,
                                      ),
                                      child: Row(
                                        children: [
                                          Expanded(
                                            child: InkWell(
                                              borderRadius:
                                                  BorderRadius.circular(10),
                                              onTap: () {
                                                setDialogState(() {
                                                  if (singleChoice) {
                                                    for (final other
                                                        in group.modifiers) {
                                                      quantities[
                                                          '${group.id}:${other.id}'] = 0;
                                                    }
                                                    quantities[key] =
                                                        quantity > 0 ? 0 : 1;
                                                  } else if (quantity == 0 &&
                                                      selectedCount <
                                                          group.maxSelections) {
                                                    quantities[key] = 1;
                                                  }
                                                });
                                              },
                                              child: Padding(
                                                padding:
                                                    const EdgeInsets.all(9),
                                                child: Row(
                                                  children: [
                                                    Icon(
                                                      quantity > 0
                                                          ? Icons
                                                              .check_circle
                                                          : Icons
                                                              .radio_button_unchecked,
                                                      color: quantity > 0
                                                          ? Theme.of(context)
                                                              .colorScheme
                                                              .primary
                                                          : null,
                                                    ),
                                                    const SizedBox(width: 10),
                                                    Expanded(
                                                      child: Text(
                                                        option.name,
                                                        style: const TextStyle(
                                                          fontWeight:
                                                              FontWeight.w650,
                                                        ),
                                                      ),
                                                    ),
                                                    Text(
                                                      option.priceDelta == 0
                                                          ? 'без доплаты'
                                                          : '${option.priceDelta > 0 ? '+' : ''}'
                                                            '${option.priceDelta.toStringAsFixed(2)} '
                                                            '${product.currencyCode}',
                                                      style: TextStyle(
                                                        color: option
                                                                    .priceDelta >
                                                                0
                                                            ? Theme.of(context)
                                                                .colorScheme
                                                                .primary
                                                            : Theme.of(context)
                                                                .colorScheme
                                                                .onSurfaceVariant,
                                                        fontWeight:
                                                            FontWeight.w700,
                                                      ),
                                                    ),
                                                  ],
                                                ),
                                              ),
                                            ),
                                          ),
                                          if (!singleChoice) ...[
                                            const SizedBox(width: 8),
                                            IconButton.filledTonal(
                                              onPressed: quantity > 0
                                                  ? () => setDialogState(() {
                                                        quantities[key] =
                                                            quantity - 1;
                                                      })
                                                  : null,
                                              icon: const Icon(Icons.remove),
                                            ),
                                            SizedBox(
                                              width: 34,
                                              child: Text(
                                                '$quantity',
                                                textAlign: TextAlign.center,
                                                style: const TextStyle(
                                                  fontWeight: FontWeight.w800,
                                                ),
                                              ),
                                            ),
                                            IconButton.filledTonal(
                                              onPressed: canIncrease &&
                                                      selectedCount <
                                                          group.maxSelections
                                                  ? () => setDialogState(() {
                                                        quantities[key] =
                                                            quantity + 1;
                                                      })
                                                  : null,
                                              icon: const Icon(Icons.add),
                                            ),
                                          ],
                                        ],
                                      ),
                                    );
                                  },
                                ),
                              ],
                            ],
                          ),
                        );
                      },
                    ),
                  ),
                  const SizedBox(height: 12),
                  Container(
                    padding: const EdgeInsets.all(14),
                    decoration: BoxDecoration(
                      color: Theme.of(context)
                          .colorScheme
                          .surfaceContainerHighest,
                      borderRadius: BorderRadius.circular(13),
                    ),
                    child: Row(
                      children: [
                        const Text(
                          'Цена блюда',
                          style: TextStyle(fontWeight: FontWeight.w700),
                        ),
                        const Spacer(),
                        if (delta.abs() > 0.0001)
                          Padding(
                            padding: const EdgeInsets.only(right: 10),
                            child: Text(
                              'модификаторы '
                              '${delta > 0 ? '+' : ''}'
                              '${delta.toStringAsFixed(2)}',
                            ),
                          ),
                        Text(
                          '${finalPrice.toStringAsFixed(2)} '
                          '${product.currencyCode}',
                          style: const TextStyle(
                            fontSize: 19,
                            fontWeight: FontWeight.w900,
                          ),
                        ),
                      ],
                    ),
                  ),
                ],
              ),
            ),
            actions: [
              TextButton(
                onPressed: () => Navigator.of(dialogContext).pop(),
                child: const Text('Отмена'),
              ),
              FilledButton.icon(
                onPressed: allValid()
                    ? () {
                        final result = <ModifierSelectionDto>[];
                        for (final group in product.modifierGroups) {
                          for (final option in group.modifiers) {
                            final quantity = quantities[
                                    '${group.id}:${option.id}'] ??
                                0;
                            if (quantity > 0) {
                              result.add(
                                ModifierSelectionDto(
                                  groupId: group.id,
                                  modifierId: option.id,
                                  quantity: quantity.toDouble(),
                                ),
                              );
                            }
                          }
                        }
                        Navigator.of(dialogContext).pop(result);
                      }
                    : null,
                icon: const Icon(Icons.add_shopping_cart),
                label: const Text('Добавить в заказ'),
              ),
            ],
          );
        },
      ),
    );
  }

  Future<void> incrementGroup(CartGroup group) async {
    if (mutating || printing || paying || order == null) return;

    if (group.hasModifiers) {
      try {
        final categories = await menuFuture;
        MenuProduct? product;
        for (final category in categories) {
          for (final candidate in category.products) {
            if (candidate.id == group.productId) {
              product = candidate;
              break;
            }
          }
          if (product != null) break;
        }
        if (product == null) {
          if (mounted) {
            setState(() => error = 'Блюдо больше не найдено в меню.');
          }
          return;
        }
        await addProduct(product);
        return;
      } catch (e) {
        if (mounted) setState(() => error = 'Модификаторы: $e');
        return;
      }
    }

    setState(() {
      mutating = true;
      error = null;
    });
    try {
      final updated = await widget.api.addItem(
        order!.id,
        group.productId,
        comment: group.comment,
      );
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

  Future<void> changeGuestCount() async {
    final current = order;
    if (current == null || mutating || printing || paying) return;

    final controller = TextEditingController(
      text: current.guestCount.toString(),
    );

    final value = await showDialog<int>(
      context: context,
      builder: (dialogContext) => AlertDialog(
        title: const Text('Количество гостей'),
        content: TextField(
          controller: controller,
          autofocus: true,
          keyboardType: TextInputType.number,
          decoration: const InputDecoration(
            labelText: 'Гостей',
            border: OutlineInputBorder(),
          ),
        ),
        actions: [
          TextButton(
            onPressed: () => Navigator.of(dialogContext).pop(),
            child: const Text('Отмена'),
          ),
          FilledButton(
            onPressed: () {
              final parsed = int.tryParse(controller.text.trim());
              if (parsed != null && parsed >= 1 && parsed <= 100) {
                Navigator.of(dialogContext).pop(parsed);
              }
            },
            child: const Text('Сохранить'),
          ),
        ],
      ),
    );
    controller.dispose();

    if (value == null || !mounted || value == current.guestCount) return;

    setState(() {
      mutating = true;
      error = null;
    });
    try {
      final updated = await widget.api.updateGuestCount(current.id, value);
      if (mounted) setState(() => order = updated);
    } catch (e) {
      if (mounted) setState(() => error = 'Гости: $e');
    } finally {
      if (mounted) setState(() => mutating = false);
    }
  }

  Future<void> moveOrderToTable() async {
    final current = order;
    if (current == null || mutating || printing || paying) return;

    try {
      final halls = await widget.api.getHalls();
      if (!mounted) return;

      final availableHalls = halls
          .where((hall) => hall.tables.isNotEmpty)
          .toList();

      final hasTransferTarget = availableHalls.any(
        (hall) => hall.tables.any(
          (table) => table.id != current.tableId && !table.occupied,
        ),
      );

      if (!hasTransferTarget) {
        ScaffoldMessenger.of(context).showSnackBar(
          const SnackBar(content: Text('Нет свободного стола для переноса.')),
        );
        return;
      }

      String selectedHallId = availableHalls.first.id;
      for (final hall in availableHalls) {
        if (hall.tables.any((table) => table.id == current.tableId)) {
          selectedHallId = hall.id;
          break;
        }
      }

      final target = await showDialog<_MoveOrderTarget>(
        context: context,
        barrierDismissible: false,
        builder: (dialogContext) => StatefulBuilder(
          builder: (context, setDialogState) {
            var selectedHall = availableHalls.first;
            for (final hall in availableHalls) {
              if (hall.id == selectedHallId) {
                selectedHall = hall;
                break;
              }
            }

            final freeCount = selectedHall.tables
                .where(
                  (table) =>
                      table.id != current.tableId && !table.occupied,
                )
                .length;

            return AlertDialog(
              insetPadding: const EdgeInsets.all(20),
              titlePadding: const EdgeInsets.fromLTRB(26, 22, 16, 0),
              contentPadding: const EdgeInsets.fromLTRB(26, 16, 26, 10),
              actionsPadding: const EdgeInsets.fromLTRB(26, 4, 26, 20),
              title: Row(
                children: [
                  Expanded(
                    child: Text(
                      'Перенести заказ #${current.displayNumber}',
                    ),
                  ),
                  IconButton(
                    tooltip: 'Закрыть',
                    onPressed: () => Navigator.of(dialogContext).pop(),
                    icon: const Icon(Icons.close),
                  ),
                ],
              ),
              content: SizedBox(
                width: 760,
                height: 520,
                child: Column(
                  crossAxisAlignment: CrossAxisAlignment.stretch,
                  children: [
                    const Text(
                      'ВЫБЕРИТЕ ЗАЛ',
                      style: TextStyle(
                        fontSize: 12,
                        fontWeight: FontWeight.w900,
                        letterSpacing: .7,
                      ),
                    ),
                    const SizedBox(height: 10),
                    SizedBox(
                      height: 52,
                      child: ListView.separated(
                        scrollDirection: Axis.horizontal,
                        itemCount: availableHalls.length,
                        separatorBuilder: (_, __) =>
                            const SizedBox(width: 8),
                        itemBuilder: (context, index) {
                          final hall = availableHalls[index];
                          final selected = hall.id == selectedHallId;
                          final hallFreeCount = hall.tables
                              .where(
                                (table) =>
                                    table.id != current.tableId &&
                                    !table.occupied,
                              )
                              .length;

                          final label =
                              '${hall.name} · свободно ${hallFreeCount}';

                          return selected
                              ? FilledButton.icon(
                                  onPressed: () {},
                                  icon: const Icon(
                                    Icons.meeting_room_outlined,
                                  ),
                                  label: Text(label),
                                )
                              : OutlinedButton.icon(
                                  onPressed: () {
                                    setDialogState(
                                      () => selectedHallId = hall.id,
                                    );
                                  },
                                  icon: const Icon(
                                    Icons.meeting_room_outlined,
                                  ),
                                  label: Text(label),
                                );
                        },
                      ),
                    ),
                    const SizedBox(height: 18),
                    Row(
                      children: [
                        Text(
                          selectedHall.name,
                          style: Theme.of(context)
                              .textTheme
                              .titleLarge
                              ?.copyWith(fontWeight: FontWeight.w800),
                        ),
                        const Spacer(),
                        Text(
                          'Свободно: $freeCount',
                          style: TextStyle(
                            color: Theme.of(context)
                                .colorScheme
                                .onSurfaceVariant,
                            fontWeight: FontWeight.w600,
                          ),
                        ),
                      ],
                    ),
                    const SizedBox(height: 10),
                    Expanded(
                      child: GridView.builder(
                        gridDelegate:
                            const SliverGridDelegateWithMaxCrossAxisExtent(
                          maxCrossAxisExtent: 190,
                          childAspectRatio: 1.5,
                          crossAxisSpacing: 10,
                          mainAxisSpacing: 10,
                        ),
                        itemCount: selectedHall.tables.length,
                        itemBuilder: (context, index) {
                          final table = selectedHall.tables[index];
                          final isCurrent = table.id == current.tableId;
                          final isOccupied = table.occupied && !isCurrent;
                          final canSelect = !isCurrent && !isOccupied;

                          String status;
                          IconData icon;
                          if (isCurrent) {
                            status = 'Текущий стол';
                            icon = Icons.location_on_outlined;
                          } else if (isOccupied) {
                            status = 'Занят';
                            icon = Icons.lock_outline;
                          } else {
                            status = 'Свободен';
                            icon = Icons.check_circle_outline;
                          }

                          return Card(
                            clipBehavior: Clip.antiAlias,
                            child: InkWell(
                              onTap: canSelect
                                  ? () => Navigator.of(dialogContext).pop(
                                        _MoveOrderTarget(
                                          hallName: selectedHall.name,
                                          table: table,
                                        ),
                                      )
                                  : null,
                              child: Padding(
                                padding: const EdgeInsets.all(14),
                                child: Column(
                                  crossAxisAlignment:
                                      CrossAxisAlignment.start,
                                  children: [
                                    Row(
                                      children: [
                                        Icon(
                                          Icons.table_restaurant,
                                          size: 24,
                                          color: canSelect
                                              ? Theme.of(context)
                                                  .colorScheme
                                                  .primary
                                              : Theme.of(context)
                                                  .disabledColor,
                                        ),
                                        const Spacer(),
                                        Icon(
                                          icon,
                                          size: 18,
                                          color: canSelect
                                              ? Theme.of(context)
                                                  .colorScheme
                                                  .primary
                                              : Theme.of(context)
                                                  .disabledColor,
                                        ),
                                      ],
                                    ),
                                    const Spacer(),
                                    Text(
                                      'Стол ${table.name}',
                                      maxLines: 1,
                                      overflow: TextOverflow.ellipsis,
                                      style: TextStyle(
                                        fontSize: 17,
                                        fontWeight: FontWeight.w800,
                                        color: canSelect
                                            ? null
                                            : Theme.of(context)
                                                .disabledColor,
                                      ),
                                    ),
                                    const SizedBox(height: 3),
                                    Text(
                                      '${table.seats} мест · $status',
                                      maxLines: 1,
                                      overflow: TextOverflow.ellipsis,
                                      style: TextStyle(
                                        fontSize: 11,
                                        color: canSelect
                                            ? Theme.of(context)
                                                .colorScheme
                                                .onSurfaceVariant
                                            : Theme.of(context)
                                                .disabledColor,
                                      ),
                                    ),
                                  ],
                                ),
                              ),
                            ),
                          );
                        },
                      ),
                    ),
                    const SizedBox(height: 8),
                    Text(
                      'Нажмите на свободный стол — заказ сразу будет перенесён.',
                      style: TextStyle(
                        color:
                            Theme.of(context).colorScheme.onSurfaceVariant,
                        fontSize: 12,
                      ),
                    ),
                  ],
                ),
              ),
              actions: [
                TextButton(
                  onPressed: () => Navigator.of(dialogContext).pop(),
                  child: const Text('Отмена'),
                ),
              ],
            );
          },
        ),
      );

      if (target == null || !mounted) return;

      setState(() {
        mutating = true;
        error = null;
      });

      final moved = await widget.api.moveOrder(current.id, target.table.id);
      if (!mounted) return;

      setState(() => order = moved);
      ScaffoldMessenger.of(context).showSnackBar(
        SnackBar(
          content: Text(
            'Заказ #${moved.displayNumber} перенесён: '
            '${target.hallName}, стол ${target.table.name}.',
          ),
        ),
      );

      Navigator.of(context).pop(true);
    } catch (e) {
      if (mounted) setState(() => error = 'Перенос заказа: $e');
    } finally {
      if (mounted) setState(() => mutating = false);
    }
  }

  Future<void> transferOrderItems() async {
    final current = order;
    if (current == null ||
        current.items.where((item) => item.status != 'VOIDED').isEmpty ||
        mutating ||
        printing ||
        paying) {
      return;
    }

    final groups = CartGroup.fromOrder(current);
    if (groups.isEmpty) return;

    final selectedIds = <String>{};

    final itemIds = await showDialog<List<String>>(
      context: context,
      barrierDismissible: false,
      builder: (dialogContext) => StatefulBuilder(
        builder: (context, setDialogState) {
          final selectedCount = groups
              .where(
                (group) =>
                    group.lines.isNotEmpty &&
                    group.lines.every((line) => selectedIds.contains(line.id)),
              )
              .length;

          return AlertDialog(
            insetPadding: const EdgeInsets.all(20),
            title: Text('Перенести позиции · заказ #${current.displayNumber}'),
            content: SizedBox(
              width: 680,
              height: 500,
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.stretch,
                children: [
                  const Text(
                    '1. ВЫБЕРИТЕ ПОЗИЦИИ',
                    style: TextStyle(
                      fontSize: 12,
                      fontWeight: FontWeight.w900,
                      letterSpacing: .7,
                    ),
                  ),
                  const SizedBox(height: 8),
                  Text(
                    'Можно выбрать одну или несколько позиций. '
                    'Уже отправленные на кухню позиции тоже можно перенести.',
                    style: TextStyle(
                      color: Theme.of(context).colorScheme.onSurfaceVariant,
                    ),
                  ),
                  const SizedBox(height: 12),
                  Row(
                    children: [
                      TextButton(
                        onPressed: () {
                          setDialogState(() {
                            selectedIds
                              ..clear()
                              ..addAll(
                                groups.expand(
                                  (group) => group.lines.map((line) => line.id),
                                ),
                              );
                          });
                        },
                        child: const Text('Выбрать все'),
                      ),
                      TextButton(
                        onPressed: () =>
                            setDialogState(() => selectedIds.clear()),
                        child: const Text('Снять выбор'),
                      ),
                      const Spacer(),
                      Text(
                        'Выбрано: $selectedCount',
                        style: const TextStyle(fontWeight: FontWeight.w700),
                      ),
                    ],
                  ),
                  const Divider(height: 1),
                  Expanded(
                    child: ListView.separated(
                      itemCount: groups.length,
                      separatorBuilder: (_, __) => const Divider(height: 1),
                      itemBuilder: (context, index) {
                        final group = groups[index];
                        final lineIds =
                            group.lines.map((line) => line.id).toList();
                        final selected = lineIds.isNotEmpty &&
                            lineIds.every(selectedIds.contains);

                        return CheckboxListTile(
                          value: selected,
                          onChanged: (value) {
                            setDialogState(() {
                              if (value == true) {
                                selectedIds.addAll(lineIds);
                              } else {
                                selectedIds.removeAll(lineIds);
                              }
                            });
                          },
                          secondary: Icon(
                            group.status == 'SENT'
                                ? Icons.soup_kitchen_outlined
                                : Icons.receipt_long_outlined,
                          ),
                          title: Text(
                            group.productName,
                            style: const TextStyle(
                              fontWeight: FontWeight.w700,
                            ),
                          ),
                          subtitle: Text(
                            [
                              '${group.quantity.g} × '
                                  '${group.unitPrice.toStringAsFixed(2)} AZN',
                              group.status == 'SENT'
                                  ? 'Уже отправлено на кухню'
                                  : 'Новое',
                              if (group.comment != null)
                                'Комментарий: ${group.comment}',
                            ].join(' · '),
                          ),
                          controlAffinity: ListTileControlAffinity.leading,
                        );
                      },
                    ),
                  ),
                ],
              ),
            ),
            actions: [
              TextButton(
                onPressed: () => Navigator.of(dialogContext).pop(),
                child: const Text('Отмена'),
              ),
              FilledButton.icon(
                onPressed: selectedIds.isEmpty
                    ? null
                    : () => Navigator.of(dialogContext)
                        .pop(selectedIds.toList()),
                icon: const Icon(Icons.arrow_forward),
                label: const Text('Далее · выбрать стол'),
              ),
            ],
          );
        },
      ),
    );

    if (itemIds == null || itemIds.isEmpty || !mounted) return;

    try {
      final halls = await widget.api.getHalls();
      if (!mounted) return;

      final availableHalls =
          halls.where((hall) => hall.tables.isNotEmpty).toList();
      if (availableHalls.isEmpty) {
        setState(() => error = 'Нет доступных столов для переноса.');
        return;
      }

      String selectedHallId = availableHalls.first.id;
      for (final hall in availableHalls) {
        if (hall.tables.any((table) => table.id == current.tableId)) {
          selectedHallId = hall.id;
          break;
        }
      }

      final target = await showDialog<_MoveOrderTarget>(
        context: context,
        barrierDismissible: false,
        builder: (dialogContext) => StatefulBuilder(
          builder: (context, setDialogState) {
            var selectedHall = availableHalls.first;
            for (final hall in availableHalls) {
              if (hall.id == selectedHallId) {
                selectedHall = hall;
                break;
              }
            }

            return AlertDialog(
              insetPadding: const EdgeInsets.all(20),
              title: Text(
                '2. Куда перенести ${itemIds.length} поз.',
              ),
              content: SizedBox(
                width: 760,
                height: 520,
                child: Column(
                  crossAxisAlignment: CrossAxisAlignment.stretch,
                  children: [
                    const Text(
                      'ВЫБЕРИТЕ ЗАЛ',
                      style: TextStyle(
                        fontSize: 12,
                        fontWeight: FontWeight.w900,
                        letterSpacing: .7,
                      ),
                    ),
                    const SizedBox(height: 10),
                    SizedBox(
                      height: 52,
                      child: ListView.separated(
                        scrollDirection: Axis.horizontal,
                        itemCount: availableHalls.length,
                        separatorBuilder: (_, __) =>
                            const SizedBox(width: 8),
                        itemBuilder: (context, index) {
                          final hall = availableHalls[index];
                          final selected = hall.id == selectedHallId;

                          return selected
                              ? FilledButton.icon(
                                  onPressed: () {},
                                  icon: const Icon(
                                    Icons.meeting_room_outlined,
                                  ),
                                  label: Text(hall.name),
                                )
                              : OutlinedButton.icon(
                                  onPressed: () => setDialogState(
                                    () => selectedHallId = hall.id,
                                  ),
                                  icon: const Icon(
                                    Icons.meeting_room_outlined,
                                  ),
                                  label: Text(hall.name),
                                );
                        },
                      ),
                    ),
                    const SizedBox(height: 16),
                    Expanded(
                      child: GridView.builder(
                        gridDelegate:
                            const SliverGridDelegateWithMaxCrossAxisExtent(
                          maxCrossAxisExtent: 190,
                          childAspectRatio: 1.42,
                          crossAxisSpacing: 10,
                          mainAxisSpacing: 10,
                        ),
                        itemCount: selectedHall.tables.length,
                        itemBuilder: (context, index) {
                          final table = selectedHall.tables[index];
                          final isCurrent = table.id == current.tableId;
                          final targetOrder = table.openOrder;
                          final paymentStarted = targetOrder != null &&
                              (targetOrder.status == 'PARTIALLY_PAID' ||
                                  targetOrder.status == 'PAID');
                          final canSelect = !isCurrent && !paymentStarted;

                          String status;
                          if (isCurrent) {
                            status = 'Текущий стол';
                          } else if (paymentStarted) {
                            status =
                                'Заказ #${targetOrder.displayNumber} · оплата начата';
                          } else if (targetOrder != null) {
                            status =
                                'Добавить в заказ #${targetOrder.displayNumber}';
                          } else {
                            status = 'Свободен · создать новый заказ';
                          }

                          return Card(
                            clipBehavior: Clip.antiAlias,
                            child: InkWell(
                              onTap: canSelect
                                  ? () => Navigator.of(dialogContext).pop(
                                        _MoveOrderTarget(
                                          hallName: selectedHall.name,
                                          table: table,
                                        ),
                                      )
                                  : null,
                              child: Padding(
                                padding: const EdgeInsets.all(14),
                                child: Column(
                                  crossAxisAlignment:
                                      CrossAxisAlignment.start,
                                  children: [
                                    Row(
                                      children: [
                                        Icon(
                                          Icons.table_restaurant,
                                          color: canSelect
                                              ? Theme.of(context)
                                                  .colorScheme
                                                  .primary
                                              : Theme.of(context)
                                                  .disabledColor,
                                        ),
                                        const Spacer(),
                                        if (targetOrder != null && !isCurrent)
                                          const Icon(
                                            Icons.receipt_long_outlined,
                                            size: 18,
                                          ),
                                      ],
                                    ),
                                    const Spacer(),
                                    Text(
                                      'Стол ${table.name}',
                                      style: TextStyle(
                                        fontSize: 17,
                                        fontWeight: FontWeight.w800,
                                        color: canSelect
                                            ? null
                                            : Theme.of(context)
                                                .disabledColor,
                                      ),
                                    ),
                                    const SizedBox(height: 4),
                                    Text(
                                      status,
                                      maxLines: 2,
                                      overflow: TextOverflow.ellipsis,
                                      style: TextStyle(
                                        fontSize: 11,
                                        color: canSelect
                                            ? Theme.of(context)
                                                .colorScheme
                                                .onSurfaceVariant
                                            : Theme.of(context)
                                                .disabledColor,
                                      ),
                                    ),
                                  ],
                                ),
                              ),
                            ),
                          );
                        },
                      ),
                    ),
                    const SizedBox(height: 8),
                    Text(
                      'Можно перенести как на свободный стол, так и '
                      'добавить позиции в уже открытый заказ.',
                      style: TextStyle(
                        color:
                            Theme.of(context).colorScheme.onSurfaceVariant,
                        fontSize: 12,
                      ),
                    ),
                  ],
                ),
              ),
              actions: [
                TextButton(
                  onPressed: () => Navigator.of(dialogContext).pop(),
                  child: const Text('Назад'),
                ),
              ],
            );
          },
        ),
      );

      if (target == null || !mounted) return;

      setState(() {
        mutating = true;
        error = null;
      });

      final result = await widget.api.transferOrderItems(
        orderId: current.id,
        targetTableId: target.table.id,
        itemIds: itemIds,
      );

      if (!mounted) return;

      setState(() => order = result.sourceOrder);

      final destinationText = result.targetCreated
          ? 'создан заказ #${result.targetOrder.displayNumber}'
          : 'добавлено в заказ #${result.targetOrder.displayNumber}';

      ScaffoldMessenger.of(context).showSnackBar(
        SnackBar(
          content: Text(
            'Позиции перенесены: ${target.hallName}, '
            'стол ${target.table.name} · $destinationText.',
          ),
        ),
      );

      if (result.sourceOrder.status == 'CANCELLED') {
        Navigator.of(context).pop(true);
      }
    } catch (e) {
      if (mounted) setState(() => error = 'Перенос позиций: $e');
    } finally {
      if (mounted) setState(() => mutating = false);
    }
  }

  Future<void> editGroupComment(CartGroup group) async {
    final current = order;
    if (current == null ||
        group.status != 'NEW' ||
        mutating ||
        printing ||
        paying) {
      return;
    }

    final controller = TextEditingController(text: group.comment ?? '');
    final value = await showDialog<String?>(
      context: context,
      builder: (dialogContext) => AlertDialog(
        title: Text('Комментарий · ${group.productName}'),
        content: SizedBox(
          width: 460,
          child: TextField(
            controller: controller,
            autofocus: true,
            minLines: 2,
            maxLines: 5,
            maxLength: 500,
            decoration: const InputDecoration(
              hintText: 'Например: без лука, хорошо прожарить',
              labelText: 'Комментарий для кухни',
              border: OutlineInputBorder(),
            ),
          ),
        ),
        actions: [
          TextButton(
            onPressed: () => Navigator.of(dialogContext).pop(),
            child: const Text('Отмена'),
          ),
          TextButton(
            onPressed: () => Navigator.of(dialogContext).pop(''),
            child: const Text('Очистить'),
          ),
          FilledButton(
            onPressed: () =>
                Navigator.of(dialogContext).pop(controller.text.trim()),
            child: const Text('Сохранить'),
          ),
        ],
      ),
    );
    controller.dispose();

    if (value == null || !mounted) return;

    final newLines = group.lines
        .where((line) => line.status == 'NEW')
        .toList();
    if (newLines.isEmpty) return;

    setState(() {
      mutating = true;
      error = null;
    });

    try {
      OrderDto updated = current;
      for (final line in newLines) {
        updated = await widget.api.updateItemComment(
          current.id,
          line.id,
          value.isEmpty ? null : value,
        );
      }
      if (mounted) setState(() => order = updated);
    } catch (e) {
      if (mounted) setState(() => error = 'Комментарий: $e');
    } finally {
      if (mounted) setState(() => mutating = false);
    }
  }

  Future<void> voidSentItem(CartGroup group) async {
    final current = order;
    if (current == null ||
        group.status != 'SENT' ||
        !widget.session.hasPermission('orders.void') ||
        mutating ||
        printing ||
        paying) {
      return;
    }

    OrderLineDto? target;
    for (final line in group.lines.reversed) {
      if (line.status == 'SENT') {
        target = line;
        break;
      }
    }
    if (target == null) return;

    final controller = TextEditingController();
    final reason = await showDialog<String>(
      context: context,
      barrierDismissible: false,
      builder: (dialogContext) => AlertDialog(
        title: Text('Отменить ${group.productName}?'),
        content: SizedBox(
          width: 460,
          child: Column(
            mainAxisSize: MainAxisSize.min,
            crossAxisAlignment: CrossAxisAlignment.stretch,
            children: [
              const Text(
                'Позиция уже отправлена на кухню. '
                'На кухонный принтер будет отправлен чек отмены.',
              ),
              const SizedBox(height: 14),
              TextField(
                controller: controller,
                autofocus: true,
                maxLength: 500,
                minLines: 2,
                maxLines: 4,
                decoration: const InputDecoration(
                  labelText: 'Причина отмены',
                  hintText: 'Например: клиент передумал',
                  border: OutlineInputBorder(),
                ),
              ),
            ],
          ),
        ),
        actions: [
          TextButton(
            onPressed: () => Navigator.of(dialogContext).pop(),
            child: const Text('Назад'),
          ),
          FilledButton(
            onPressed: () {
              final value = controller.text.trim();
              if (value.isNotEmpty) {
                Navigator.of(dialogContext).pop(value);
              }
            },
            child: const Text('Отменить позицию'),
          ),
        ],
      ),
    );
    controller.dispose();

    if (reason == null || !mounted) return;

    setState(() {
      mutating = true;
      error = null;
    });
    try {
      final updated = await widget.api.voidItem(
        current.id,
        target.id,
        reason,
      );
      if (!mounted) return;
      setState(() => order = updated);
      ScaffoldMessenger.of(context).showSnackBar(
        SnackBar(
          content: Text(
            '${group.productName}: отмена сохранена и отправлена на кухню.',
          ),
        ),
      );
    } catch (e) {
      if (mounted) setState(() => error = 'Отмена позиции: $e');
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

    setState(() {
      paying = true;
      error = null;
    });

    try {
      final result = await showDialog<PaymentResultDto>(
        context: context,
        barrierDismissible: false,
        builder: (dialogContext) => _PaymentDialog(
          api: widget.api,
          shiftId: widget.shift.id,
          order: current,
          onOrderChanged: (updated) {
            if (mounted) setState(() => order = updated);
          },
        ),
      );

      if (!mounted || result == null) return;

      setState(() => order = result.order);
      if (result.order.status == 'PAID') {
        await printPaidReceiptAndClose(result.order, result.payment);
      }
    } catch (e) {
      if (mounted) setState(() => error = 'Оплата: $e');
    } finally {
      if (mounted) setState(() => paying = false);
    }
  }

  List<PaymentDto> _uniqueCompletedPayments(OrderDto paidOrder) {
    final seen = <String>{};
    final result = <PaymentDto>[];
    for (final payment in paidOrder.payments) {
      if (payment.status != 'COMPLETED' && payment.status != 'REFUNDED') {
        continue;
      }
      if (seen.add(payment.id)) result.add(payment);
    }
    return result;
  }

  String _paymentSummary(OrderDto paidOrder) {
    final totals = <String, double>{};
    for (final entry in _uniqueCompletedPayments(paidOrder)) {
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

  List<ReceiptPaymentPart> _receiptPaymentParts(OrderDto paidOrder) {
    final totals = <String, double>{};
    for (final entry in _uniqueCompletedPayments(paidOrder)) {
      totals.update(
        entry.method,
        (value) => value + entry.amount,
        ifAbsent: () => entry.amount,
      );
    }

    return totals.entries
        .map(
          (entry) => ReceiptPaymentPart(
            method: entry.key,
            amount: entry.value,
          ),
        )
        .toList();
  }

  double? _cashReceivedForReceipt(OrderDto paidOrder) {
    final cash = _uniqueCompletedPayments(paidOrder)
        .where((payment) => payment.method == 'CASH')
        .toList();
    if (cash.isEmpty) return null;

    var total = 0.0;
    var hasTendered = false;
    for (final payment in cash) {
      if (payment.tenderedAmount != null) {
        total += payment.tenderedAmount!;
        hasTendered = true;
      }
    }
    return hasTendered ? total : null;
  }

  double? _changeForReceipt(OrderDto paidOrder) {
    var total = 0.0;
    for (final payment in _uniqueCompletedPayments(paidOrder)) {
      if (payment.method == 'CASH') total += payment.changeAmount;
    }
    return total > 0.005 ? total : null;
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
        payments: _receiptPaymentParts(paidOrder),
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

  Future<void> sendToKitchen() async {
    if (mutating ||
        printing ||
        paying ||
        order == null ||
        !order!.hasNewItems) {
      return;
    }

    setState(() {
      mutating = true;
      error = null;
    });

    try {
      final updated = await widget.api.sendOrderToKitchen(order!.id);
      if (!mounted) return;

      setState(() => order = updated);
      ScaffoldMessenger.of(context).showSnackBar(
        const SnackBar(content: Text('Заказ отправлен на кухню')),
      );
    } catch (e) {
      if (mounted) setState(() => error = e.toString());
    } finally {
      if (mounted) setState(() => mutating = false);
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
          if (order != null && !order!.isPaid && order!.status != 'PARTIALLY_PAID')
            TextButton.icon(
              onPressed: mutating ? null : changeGuestCount,
              icon: const Icon(Icons.people_outline),
              label: Text('Гости: ${order!.guestCount}'),
            ),
          if (order != null && !order!.isPaid && order!.status != 'PARTIALLY_PAID')
            TextButton.icon(
              onPressed: mutating ? null : moveOrderToTable,
              icon: const Icon(Icons.swap_horiz),
              label: const Text('Перенести'),
            ),
          if (order != null &&
              !order!.isPaid &&
              order!.status != 'PARTIALLY_PAID' &&
              order!.items.any((item) => item.status != 'VOIDED'))
            TextButton.icon(
              onPressed: mutating ? null : transferOrderItems,
              icon: const Icon(Icons.call_split),
              label: const Text('Позиции'),
            ),
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
              onRetry: () {
                setState(() {
                  menuFuture = widget.api.getMenu();
                });
              },
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
                canVoid: widget.session.hasPermission('orders.void'),
                onPlus: incrementGroup,
                onMinus: decrementGroup,
                onSend: sendToKitchen,
                onComment: editGroupComment,
                onVoid: voidSentItem,
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

class _PaymentDialog extends StatefulWidget {
  const _PaymentDialog({
    required this.api,
    required this.shiftId,
    required this.order,
    required this.onOrderChanged,
  });

  final PosApiClient api;
  final String shiftId;
  final OrderDto order;
  final ValueChanged<OrderDto> onOrderChanged;

  @override
  State<_PaymentDialog> createState() => _PaymentDialogState();
}

class _PaymentDialogState extends State<_PaymentDialog> {
  late OrderDto currentOrder;
  late final TextEditingController mixedCashController;
  late final TextEditingController cashReceivedController;

  String mode = 'CASH';
  String? error;
  bool submitting = false;

  @override
  void initState() {
    super.initState();
    currentOrder = widget.order;
    final half = currentOrder.remaining / 2;
    mixedCashController = TextEditingController(text: half.toStringAsFixed(2));
    cashReceivedController = TextEditingController(
      text: currentOrder.remaining.toStringAsFixed(2),
    );
    mixedCashController.addListener(_refresh);
    cashReceivedController.addListener(_refresh);
  }

  @override
  void dispose() {
    mixedCashController
      ..removeListener(_refresh)
      ..dispose();
    cashReceivedController
      ..removeListener(_refresh)
      ..dispose();
    super.dispose();
  }

  void _refresh() {
    if (mounted) setState(() => error = null);
  }

  double? _parse(TextEditingController controller) =>
      double.tryParse(controller.text.trim().replaceAll(',', '.'));

  double get _mixedCash => _parse(mixedCashController) ?? 0;

  double get _cashDue {
    if (mode == 'CASH') return currentOrder.remaining;
    if (mode == 'MIXED') return _mixedCash;
    return 0;
  }

  double get _cardDue {
    if (mode == 'CARD') return currentOrder.remaining;
    if (mode == 'MIXED') {
      final value = currentOrder.remaining - _mixedCash;
      return value > 0 ? value : 0;
    }
    return 0;
  }

  double get _cashReceived => _parse(cashReceivedController) ?? 0;

  double get _change =>
      _cashDue > 0 && _cashReceived > _cashDue
          ? _cashReceived - _cashDue
          : 0;

  String _methodLabel(String value) =>
      value == 'CASH' ? 'Наличные' : 'Карта';

  void _selectMode(String value) {
    setState(() {
      mode = value;
      error = null;

      if (mode == 'MIXED') {
        final half = currentOrder.remaining / 2;
        if (_mixedCash <= 0 || _mixedCash >= currentOrder.remaining) {
          mixedCashController.text = half.toStringAsFixed(2);
        }
        cashReceivedController.text = _mixedCash.toStringAsFixed(2);
      } else if (mode == 'CASH') {
        cashReceivedController.text =
            currentOrder.remaining.toStringAsFixed(2);
      }
    });
  }

  void _setHalf() {
    final half = currentOrder.remaining / 2;
    mixedCashController.text = half.toStringAsFixed(2);
    cashReceivedController.text = half.toStringAsFixed(2);
  }

  List<double> _cashSuggestions() {
    final amount = _cashDue;
    final values = <double>[amount, 5, 10, 20, 50, 100, 200]
        .where((value) => value + 0.0001 >= amount && value > 0)
        .toSet()
        .toList()
      ..sort();
    return values.take(6).toList();
  }

  void _applyOrder(PaymentResultDto result) {
    currentOrder = result.order;
    widget.onOrderChanged(result.order);
  }

  Future<void> submit() async {
    if (submitting) return;

    final remaining = currentOrder.remaining;
    if (remaining <= 0) return;

    if (mode == 'MIXED') {
      if (_mixedCash <= 0.005 || _mixedCash >= remaining - 0.005) {
        setState(() {
          error =
              'Для смешанной оплаты укажите часть наличными. '
              'Остаток автоматически пойдёт на карту.';
        });
        return;
      }
    }

    if ((mode == 'CASH' || mode == 'MIXED') &&
        _cashReceived + 0.0001 < _cashDue) {
      setState(() {
        error =
            'Получено наличными меньше суммы наличной части '
            '${_cashDue.toStringAsFixed(2)} AZN.';
      });
      return;
    }

    setState(() {
      submitting = true;
      error = null;
    });

    var cashPartSaved = false;

    try {
      PaymentResultDto result;

      if (mode == 'CASH') {
        result = await widget.api.payOrder(
          orderId: currentOrder.id,
          shiftId: widget.shiftId,
          method: 'CASH',
          amount: remaining,
          tenderedAmount: _cashReceived,
        );
        _applyOrder(result);
      } else if (mode == 'CARD') {
        result = await widget.api.payOrder(
          orderId: currentOrder.id,
          shiftId: widget.shiftId,
          method: 'CARD',
          amount: remaining,
        );
        _applyOrder(result);
      } else {
        final cashAmount = _mixedCash;
        final cashResult = await widget.api.payOrder(
          orderId: currentOrder.id,
          shiftId: widget.shiftId,
          method: 'CASH',
          amount: cashAmount,
          tenderedAmount: _cashReceived,
        );
        cashPartSaved = true;
        _applyOrder(cashResult);

        if (cashResult.remaining <= 0.005) {
          result = cashResult;
        } else {
          result = await widget.api.payOrder(
            orderId: currentOrder.id,
            shiftId: widget.shiftId,
            method: 'CARD',
            amount: cashResult.remaining,
          );
          _applyOrder(result);
        }
      }

      if (!mounted) return;

      if (result.order.status == 'PAID') {
        Navigator.of(context).pop(result);
        return;
      }

      setState(() {
        submitting = false;
        error =
            'Оплата сохранена, но осталось '
            '${result.remaining.toStringAsFixed(2)} AZN.';
      });
    } catch (e) {
      if (!mounted) return;

      if (cashPartSaved) {
        mode = 'CARD';
        cashReceivedController.text =
            currentOrder.remaining.toStringAsFixed(2);
        setState(() {
          submitting = false;
          error =
              'Наличная часть уже сохранена. Карта не завершилась: $e\n'
              'Осталось ${currentOrder.remaining.toStringAsFixed(2)} AZN. '
              'Нажмите «Оплатить картой» для повтора.';
        });
      } else {
        setState(() {
          submitting = false;
          error = e.toString();
        });
      }
    }
  }

  Widget _modeButton({
    required String value,
    required IconData icon,
    required String title,
    required String subtitle,
  }) {
    final selected = mode == value;
    final child = Padding(
      padding: const EdgeInsets.symmetric(vertical: 10),
      child: Column(
        mainAxisSize: MainAxisSize.min,
        children: [
          Icon(icon, size: 28),
          const SizedBox(height: 6),
          Text(
            title,
            style: const TextStyle(fontWeight: FontWeight.w800),
          ),
          const SizedBox(height: 2),
          Text(
            subtitle,
            textAlign: TextAlign.center,
            style: const TextStyle(fontSize: 11),
          ),
        ],
      ),
    );

    return SizedBox(
      height: 90,
      child: selected
          ? FilledButton(
              onPressed: submitting ? null : () => _selectMode(value),
              child: child,
            )
          : OutlinedButton(
              onPressed: submitting ? null : () => _selectMode(value),
              child: child,
            ),
    );
  }

  @override
  Widget build(BuildContext context) {
    final remaining = currentOrder.remaining;
    final seen = <String>{};
    final completedPayments = currentOrder.payments
        .where(
          (payment) =>
              (payment.status == 'COMPLETED' ||
                  payment.status == 'REFUNDED') &&
              seen.add(payment.id),
        )
        .toList();

    final scheme = Theme.of(context).colorScheme;

    String buttonText;
    if (mode == 'CASH') {
      buttonText =
          'Оплатить ${remaining.toStringAsFixed(2)} AZN наличными';
    } else if (mode == 'CARD') {
      buttonText =
          'Оплатить ${remaining.toStringAsFixed(2)} AZN картой';
    } else {
      buttonText =
          'Оплатить: ${_cashDue.toStringAsFixed(2)} наличными + '
          '${_cardDue.toStringAsFixed(2)} картой';
    }

    return AlertDialog(
      insetPadding: const EdgeInsets.all(20),
      titlePadding: const EdgeInsets.fromLTRB(28, 22, 18, 0),
      contentPadding: const EdgeInsets.fromLTRB(28, 16, 28, 10),
      actionsPadding: const EdgeInsets.fromLTRB(28, 4, 28, 22),
      title: Row(
        children: [
          Expanded(
            child: Text('Оплата заказа #${currentOrder.displayNumber}'),
          ),
          IconButton(
            tooltip: 'Закрыть',
            onPressed: submitting ? null : () => Navigator.of(context).pop(),
            icon: const Icon(Icons.close),
          ),
        ],
      ),
      content: SizedBox(
        width: 760,
        child: SingleChildScrollView(
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.stretch,
            children: [
              Container(
                padding: const EdgeInsets.symmetric(
                  horizontal: 10,
                  vertical: 16,
                ),
                decoration: BoxDecoration(
                  color: scheme.surfaceContainerHighest.withValues(alpha: .55),
                  borderRadius: BorderRadius.circular(18),
                ),
                child: Row(
                  children: [
                    Expanded(
                      child: _PaymentSummaryValue(
                        label: 'Итого',
                        value: currentOrder.total,
                      ),
                    ),
                    Expanded(
                      child: _PaymentSummaryValue(
                        label: 'Уже оплачено',
                        value: currentOrder.paidTotal,
                      ),
                    ),
                    Expanded(
                      child: _PaymentSummaryValue(
                        label: 'ОСТАЛОСЬ',
                        value: remaining,
                        emphasized: true,
                      ),
                    ),
                  ],
                ),
              ),
              if (completedPayments.isNotEmpty) ...[
                const SizedBox(height: 12),
                Wrap(
                  spacing: 8,
                  runSpacing: 8,
                  children: [
                    for (final payment in completedPayments)
                      Chip(
                        avatar: Icon(
                          payment.method == 'CASH'
                              ? Icons.payments_outlined
                              : Icons.credit_card,
                          size: 18,
                        ),
                        label: Text(
                          '${_methodLabel(payment.method)} '
                          '${payment.amount.toStringAsFixed(2)} AZN',
                        ),
                      ),
                  ],
                ),
              ],
              const SizedBox(height: 20),
              const Text(
                'КАК ОПЛАЧИВАЕТ КЛИЕНТ?',
                style: TextStyle(
                  fontSize: 12,
                  fontWeight: FontWeight.w900,
                  letterSpacing: .7,
                ),
              ),
              const SizedBox(height: 10),
              Row(
                children: [
                  Expanded(
                    child: _modeButton(
                      value: 'CASH',
                      icon: Icons.payments_outlined,
                      title: 'НАЛИЧНЫЕ',
                      subtitle: 'Вся оставшаяся сумма',
                    ),
                  ),
                  const SizedBox(width: 10),
                  Expanded(
                    child: _modeButton(
                      value: 'CARD',
                      icon: Icons.credit_card,
                      title: 'КАРТА',
                      subtitle: 'Вся оставшаяся сумма',
                    ),
                  ),
                  const SizedBox(width: 10),
                  Expanded(
                    child: _modeButton(
                      value: 'MIXED',
                      icon: Icons.call_split,
                      title: 'СМЕШАННАЯ',
                      subtitle: 'Часть наличными + часть картой',
                    ),
                  ),
                ],
              ),
              if (mode == 'MIXED') ...[
                const SizedBox(height: 22),
                Container(
                  padding: const EdgeInsets.all(18),
                  decoration: BoxDecoration(
                    border: Border.all(color: scheme.outlineVariant),
                    borderRadius: BorderRadius.circular(16),
                  ),
                  child: Column(
                    crossAxisAlignment: CrossAxisAlignment.stretch,
                    children: [
                      Row(
                        children: [
                          const Expanded(
                            child: Text(
                              'РАСПРЕДЕЛЕНИЕ ОПЛАТЫ',
                              style: TextStyle(
                                fontWeight: FontWeight.w900,
                              ),
                            ),
                          ),
                          ActionChip(
                            label: const Text('50 / 50'),
                            onPressed: submitting ? null : _setHalf,
                          ),
                        ],
                      ),
                      const SizedBox(height: 14),
                      Row(
                        crossAxisAlignment: CrossAxisAlignment.end,
                        children: [
                          Expanded(
                            child: TextField(
                              controller: mixedCashController,
                              enabled: !submitting,
                              style: const TextStyle(
                                fontSize: 24,
                                fontWeight: FontWeight.w800,
                              ),
                              keyboardType:
                                  const TextInputType.numberWithOptions(
                                decimal: true,
                              ),
                              decoration: const InputDecoration(
                                labelText: 'Наличными',
                                suffixText: 'AZN',
                                prefixIcon:
                                    Icon(Icons.payments_outlined),
                                border: OutlineInputBorder(),
                              ),
                            ),
                          ),
                          const Padding(
                            padding: EdgeInsets.symmetric(horizontal: 14),
                            child: Icon(Icons.add, size: 26),
                          ),
                          Expanded(
                            child: InputDecorator(
                              decoration: const InputDecoration(
                                labelText: 'Картой',
                                suffixText: 'AZN',
                                prefixIcon: Icon(Icons.credit_card),
                                border: OutlineInputBorder(),
                              ),
                              child: Text(
                                _cardDue.toStringAsFixed(2),
                                style: const TextStyle(
                                  fontSize: 24,
                                  fontWeight: FontWeight.w800,
                                ),
                              ),
                            ),
                          ),
                        ],
                      ),
                      const SizedBox(height: 8),
                      Text(
                        'Введите только сумму наличными — сумма по карте '
                        'посчитается автоматически.',
                        style: TextStyle(
                          color: scheme.onSurfaceVariant,
                          fontSize: 12,
                        ),
                      ),
                    ],
                  ),
                ),
              ],
              if (mode == 'CASH' || mode == 'MIXED') ...[
                const SizedBox(height: 20),
                Text(
                  mode == 'CASH'
                      ? 'КЛИЕНТ ДАЛ НАЛИЧНЫМИ'
                      : 'КЛИЕНТ ДАЛ ДЛЯ НАЛИЧНОЙ ЧАСТИ',
                  style: const TextStyle(
                    fontSize: 12,
                    fontWeight: FontWeight.w900,
                    letterSpacing: .6,
                  ),
                ),
                const SizedBox(height: 10),
                TextField(
                  controller: cashReceivedController,
                  enabled: !submitting,
                  style: const TextStyle(
                    fontSize: 24,
                    fontWeight: FontWeight.w800,
                  ),
                  keyboardType:
                      const TextInputType.numberWithOptions(decimal: true),
                  decoration: InputDecoration(
                    prefixIcon: const Icon(Icons.payments_outlined),
                    suffixText: 'AZN',
                    helperText:
                        'Наличная часть: ${_cashDue.toStringAsFixed(2)} AZN',
                    border: const OutlineInputBorder(),
                  ),
                ),
                const SizedBox(height: 8),
                Wrap(
                  spacing: 8,
                  runSpacing: 8,
                  children: [
                    for (final value in _cashSuggestions())
                      ActionChip(
                        label: Text(
                          (value - _cashDue).abs() < 0.005
                              ? 'Ровно'
                              : value.toStringAsFixed(0),
                        ),
                        onPressed: submitting
                            ? null
                            : () {
                                cashReceivedController.text =
                                    value.toStringAsFixed(2);
                              },
                      ),
                  ],
                ),
                const SizedBox(height: 12),
                Container(
                  padding: const EdgeInsets.symmetric(
                    horizontal: 16,
                    vertical: 14,
                  ),
                  decoration: BoxDecoration(
                    color: _change > 0
                        ? scheme.primaryContainer
                        : scheme.surfaceContainerHighest,
                    borderRadius: BorderRadius.circular(14),
                  ),
                  child: Row(
                    children: [
                      const Text(
                        'СДАЧА',
                        style: TextStyle(fontWeight: FontWeight.w900),
                      ),
                      const Spacer(),
                      Text(
                        '${_change.toStringAsFixed(2)} AZN',
                        style: const TextStyle(
                          fontSize: 24,
                          fontWeight: FontWeight.w900,
                        ),
                      ),
                    ],
                  ),
                ),
              ],
              if (error != null) ...[
                const SizedBox(height: 14),
                Container(
                  padding: const EdgeInsets.all(12),
                  decoration: BoxDecoration(
                    color: scheme.errorContainer,
                    borderRadius: BorderRadius.circular(12),
                  ),
                  child: Text(
                    error!,
                    style: TextStyle(
                      color: scheme.onErrorContainer,
                      fontWeight: FontWeight.w600,
                    ),
                  ),
                ),
              ],
            ],
          ),
        ),
      ),
      actions: [
        TextButton(
          onPressed: submitting ? null : () => Navigator.of(context).pop(),
          child: const Text('Отмена'),
        ),
        const SizedBox(width: 8),
        SizedBox(
          height: 54,
          child: FilledButton.icon(
            onPressed: submitting ? null : submit,
            icon: submitting
                ? const SizedBox(
                    width: 18,
                    height: 18,
                    child: CircularProgressIndicator(strokeWidth: 2),
                  )
                : const Icon(Icons.check_circle_outline),
            label: Text(
              submitting ? 'Проводим оплату…' : buttonText,
            ),
          ),
        ),
      ],
    );
  }
}

class _PaymentSummaryValue extends StatelessWidget {
  const _PaymentSummaryValue({
    required this.label,
    required this.value,
    this.emphasized = false,
  });

  final String label;
  final double value;
  final bool emphasized;

  @override
  Widget build(BuildContext context) {
    final scheme = Theme.of(context).colorScheme;
    return Padding(
      padding: const EdgeInsets.symmetric(horizontal: 14),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Text(
            label,
            style: TextStyle(
              color: scheme.onSurfaceVariant,
              fontSize: 12,
            ),
          ),
          const SizedBox(height: 4),
          Text(
            '${value.toStringAsFixed(2)} AZN',
            style: TextStyle(
              fontSize: emphasized ? 22 : 18,
              fontWeight: emphasized ? FontWeight.w900 : FontWeight.w700,
              color: emphasized ? scheme.primary : null,
            ),
          ),
        ],
      ),
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
    required this.canVoid,
    required this.onPlus,
    required this.onMinus,
    required this.onSend,
    required this.onComment,
    required this.onVoid,
    required this.onPrintPrecheck,
    required this.onPay,
    required this.onFinalizePaid,
  });

  final OrderDto? order;
  final bool busy;
  final bool printing;
  final bool paying;
  final String? error;
  final bool canVoid;
  final ValueChanged<CartGroup> onPlus;
  final ValueChanged<CartGroup> onMinus;
  final Future<void> Function() onSend;
  final ValueChanged<CartGroup> onComment;
  final ValueChanged<CartGroup> onVoid;
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
                        final canRemove =
                            group.lines.any((x) => x.status == 'NEW');
                        final canVoidGroup =
                            canVoid && group.status == 'SENT';

                        return Padding(
                          padding: const EdgeInsets.symmetric(vertical: 9),
                          child: Column(
                            crossAxisAlignment: CrossAxisAlignment.stretch,
                            children: [
                              Row(
                                children: [
                                  Expanded(
                                    child: Column(
                                      crossAxisAlignment:
                                          CrossAxisAlignment.start,
                                      children: [
                                        Row(
                                          children: [
                                            Expanded(
                                              child: Text(
                                                group.productName,
                                                style: const TextStyle(
                                                  fontWeight: FontWeight.w700,
                                                ),
                                              ),
                                            ),
                                            _OrderItemStatusBadge(
                                              status: group.status,
                                            ),
                                          ],
                                        ),
                                        const SizedBox(height: 3),
                                        Text(
                                          '${group.quantity.g} × '
                                          '${group.unitPrice.toStringAsFixed(2)} '
                                          '= ${group.total.toStringAsFixed(2)}',
                                        ),
                                        if (group.comment != null) ...[
                                          const SizedBox(height: 4),
                                          Text(
                                            'Комментарий: ${group.comment}',
                                            style: TextStyle(
                                              color: Theme.of(context)
                                                  .colorScheme
                                                  .primary,
                                              fontWeight: FontWeight.w600,
                                              fontSize: 12,
                                            ),
                                          ),
                                        ],
                                      ],
                                    ),
                                  ),
                                  if ((group.status == 'NEW') || canVoidGroup)
                                    PopupMenuButton<String>(
                                      tooltip: 'Действия',
                                      onSelected: (value) {
                                        if (value == 'comment') {
                                          onComment(group);
                                        } else if (value == 'void') {
                                          onVoid(group);
                                        }
                                      },
                                      itemBuilder: (_) => [
                                        if (group.status == 'NEW')
                                          const PopupMenuItem(
                                            value: 'comment',
                                            child: Text(
                                              'Комментарий для кухни',
                                            ),
                                          ),
                                        if (canVoidGroup)
                                          const PopupMenuItem(
                                            value: 'void',
                                            child: Text(
                                              'Отменить отправленную позицию',
                                            ),
                                          ),
                                      ],
                                    ),
                                  IconButton.filledTonal(
                                    onPressed: busy ||
                                            editingLocked ||
                                            !canRemove
                                        ? null
                                        : () => onMinus(group),
                                    icon: const Icon(Icons.remove),
                                  ),
                                  Padding(
                                    padding: const EdgeInsets.symmetric(
                                      horizontal: 7,
                                    ),
                                    child: Text(
                                      group.quantity.g,
                                      style:
                                          Theme.of(context).textTheme.titleMedium,
                                    ),
                                  ),
                                  IconButton.filled(
                                    onPressed: busy || editingLocked
                                        ? null
                                        : () => onPlus(group),
                                    icon: const Icon(Icons.add),
                                  ),
                                ],
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
              onPressed: busy || order == null || !order!.hasNewItems
                  ? null
                  : () async => onSend(),
              icon: const Icon(Icons.soup_kitchen_outlined),
              label: const Padding(
                padding: EdgeInsets.symmetric(vertical: 14),
                child: Text('Отправить на кухню'),
              ),
            ),
            const SizedBox(height: 10),
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
    required this.comment,
  });

  final String productId;
  final String productName;
  final double unitPrice;
  final String status;
  final String? comment;
  final List<OrderLineDto> lines = [];

  double get quantity => lines.fold(0, (sum, line) => sum + line.quantity);
  double get total => lines.fold(0, (sum, line) => sum + line.lineTotal);

  static List<CartGroup> fromOrder(OrderDto? order) {
    if (order == null) return const [];

    final map = <String, CartGroup>{};
    for (final line in order.items) {
      if (line.status == 'VOIDED') continue;

      final normalizedComment = line.comment?.trim();
      final key =
          '${line.productId}|${line.unitPrice}|${line.status}|'
          '${normalizedComment ?? ''}';

      final group = map.putIfAbsent(
        key,
        () => CartGroup(
          productId: line.productId,
          productName: line.productName,
          unitPrice: line.unitPrice,
          status: line.status,
          comment: normalizedComment == null || normalizedComment.isEmpty
              ? null
              : normalizedComment,
        ),
      );
      group.lines.add(line);
    }

    return map.values.toList();
  }
}

class _OrderItemStatusBadge extends StatelessWidget {
  const _OrderItemStatusBadge({required this.status});

  final String status;

  @override
  Widget build(BuildContext context) {
    final sent = status == 'SENT';
    final scheme = Theme.of(context).colorScheme;

    return Container(
      margin: const EdgeInsets.only(left: 8),
      padding: const EdgeInsets.symmetric(horizontal: 8, vertical: 3),
      decoration: BoxDecoration(
        color: sent
            ? scheme.secondaryContainer
            : scheme.surfaceContainerHighest,
        borderRadius: BorderRadius.circular(999),
      ),
      child: Text(
        sent ? 'КУХНЯ' : 'НОВОЕ',
        style: TextStyle(
          fontSize: 10,
          fontWeight: FontWeight.w800,
          color: sent
              ? scheme.onSecondaryContainer
              : scheme.onSurfaceVariant,
        ),
      ),
    );
  }
}

class _MoveOrderTarget {
  const _MoveOrderTarget({
    required this.hallName,
    required this.table,
  });

  final String hallName;
  final DiningTableDto table;
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
