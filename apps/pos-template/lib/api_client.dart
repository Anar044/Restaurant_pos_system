import 'dart:convert';

import 'package:http/http.dart' as http;

class ApiException implements Exception {
  ApiException(this.message, [this.statusCode]);
  final String message;
  final int? statusCode;

  @override
  String toString() => message;
}

class AuthSession {
  const AuthSession({
    required this.token,
    required this.employeeId,
    required this.employeeName,
    required this.roleName,
    required this.restaurantId,
  });

  final String token;
  final String employeeId;
  final String employeeName;
  final String roleName;
  final String restaurantId;
}

class PosApiClient {
  PosApiClient(this.baseUrl, {http.Client? httpClient})
      : _http = httpClient ?? http.Client();

  final String baseUrl;
  final http.Client _http;
  AuthSession? session;

  Uri _uri(String path) => Uri.parse('$baseUrl$path');

  Map<String, String> get _headers => {
        'Content-Type': 'application/json',
        if (session != null) 'Authorization': 'Bearer ${session!.token}',
      };

  Future<AuthSession> loginWithPin(String restaurantId, String pin) async {
    final response = await _http.post(
      _uri('/api/v1/auth/pin'),
      headers: {'Content-Type': 'application/json'},
      body: jsonEncode({'restaurantId': restaurantId, 'pin': pin}),
    );
    final data = _decode(response);
    final auth = AuthSession(
      token: data['token'] as String,
      employeeId: data['employeeId'] as String,
      employeeName: data['employeeName'] as String,
      roleName: data['roleName'] as String,
      restaurantId: data['restaurantId'] as String,
    );
    session = auth;
    return auth;
  }

  Future<List<HallDto>> getHalls() async {
    final response = await _http.get(_uri('/api/v1/halls'), headers: _headers);
    final data = _decode(response);
    return (data['halls'] as List<dynamic>)
        .map((e) => HallDto.fromJson(e as Map<String, dynamic>))
        .toList();
  }

  Future<List<MenuCategory>> getMenu() async {
    final response = await _http.get(_uri('/api/v1/menu'), headers: _headers);
    final data = _decode(response);
    return (data['categories'] as List<dynamic>)
        .map((e) => MenuCategory.fromJson(e as Map<String, dynamic>))
        .toList();
  }

  Future<OrderDto> getOrder(String orderId) async {
    final response = await _http.get(
      _uri('/api/v1/orders/$orderId'),
      headers: _headers,
    );
    return OrderDto.fromJson(_decode(response));
  }

  Future<OrderDto> createOrder({int guestCount = 1, String? tableId}) async {
    final response = await _http.post(
      _uri('/api/v1/orders'),
      headers: _headers,
      body: jsonEncode({
        'guestCount': guestCount,
        if (tableId != null) 'tableId': tableId,
      }),
    );
    return OrderDto.fromJson(_decode(response));
  }

  Future<OrderDto> addItem(String orderId, String productId) async {
    final response = await _http.post(
      _uri('/api/v1/orders/$orderId/items'),
      headers: _headers,
      body: jsonEncode({'productId': productId, 'quantity': 1}),
    );
    return OrderDto.fromJson(_decode(response));
  }

  Future<OrderDto> deleteItem(String orderId, String itemId) async {
    final response = await _http.delete(
      _uri('/api/v1/orders/$orderId/items/$itemId'),
      headers: _headers,
    );
    return OrderDto.fromJson(_decode(response));
  }

  Future<OrderDto> sendOrderToKitchen(String orderId) async {
    final response = await _http.post(
      _uri('/api/v1/orders/$orderId/send'),
      headers: _headers,
    );
    return OrderDto.fromJson(_decode(response));
  }

  Map<String, dynamic> _decode(http.Response response) {
    Map<String, dynamic> data = {};
    if (response.body.isNotEmpty) {
      final decoded = jsonDecode(utf8.decode(response.bodyBytes));
      if (decoded is Map<String, dynamic>) data = decoded;
    }
    if (response.statusCode < 200 || response.statusCode >= 300) {
      throw ApiException(
        data['message'] as String? ?? 'Request failed (${response.statusCode})',
        response.statusCode,
      );
    }
    return data;
  }
}

class HallDto {
  const HallDto({required this.id, required this.name, required this.tables});
  final String id;
  final String name;
  final List<DiningTableDto> tables;

