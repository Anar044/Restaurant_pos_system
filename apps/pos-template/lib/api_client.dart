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

  Future<List<MenuCategory>> getMenu() async {
    final response = await _http.get(_uri('/api/v1/menu'), headers: _headers);
    final data = _decode(response);
    final categories = data['categories'] as List<dynamic>;
    return categories
        .map((e) => MenuCategory.fromJson(e as Map<String, dynamic>))
        .toList();
  }

  Future<OrderDto> createOrder({int guestCount = 1}) async {
    final response = await _http.post(
      _uri('/api/v1/orders'),
      headers: _headers,
      body: jsonEncode({'guestCount': guestCount}),
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
    required this.productName,
    required this.quantity,
    required this.unitPrice,
    required this.lineTotal,
  });
  final String id;
  final String productName;
  final double quantity;
  final double unitPrice;
  final double lineTotal;

  factory OrderLineDto.fromJson(Map<String, dynamic> json) => OrderLineDto(
        id: json['id'] as String,
        productName: json['productName'] as String,
        quantity: (json['quantity'] as num).toDouble(),
        unitPrice: (json['unitPrice'] as num).toDouble(),
        lineTotal: (json['lineTotal'] as num).toDouble(),
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
  });
  final String id;
  final int displayNumber;
  final String status;
  final double total;
  final int version;
  final List<OrderLineDto> items;

  factory OrderDto.fromJson(Map<String, dynamic> json) => OrderDto(
        id: json['id'] as String,
        displayNumber: (json['displayNumber'] as num).toInt(),
        status: json['status'] as String,
        total: (json['total'] as num).toDouble(),
        version: (json['version'] as num).toInt(),
        items: (json['items'] as List<dynamic>)
            .map((e) => OrderLineDto.fromJson(e as Map<String, dynamic>))
            .toList(),
      );
}
