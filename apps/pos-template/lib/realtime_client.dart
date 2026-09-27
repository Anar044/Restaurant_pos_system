import 'dart:async';
import 'dart:math';

import 'package:signalr_netcore/signalr_client.dart';

class RestaurantRealtimeEvent {
  const RestaurantRealtimeEvent({
    required this.resource,
    required this.operation,
    this.orderId,
    this.utc,
  });

  final String resource;
  final String operation;
  final String? orderId;
  final DateTime? utc;
}

class OrderEditLockResult {
  const OrderEditLockResult({
    required this.acquired,
    this.tableId,
    this.orderId,
    this.deviceId,
    this.deviceName,
    this.employeeName,
    this.expiresAt,
    this.error,
  });

  final bool acquired;
  final String? tableId;
  final String? orderId;
  final String? deviceId;
  final String? deviceName;
  final String? employeeName;
  final DateTime? expiresAt;
  final String? error;

  factory OrderEditLockResult.fromObject(Object? value) {
    if (value is! Map) {
      return const OrderEditLockResult(
        acquired: false,
        error: 'Сервер вернул некорректный ответ блокировки.',
      );
    }

    final map = <String, dynamic>{
      for (final entry in value.entries) entry.key.toString(): entry.value,
    };

    return OrderEditLockResult(
      acquired: map['acquired'] as bool? ?? false,
      tableId: map['tableId']?.toString(),
      orderId: map['orderId']?.toString(),
      deviceId: map['deviceId']?.toString(),
      deviceName: map['deviceName']?.toString(),
      employeeName: map['employeeName']?.toString(),
      expiresAt: map['expiresAt'] == null
          ? null
          : DateTime.tryParse(map['expiresAt'].toString()),
    );
  }

  factory OrderEditLockResult.unavailable(Object error) =>
      OrderEditLockResult(
        acquired: false,
        error: 'Не удалось получить блокировку стола: $error',
      );
}

class RestaurantRealtimeClient {
  RestaurantRealtimeClient(
    String baseUrl, {
    required this.deviceId,
    String? instanceId,
  })  : instanceId = instanceId ?? _createInstanceId(),
        _hubUrl =
            '${baseUrl.replaceFirst(RegExp(r'/$'), '')}/hubs/restaurant';

  final String _hubUrl;
  final String deviceId;
  final String instanceId;

  final StreamController<RestaurantRealtimeEvent> _events =
      StreamController<RestaurantRealtimeEvent>.broadcast();

  HubConnection? _connection;
  Timer? _retryTimer;
  Timer? _leaseTimer;
  String? _token;
  String? _editingTableId;
  String? _editingOrderId;
  bool _starting = false;
  bool _disposed = false;

  Stream<RestaurantRealtimeEvent> get events => _events.stream;

  Future<void> start(String token) async {
    if (_disposed) return;
    _token = token;
    await _connect();
  }

  Future<OrderEditLockResult> beginEditingTable(
    String tableId,
    String? orderId,
  ) async {
    if (_disposed) {
      return const OrderEditLockResult(
        acquired: false,
        error: 'Realtime-клиент уже остановлен.',
      );
    }

    await _connect();

    final connection = _connection;
    if (connection?.state != HubConnectionState.Connected) {
      return const OrderEditLockResult(
        acquired: false,
        error: 'Нет realtime-соединения с Restaurant Node.',
      );
    }

    try {
      final value = await connection!.invoke(
        'BeginEditingTable',
        args: <Object>[
          tableId,
          orderId ?? '',
          deviceId,
          instanceId,
        ],
      );

      final result = OrderEditLockResult.fromObject(value);
      if (result.acquired) {
        _editingTableId = tableId;
        _editingOrderId = orderId;
        _startLeaseRenewal();
      }
      return result;
    } catch (e) {
      return OrderEditLockResult.unavailable(e);
    }
  }

  Future<void> endEditingTable(String tableId) async {
    if (_editingTableId != tableId) return;

    _leaseTimer?.cancel();
    _leaseTimer = null;
    _editingTableId = null;
    _editingOrderId = null;

    final connection = _connection;
    if (connection?.state != HubConnectionState.Connected) return;

    try {
      await connection!.invoke(
        'EndEditingTable',
        args: <Object>[tableId, instanceId],
      );
    } catch (_) {
      // The server also releases locks automatically when the connection drops.
    }
  }

