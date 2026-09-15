import 'package:flutter/material.dart';

import 'api_client.dart';
import 'config.dart';

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
  AuthSession? session;

  @override
  Widget build(BuildContext context) {
    return MaterialApp(
      debugShowCheckedModeBanner: false,
      title: 'Restaurant POS',
      theme: ThemeData(
        colorScheme: ColorScheme.fromSeed(seedColor: const Color(0xFF243447)),
        useMaterial3: true,
      ),
      home: session == null
          ? LoginPage(
              api: api,
              onLoggedIn: (value) => setState(() => session = value),
            )
          : PosPage(api: api, session: session!),
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
          child: Padding(
            padding: const EdgeInsets.all(24),
            child: Column(
              mainAxisAlignment: MainAxisAlignment.center,
              children: [
                const Icon(Icons.point_of_sale_rounded, size: 64),
                const SizedBox(height: 16),
                Text('Restaurant POS', style: Theme.of(context).textTheme.headlineMedium),
                const SizedBox(height: 8),
                Text('Введите PIN', style: Theme.of(context).textTheme.titleMedium),
                const SizedBox(height: 18),
                Text(List.filled(pin.length, '•').join(' '), style: Theme.of(context).textTheme.headlineLarge),
                if (error != null) ...[
                  const SizedBox(height: 8),
                  Text(error!, style: TextStyle(color: Theme.of(context).colorScheme.error)),
                ],
                const SizedBox(height: 20),
                GridView.count(
                  shrinkWrap: true,
                  crossAxisCount: 3,
                  mainAxisSpacing: 10,
                  crossAxisSpacing: 10,
                  childAspectRatio: 1.6,
                  physics: const NeverScrollableScrollPhysics(),
                  children: [
                    for (final d in ['1','2','3','4','5','6','7','8','9'])
                      FilledButton.tonal(onPressed: () => digit(d), child: Text(d, style: const TextStyle(fontSize: 22))),
                    OutlinedButton(onPressed: () => setState(() => pin = ''), child: const Icon(Icons.clear)),
                    FilledButton.tonal(onPressed: () => digit('0'), child: const Text('0', style: TextStyle(fontSize: 22))),
                    FilledButton(onPressed: loading ? null : submit, child: loading ? const CircularProgressIndicator() : const Icon(Icons.arrow_forward)),
                  ],
                ),
                const SizedBox(height: 16),
                Text('API: ${AppConfig.apiBaseUrl}', style: Theme.of(context).textTheme.bodySmall),
              ],
            ),
          ),
        ),
      ),
    );
  }
}

class PosPage extends StatefulWidget {
  const PosPage({super.key, required this.api, required this.session});
  final PosApiClient api;
  final AuthSession session;

  @override
  State<PosPage> createState() => _PosPageState();
}

class _PosPageState extends State<PosPage> {
  late Future<List<MenuCategory>> menuFuture;
  OrderDto? order;
  bool mutating = false;
  String? error;

  @override
  void initState() {
    super.initState();
    menuFuture = widget.api.getMenu();
  }

  Future<void> ensureOrderAndAdd(MenuProduct product) async {
    if (mutating) return;
    setState(() {
      mutating = true;
      error = null;
    });
    try {
      var current = order ?? await widget.api.createOrder();
      current = await widget.api.addItem(current.id, product.id);
      setState(() => order = current);
    } catch (e) {
      setState(() => error = e.toString());
    } finally {
      if (mounted) setState(() => mutating = false);
    }
  }

