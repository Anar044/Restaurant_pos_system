import 'dart:async';

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

class RestaurantRealtimeClient {
  RestaurantRealtimeClient(String baseUrl)
      : _hubUrl =
            '${baseUrl.replaceFirst(RegExp(r'/$'), '')}/hubs/restaurant';

  final String _hubUrl;
  final StreamController<RestaurantRealtimeEvent> _events =
      StreamController<RestaurantRealtimeEvent>.broadcast();

  HubConnection? _connection;
  Timer? _retryTimer;
  String? _token;
  bool _starting = false;
  bool _disposed = false;

  Stream<RestaurantRealtimeEvent> get events => _events.stream;

  Future<void> start(String token) async {
    if (_disposed) return;
    _token = token;
    await _connect();
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
      if (!_disposed) {
        _events.add(
          const RestaurantRealtimeEvent(
            resource: 'connection',
            operation: 'RECONNECTED',
          ),
        );
      }
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

    final connection = _connection;
    _connection = null;
    if (connection != null) {
      await connection.stop();
    }
  }

  Future<void> dispose() async {
    if (_disposed) return;
    _disposed = true;
    await stop();
    await _events.close();
  }
}