  Future<void> _connect() async {
    if (_disposed || _starting || _token == null) return;

    final existing = _connection;
    if (existing?.state == HubConnectionState.Connected ||
        existing?.state == HubConnectionState.Connecting ||
        existing?.state == HubConnectionState.Reconnecting) {
      return;
    }

    _starting = true;
    _retryTimer?.cancel();

    final options = HttpConnectionOptions(
      accessTokenFactory: () async => _token!,
      requestTimeout: 5000,
    );

    final connection = HubConnectionBuilder()
        .withUrl(_hubUrl, options: options)
        .withAutomaticReconnect(
          retryDelays: const [0, 2000, 5000, 10000, 30000],
        )
        .build();

    connection.on('RestaurantChanged', _onRestaurantChanged);
    connection.onreconnected(({String? connectionId}) {
      if (_disposed) return;

      _events.add(
        const RestaurantRealtimeEvent(
          resource: 'connection',
          operation: 'RECONNECTED',
        ),
      );

      unawaited(_renewEditingTable());
    });
    connection.onclose(({Exception? error}) {
      if (_connection == connection && !_disposed) {
        _scheduleReconnect();
      }
    });

    _connection = connection;

    try {
      final startFuture = connection.start();
      if (startFuture != null) {
        await startFuture;
      }
    } catch (_) {
      if (_connection == connection && !_disposed) {
        _scheduleReconnect();
      }
    } finally {
      _starting = false;
    }
  }

  void _onRestaurantChanged(List<Object?>? arguments) {
    if (_disposed || arguments == null || arguments.isEmpty) return;

    final value = arguments.first;
    if (value is! Map) return;

    final map = <String, dynamic>{
      for (final entry in value.entries) entry.key.toString(): entry.value,
    };

    _events.add(
      RestaurantRealtimeEvent(
        resource: map['resource']?.toString() ?? 'unknown',
        operation: map['operation']?.toString() ?? 'CHANGED',
        orderId: map['orderId']?.toString(),
        utc: map['utc'] == null
            ? null
            : DateTime.tryParse(map['utc'].toString()),
      ),
    );
  }

  void _startLeaseRenewal() {
    _leaseTimer?.cancel();
    _leaseTimer = Timer.periodic(const Duration(seconds: 20), (_) {
      unawaited(_renewEditingTable());
    });
  }

  Future<void> _renewEditingTable() async {
    final tableId = _editingTableId;
    if (_disposed || tableId == null) return;

    final connection = _connection;
    if (connection?.state != HubConnectionState.Connected) return;

    try {
      final value = await connection!.invoke(
        'BeginEditingTable',
        args: <Object>[
          tableId,
          _editingOrderId ?? '',
          deviceId,
          instanceId,
        ],
      );

      final result = OrderEditLockResult.fromObject(value);
      if (!result.acquired) {
        _leaseTimer?.cancel();
        _leaseTimer = null;
        _editingTableId = null;
        _editingOrderId = null;

        _events.add(
          const RestaurantRealtimeEvent(
            resource: 'edit-lock',
            operation: 'LOST',
          ),
        );
      }
    } catch (_) {
      // Keep the local editing state while SignalR reconnects.
      // The server lease will expire if this client never reconnects.
    }
  }

  void _scheduleReconnect() {
    if (_disposed || _token == null) return;
    _retryTimer?.cancel();
    _retryTimer = Timer(const Duration(seconds: 5), () {
      unawaited(_connect());
    });
  }

  Future<void> stop() async {
    _retryTimer?.cancel();
    _retryTimer = null;
    _leaseTimer?.cancel();
    _leaseTimer = null;

    final connection = _connection;
    _connection = null;
    if (connection != null) {
      await connection.stop();
    }
  }

  Future<void> dispose() async {
    if (_disposed) return;

    final tableId = _editingTableId;
    if (tableId != null) {
      await endEditingTable(tableId);
    }

    _disposed = true;
    await stop();
    await _events.close();
  }

  static String _createInstanceId() {
    final random = Random.secure();
    final suffix = List<int>.generate(4, (_) => random.nextInt(256))
        .map((value) => value.toRadixString(16).padLeft(2, '0'))
        .join();

    return '${DateTime.now().microsecondsSinceEpoch}-$suffix';
  }
}