  factory HallDto.fromJson(Map<String, dynamic> json) => HallDto(
        id: json['id'] as String,
        name: json['name'] as String,
        tables: (json['tables'] as List<dynamic>)
            .map((e) => DiningTableDto.fromJson(e as Map<String, dynamic>))
            .toList(),
      );
}

class DiningTableDto {
  const DiningTableDto({
    required this.id,
    required this.name,
    required this.seats,
    required this.occupied,
    this.openOrder,
  });

  final String id;
  final String name;
  final int seats;
  final bool occupied;
  final OpenOrderSummary? openOrder;

  factory DiningTableDto.fromJson(Map<String, dynamic> json) => DiningTableDto(
        id: json['id'] as String,
        name: json['name'] as String,
        seats: (json['seats'] as num).toInt(),
        occupied: json['occupied'] as bool? ?? false,
        openOrder: json['openOrder'] == null
            ? null
            : OpenOrderSummary.fromJson(
                json['openOrder'] as Map<String, dynamic>,
              ),
      );
}

class OpenOrderSummary {
  const OpenOrderSummary({
    required this.id,
    required this.displayNumber,
    required this.status,
    required this.total,
    required this.guestCount,
  });

  final String id;
  final int displayNumber;
  final String status;
  final double total;
  final int guestCount;

  factory OpenOrderSummary.fromJson(Map<String, dynamic> json) =>
      OpenOrderSummary(
        id: json['id'] as String,
        displayNumber: (json['displayNumber'] as num).toInt(),
        status: json['status'] as String,
        total: (json['total'] as num).toDouble(),
        guestCount: (json['guestCount'] as num).toInt(),
      );
}

class MenuCategory {
  const MenuCategory({required this.id, required this.name, required this.products});
  final String id;
  final String name;
  final List<MenuProduct> products;

  factory MenuCategory.fromJson(Map<String, dynamic> json) => MenuCategory(
        id: json['id'] as String,
        name: json['name'] as String,
        products: (json['products'] as List<dynamic>)
            .map((e) => MenuProduct.fromJson(e as Map<String, dynamic>))
            .toList(),
      );
}

class MenuProduct {
  const MenuProduct({
    required this.id,
    required this.name,
    required this.price,
    required this.currencyCode,
  });
  final String id;
  final String name;
  final double price;
  final String currencyCode;

  factory MenuProduct.fromJson(Map<String, dynamic> json) => MenuProduct(
        id: json['id'] as String,
        name: json['name'] as String,
        price: (json['price'] as num).toDouble(),
        currencyCode: json['currencyCode'] as String,
      );
}

class OrderLineDto {
  const OrderLineDto({
    required this.id,
    required this.productId,
    required this.productName,
    required this.quantity,
    required this.unitPrice,
    required this.lineTotal,
    required this.status,
    this.sentAt,
  });

  final String id;
  final String productId;
  final String productName;
  final double quantity;
  final double unitPrice;
  final double lineTotal;
  final String status;
  final DateTime? sentAt;

  factory OrderLineDto.fromJson(Map<String, dynamic> json) => OrderLineDto(
        id: json['id'] as String,
        productId: json['productId'] as String,
        productName: json['productName'] as String,
        quantity: (json['quantity'] as num).toDouble(),
        unitPrice: (json['unitPrice'] as num).toDouble(),
        lineTotal: (json['lineTotal'] as num).toDouble(),
        status: json['status'] as String,
        sentAt: json['sentAt'] == null
            ? null
            : DateTime.tryParse(json['sentAt'] as String),
      );
}

class OrderDto {
  const OrderDto({
    required this.id,
    required this.displayNumber,
    required this.status,
    required this.total,
    required this.version,
    required this.items,
    required this.guestCount,
    this.tableId,
  });

  final String id;
  final int displayNumber;
  final String status;
  final String? tableId;
  final int guestCount;
  final double total;
  final int version;
  final List<OrderLineDto> items;

  bool get hasNewItems => items.any((item) => item.status == 'NEW');

  factory OrderDto.fromJson(Map<String, dynamic> json) => OrderDto(
        id: json['id'] as String,
        displayNumber: (json['displayNumber'] as num).toInt(),
        status: json['status'] as String,
        tableId: json['tableId'] as String?,
        guestCount: (json['guestCount'] as num).toInt(),
        total: (json['total'] as num).toDouble(),
        version: (json['version'] as num).toInt(),
        items: (json['items'] as List<dynamic>)
            .map((e) => OrderLineDto.fromJson(e as Map<String, dynamic>))
            .toList(),
      );
}