  @override
  Widget build(BuildContext context) {
    return Scaffold(
      appBar: AppBar(
        title: const Text('POS'),
        actions: [
          Padding(
            padding: const EdgeInsets.symmetric(horizontal: 16),
            child: Center(child: Text('${widget.session.employeeName} · ${widget.session.roleName}')),
          ),
        ],
      ),
      body: FutureBuilder<List<MenuCategory>>(
        future: menuFuture,
        builder: (context, snapshot) {
          if (snapshot.connectionState != ConnectionState.done) {
            return const Center(child: CircularProgressIndicator());
          }
          if (snapshot.hasError) return Center(child: Text(snapshot.error.toString()));
          final categories = snapshot.data ?? const <MenuCategory>[];
          return LayoutBuilder(
            builder: (context, constraints) {
              final wide = constraints.maxWidth >= 900;
              final menu = MenuPane(categories: categories, onProduct: ensureOrderAndAdd);
              final cart = OrderPane(order: order, busy: mutating, error: error);
              return wide
                  ? Row(children: [Expanded(flex: 2, child: menu), SizedBox(width: 360, child: cart)])
                  : Column(children: [Expanded(child: menu), SizedBox(height: 220, child: cart)]);
            },
          );
        },
      ),
    );
  }
}

class MenuPane extends StatelessWidget {
  const MenuPane({super.key, required this.categories, required this.onProduct});
  final List<MenuCategory> categories;
  final ValueChanged<MenuProduct> onProduct;

  @override
  Widget build(BuildContext context) {
    final products = categories.expand((c) => c.products).toList();
    return Padding(
      padding: const EdgeInsets.all(16),
      child: GridView.builder(
        gridDelegate: const SliverGridDelegateWithMaxCrossAxisExtent(
          maxCrossAxisExtent: 220,
          childAspectRatio: 1.35,
          crossAxisSpacing: 12,
          mainAxisSpacing: 12,
        ),
        itemCount: products.length,
        itemBuilder: (context, index) {
          final p = products[index];
          return Card(
            child: InkWell(
              borderRadius: BorderRadius.circular(12),
              onTap: () => onProduct(p),
              child: Padding(
                padding: const EdgeInsets.all(14),
                child: Column(
                  crossAxisAlignment: CrossAxisAlignment.start,
                  children: [
                    Expanded(child: Text(p.name, style: Theme.of(context).textTheme.titleMedium)),
                    Text('${p.price.toStringAsFixed(2)} ${p.currencyCode}', style: Theme.of(context).textTheme.titleSmall),
                  ],
                ),
              ),
            ),
          );
        },
      ),
    );
  }
}

class OrderPane extends StatelessWidget {
  const OrderPane({super.key, required this.order, required this.busy, required this.error});
  final OrderDto? order;
  final bool busy;
  final String? error;

  @override
  Widget build(BuildContext context) {
    return Material(
      elevation: 2,
      child: Padding(
        padding: const EdgeInsets.all(16),
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.stretch,
          children: [
            Row(
              children: [
                Text(order == null ? 'Новый заказ' : 'Заказ #${order!.displayNumber}', style: Theme.of(context).textTheme.titleLarge),
                const Spacer(),
                if (busy) const SizedBox(width: 20, height: 20, child: CircularProgressIndicator(strokeWidth: 2)),
              ],
            ),
            if (error != null) Text(error!, style: TextStyle(color: Theme.of(context).colorScheme.error)),
            const Divider(),
            Expanded(
              child: order == null || order!.items.isEmpty
                  ? const Center(child: Text('Нажмите блюдо, чтобы начать заказ'))
                  : ListView.builder(
                      itemCount: order!.items.length,
                      itemBuilder: (context, index) {
                        final item = order!.items[index];
                        return ListTile(
                          dense: true,
                          contentPadding: EdgeInsets.zero,
                          title: Text(item.productName),
                          subtitle: Text('${item.quantity.g} × ${item.unitPrice.toStringAsFixed(2)}'),
                          trailing: Text(item.lineTotal.toStringAsFixed(2)),
                        );
                      },
                    ),
            ),
            const Divider(),
            Row(
              children: [
                const Text('Итого'),
                const Spacer(),
                Text((order?.total ?? 0).toStringAsFixed(2), style: Theme.of(context).textTheme.headlineSmall),
              ],
            ),
          ],
        ),
      ),
    );
  }
}

extension PrettyNum on double {
  String get g => this == roundToDouble() ? toInt().toString() : toString();
}
