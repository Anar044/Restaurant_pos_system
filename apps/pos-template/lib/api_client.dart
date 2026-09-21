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
    required this.permissions,
  });

  final String token;
  final String employeeId;
  final String employeeName;
  final String roleName;
  final String restaurantId;
  final Set<String> permissions;

  bool hasPermission(String permission) => permissions.contains(permission);
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
      permissions: ((data['permissions'] as List<dynamic>?) ?? const [])
          .map((value) => value.toString())
          .toSet(),
    );
    session = auth;
    return auth;
  }

  Future<ShiftDto?> getCurrentShift(String deviceId) async {
    final response = await _http.get(
      _uri('/api/v1/shifts/current?deviceId=${Uri.encodeQueryComponent(deviceId)}'),
      headers: _headers,
    );
    final data = _decode(response);
    final shift = data['shift'];
    return shift == null
        ? null
        : ShiftDto.fromJson(shift as Map<String, dynamic>);
  }

  Future<ShiftDto> openShift(
    String deviceId, {
    double openingCash = 0,
  }) async {
    final response = await _http.post(
      _uri('/api/v1/shifts/open'),
      headers: _headers,
      body: jsonEncode({
        'deviceId': deviceId,
        'openingCash': openingCash,
      }),
    );
    return ShiftDto.fromJson(_decode(response));
  }

  Future<CloseShiftResult> closeShift(
    String shiftId, {
    required double closingCash,
  }) async {
    final response = await _http.post(
      _uri('/api/v1/shifts/$shiftId/close'),
      headers: _headers,
      body: jsonEncode({'closingCash': closingCash}),
    );
    return CloseShiftResult.fromJson(_decode(response));
  }

  Future<ShiftReportDto> getShiftReport(String shiftId) async {
    final response = await _http.get(
      _uri('/api/v1/shifts/$shiftId/report'),
      headers: _headers,
    );
    return ShiftReportDto.fromJson(_decode(response));
  }

  Future<void> addCashTransaction({
    required String shiftId,
    required String type,
    required double amount,
    required String reason,
  }) async {
    final response = await _http.post(
      _uri('/api/v1/shifts/$shiftId/cash-transactions'),
      headers: _headers,
      body: jsonEncode({
        'type': type,
        'amount': amount,
        'reason': reason,
      }),
    );
    _decode(response);
  }

  Future<PaymentRefundResultDto> refundPayment({
    required String paymentId,
    required String shiftId,
    required double amount,
    required String reason,
  }) async {
    final response = await _http.post(
      _uri('/api/v1/payments/$paymentId/refund'),
      headers: _headers,
      body: jsonEncode({
        'shiftId': shiftId,
        'amount': amount,
        'reason': reason,
      }),
    );
    return PaymentRefundResultDto.fromJson(_decode(response));
  }

  Future<OrderPaymentsDto> getOrderPayments(String orderId) async {
    final response = await _http.get(
      _uri('/api/v1/payments/order/$orderId'),
      headers: _headers,
    );
    return OrderPaymentsDto.fromJson(_decode(response));
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

  Future<List<OrderHistoryItemDto>> getOrderHistory({
    String? shiftId,
    int? orderNumber,
    int take = 200,
  }) async {
    final query = <String, String>{
      'take': take.toString(),
      if (shiftId != null && shiftId.isNotEmpty) 'shiftId': shiftId,
      if (orderNumber != null) 'orderNumber': orderNumber.toString(),
    };
    final uri = _uri('/api/v1/orders/history').replace(queryParameters: query);
    final response = await _http.get(uri, headers: _headers);
    final data = _decode(response);
    return ((data['orders'] as List<dynamic>?) ?? const [])
        .map((e) => OrderHistoryItemDto.fromJson(e as Map<String, dynamic>))
        .toList();
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

  Future<OrderDto> addItem(
    String orderId,
    String productId, {
    String? comment,
    List<ModifierSelectionDto> modifiers = const [],
  }) async {
    final response = await _http.post(
      _uri('/api/v1/orders/$orderId/items'),
      headers: _headers,
      body: jsonEncode({
        'productId': productId,
        'quantity': 1,
        if (comment != null && comment.trim().isNotEmpty)
          'comment': comment.trim(),
        'modifiers': modifiers.map((item) => item.toJson()).toList(),
      }),
    );
    return OrderDto.fromJson(_decode(response));
  }

  Future<OrderDto> updateGuestCount(String orderId, int guestCount) async {
    final response = await _http.put(
      _uri('/api/v1/orders/$orderId/guest-count'),
      headers: _headers,
      body: jsonEncode({'guestCount': guestCount}),
    );
    return OrderDto.fromJson(_decode(response));
  }

  Future<OrderDto> moveOrder(String orderId, String tableId) async {
    final response = await _http.put(
      _uri('/api/v1/orders/$orderId/table'),
      headers: _headers,
      body: jsonEncode({'tableId': tableId}),
    );
    return OrderDto.fromJson(_decode(response));
  }

  Future<TransferOrderItemsResultDto> transferOrderItems({
    required String orderId,
    required String targetTableId,
    required List<String> itemIds,
  }) async {
    final response = await _http.post(
      _uri('/api/v1/orders/$orderId/transfer-items'),
      headers: _headers,
      body: jsonEncode({
        'targetTableId': targetTableId,
        'itemIds': itemIds,
      }),
    );
    return TransferOrderItemsResultDto.fromJson(_decode(response));
  }

  Future<OrderDto> updateItemComment(
    String orderId,
    String itemId,
    String? comment,
  ) async {
    final response = await _http.put(
      _uri('/api/v1/orders/$orderId/items/$itemId/comment'),
      headers: _headers,
      body: jsonEncode({'comment': comment}),
    );
    return OrderDto.fromJson(_decode(response));
  }
  Future<OrderDto> updateItemModifiers(
    String orderId,
    String itemId,
    List<ModifierSelectionDto> modifiers,
  ) async {
    final response = await _http.put(
      _uri('/api/v1/orders/$orderId/items/$itemId/modifiers'),
      headers: _headers,
      body: jsonEncode({
        'modifiers': modifiers.map((item) => item.toJson()).toList(),
      }),
    );
    return OrderDto.fromJson(_decode(response));
  }


  Future<OrderDto> voidItem(
    String orderId,
    String itemId,
    String reason,
  ) async {
    final response = await _http.post(
      _uri('/api/v1/orders/$orderId/items/$itemId/void'),
      headers: _headers,
      body: jsonEncode({'reason': reason}),
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

  Future<PaymentResultDto> payOrder({
    required String orderId,
    required String shiftId,
    required String method,
    required double amount,
    double? tenderedAmount,
    String? providerReference,
  }) async {
    final response = await _http.post(
      _uri('/api/v1/payments'),
      headers: _headers,
      body: jsonEncode({
        'orderId': orderId,
        'shiftId': shiftId,
        'method': method,
        'amount': amount,
        if (tenderedAmount != null) 'tenderedAmount': tenderedAmount,
        if (providerReference != null && providerReference.trim().isNotEmpty)
          'providerReference': providerReference.trim(),
      }),
    );
    return PaymentResultDto.fromJson(_decode(response));
  }

  Future<OrderDto> closeOrder(String orderId) async {
    final response = await _http.post(
      _uri('/api/v1/orders/$orderId/close'),
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

class ShiftDto {
  const ShiftDto({
    required this.id,
    required this.deviceId,
    required this.status,
    required this.openingCash,
    required this.openedAt,
    this.closingCash,
    this.closedAt,
  });

  final String id;
  final String deviceId;
  final String status;
  final double openingCash;
  final double? closingCash;
  final DateTime openedAt;
  final DateTime? closedAt;

  factory ShiftDto.fromJson(Map<String, dynamic> json) => ShiftDto(
        id: json['id'] as String,
        deviceId: json['deviceId'] as String,
        status: json['status'] as String,
        openingCash: (json['openingCash'] as num).toDouble(),
        closingCash: (json['closingCash'] as num?)?.toDouble(),
        openedAt: DateTime.parse(json['openedAt'] as String),
        closedAt: json['closedAt'] == null
            ? null
            : DateTime.tryParse(json['closedAt'] as String),
      );
}

class CloseShiftResult {
  const CloseShiftResult({
    required this.shift,
    required this.expectedCash,
    required this.difference,
    this.report,
  });

  final ShiftDto shift;
  final double expectedCash;
  final double difference;
  final ShiftReportDto? report;

  factory CloseShiftResult.fromJson(Map<String, dynamic> json) =>
      CloseShiftResult(
        shift: ShiftDto.fromJson(json['shift'] as Map<String, dynamic>),
        expectedCash: (json['expectedCash'] as num).toDouble(),
        difference: (json['difference'] as num?)?.toDouble() ?? 0,
        report: json['report'] == null
            ? null
            : ShiftReportDto.fromJson(json['report'] as Map<String, dynamic>),
      );
}

class ShiftPaymentTotalDto {
  const ShiftPaymentTotalDto({
    required this.method,
    required this.gross,
    required this.refunds,
    required this.net,
  });

  final String method;
  final double gross;
  final double refunds;
  final double net;

  factory ShiftPaymentTotalDto.fromJson(Map<String, dynamic> json) =>
      ShiftPaymentTotalDto(
        method: json['method'] as String,
        gross: (json['gross'] as num).toDouble(),
        refunds: (json['refunds'] as num).toDouble(),
        net: (json['net'] as num).toDouble(),
      );
}

class ShiftReportDto {
  const ShiftReportDto({
    required this.shiftId,
    required this.deviceId,
    required this.status,
    required this.openedAt,
    required this.openingCash,
    required this.expectedCash,
    required this.ordersCount,
    required this.paymentsCount,
    required this.grossSales,
    required this.refunds,
    required this.netSales,
    required this.deposits,
    required this.withdrawals,
    required this.payments,
    this.closedAt,
    this.closingCash,
    this.cashDifference,
  });

  final String shiftId;
  final String deviceId;
  final String status;
  final DateTime openedAt;
  final DateTime? closedAt;
  final double openingCash;
  final double? closingCash;
  final double expectedCash;
  final double? cashDifference;
  final int ordersCount;
  final int paymentsCount;
  final double grossSales;
  final double refunds;
  final double netSales;
  final double deposits;
  final double withdrawals;
  final List<ShiftPaymentTotalDto> payments;

  factory ShiftReportDto.fromJson(Map<String, dynamic> json) => ShiftReportDto(
        shiftId: json['shiftId'] as String,
        deviceId: json['deviceId'] as String,
        status: json['status'] as String,
        openedAt: DateTime.parse(json['openedAt'] as String),
        closedAt: json['closedAt'] == null
            ? null
            : DateTime.tryParse(json['closedAt'] as String),
        openingCash: (json['openingCash'] as num).toDouble(),
        closingCash: (json['closingCash'] as num?)?.toDouble(),
        expectedCash: (json['expectedCash'] as num).toDouble(),
        cashDifference: (json['cashDifference'] as num?)?.toDouble(),
        ordersCount: (json['ordersCount'] as num).toInt(),
        paymentsCount: (json['paymentsCount'] as num).toInt(),
        grossSales: (json['grossSales'] as num).toDouble(),
        refunds: (json['refunds'] as num).toDouble(),
        netSales: (json['netSales'] as num).toDouble(),
        deposits: (json['deposits'] as num).toDouble(),
        withdrawals: (json['withdrawals'] as num).toDouble(),
        payments: ((json['payments'] as List<dynamic>?) ?? const [])
            .map((e) => ShiftPaymentTotalDto.fromJson(e as Map<String, dynamic>))
            .toList(),
      );
}

class OrderPaymentsDto {
  const OrderPaymentsDto({
    required this.payments,
    required this.refunds,
    required this.grossTotal,
    required this.refundedTotal,
    required this.netTotal,
  });

  final List<PaymentDto> payments;
  final List<PaymentRefundDto> refunds;
  final double grossTotal;
  final double refundedTotal;
  final double netTotal;

  factory OrderPaymentsDto.fromJson(Map<String, dynamic> json) =>
      OrderPaymentsDto(
        payments: ((json['payments'] as List<dynamic>?) ?? const [])
            .map((e) => PaymentDto.fromJson(e as Map<String, dynamic>))
            .toList(),
        refunds: ((json['refunds'] as List<dynamic>?) ?? const [])
            .map((e) => PaymentRefundDto.fromJson(e as Map<String, dynamic>))
            .toList(),
        grossTotal: (json['grossTotal'] as num?)?.toDouble() ?? 0,
        refundedTotal: (json['refundedTotal'] as num?)?.toDouble() ?? 0,
        netTotal: (json['netTotal'] as num?)?.toDouble() ?? 0,
      );
}

class PaymentRefundDto {
  const PaymentRefundDto({
    required this.id,
    required this.paymentId,
    required this.orderId,
    required this.shiftId,
    required this.method,
    required this.amount,
    required this.currencyCode,
    required this.reason,
    required this.createdAt,
  });

  final String id;
  final String paymentId;
  final String orderId;
  final String shiftId;
  final String method;
  final double amount;
  final String currencyCode;
  final String? reason;
  final DateTime createdAt;

  factory PaymentRefundDto.fromJson(Map<String, dynamic> json) =>
      PaymentRefundDto(
        id: json['id'] as String,
        paymentId: json['paymentId'] as String,
        orderId: json['orderId'] as String,
        shiftId: json['shiftId'] as String,
        method: json['method'] as String,
        amount: (json['amount'] as num).toDouble(),
        currencyCode: json['currencyCode'] as String,
        reason: json['reason'] as String?,
        createdAt: DateTime.parse(json['createdAt'] as String),
      );
}

class PaymentRefundResultDto {
  const PaymentRefundResultDto({
    required this.refund,
    required this.payment,
    required this.refundable,
  });

  final PaymentRefundDto refund;
  final PaymentDto payment;
  final double refundable;

  factory PaymentRefundResultDto.fromJson(Map<String, dynamic> json) =>
      PaymentRefundResultDto(
        refund: PaymentRefundDto.fromJson(
          json['refund'] as Map<String, dynamic>,
        ),
        payment: PaymentDto.fromJson(
          json['payment'] as Map<String, dynamic>,
        ),
        refundable: (json['refundable'] as num).toDouble(),
      );
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
  const MenuCategory({
    required this.id,
    required this.name,
    required this.products,
  });

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

class ModifierSelectionDto {
  const ModifierSelectionDto({
    required this.groupId,
    required this.modifierId,
    this.quantity = 1,
  });

  final String groupId;
  final String modifierId;
  final double quantity;

  Map<String, dynamic> toJson() => {
        'groupId': groupId,
        'modifierId': modifierId,
        'quantity': quantity,
      };
}

class MenuModifierOption {
  const MenuModifierOption({
    required this.id,
    required this.name,
    required this.priceDelta,
  });

  final String id;
  final String name;
  final double priceDelta;

  factory MenuModifierOption.fromJson(Map<String, dynamic> json) =>
      MenuModifierOption(
        id: json['id'] as String,
        name: json['name'] as String,
        priceDelta: (json['priceDelta'] as num?)?.toDouble() ?? 0,
      );
}

class MenuModifierGroup {
  const MenuModifierGroup({
    required this.id,
    required this.name,
    required this.minSelections,
    required this.maxSelections,
    required this.isRequired,
    required this.modifiers,
  });

  final String id;
  final String name;
  final int minSelections;
  final int maxSelections;
  final bool isRequired;
  final List<MenuModifierOption> modifiers;

  factory MenuModifierGroup.fromJson(Map<String, dynamic> json) =>
      MenuModifierGroup(
        id: json['id'] as String,
        name: json['name'] as String,
        minSelections: (json['minSelections'] as num?)?.toInt() ?? 0,
        maxSelections: (json['maxSelections'] as num?)?.toInt() ?? 1,
        isRequired: json['isRequired'] as bool? ?? false,
        modifiers: ((json['modifiers'] as List<dynamic>?) ?? const [])
            .map((e) => MenuModifierOption.fromJson(e as Map<String, dynamic>))
            .toList(),
      );
}

class MenuProduct {
  const MenuProduct({
    required this.id,
    required this.name,
    required this.price,
    required this.currencyCode,
    required this.modifierGroups,
  });

  final String id;
  final String name;
  final double price;
  final String currencyCode;
  final List<MenuModifierGroup> modifierGroups;

  bool get hasModifiers => modifierGroups.isNotEmpty;

  factory MenuProduct.fromJson(Map<String, dynamic> json) => MenuProduct(
        id: json['id'] as String,
        name: json['name'] as String,
        price: (json['price'] as num).toDouble(),
        currencyCode: json['currencyCode'] as String,
        modifierGroups: ((json['modifierGroups'] as List<dynamic>?) ?? const [])
            .map((e) => MenuModifierGroup.fromJson(e as Map<String, dynamic>))
            .toList(),
      );
}

class OrderLineModifierDto {
  const OrderLineModifierDto({
    required this.id,
    required this.modifierId,
    required this.name,
    required this.quantity,
    required this.priceDelta,
    required this.total,
  });

  final String id;
  final String modifierId;
  final String name;
  final double quantity;
  final double priceDelta;
  final double total;

  factory OrderLineModifierDto.fromJson(Map<String, dynamic> json) =>
      OrderLineModifierDto(
        id: json['id'] as String,
        modifierId: json['modifierId'] as String,
        name: json['name'] as String,
        quantity: (json['quantity'] as num?)?.toDouble() ?? 1,
        priceDelta: (json['priceDelta'] as num?)?.toDouble() ?? 0,
        total: (json['total'] as num?)?.toDouble() ?? 0,
      );
}

class OrderLineDto {
  const OrderLineDto({
    required this.id,
    required this.productId,
    required this.productName,
    required this.quantity,
    required this.unitPrice,
    required this.modifiersTotal,
    required this.lineTotal,
    required this.status,
    required this.modifiers,
    this.sentAt,
    this.voidedAt,
    this.comment,
  });

  final String id;
  final String productId;
  final String productName;
  final double quantity;
  final double unitPrice;
  final double modifiersTotal;
  final double lineTotal;
  final String status;
  final List<OrderLineModifierDto> modifiers;
  final DateTime? sentAt;
  final DateTime? voidedAt;
  final String? comment;

  factory OrderLineDto.fromJson(Map<String, dynamic> json) => OrderLineDto(
        id: json['id'] as String,
        productId: json['productId'] as String,
        productName: json['productName'] as String,
        quantity: (json['quantity'] as num).toDouble(),
        unitPrice: (json['unitPrice'] as num).toDouble(),
        modifiersTotal: (json['modifiersTotal'] as num?)?.toDouble() ?? 0,
        lineTotal: (json['lineTotal'] as num).toDouble(),
        status: json['status'] as String,
        modifiers: ((json['modifiers'] as List<dynamic>?) ?? const [])
            .map((e) => OrderLineModifierDto.fromJson(e as Map<String, dynamic>))
            .toList(),
        comment: json['comment'] as String?,
        sentAt: json['sentAt'] == null
            ? null
            : DateTime.tryParse(json['sentAt'] as String),
        voidedAt: json['voidedAt'] == null
            ? null
            : DateTime.tryParse(json['voidedAt'] as String),
      );
}

class PaymentDto {
  const PaymentDto({
    required this.id,
    required this.shiftId,
    required this.employeeId,
    required this.method,
    required this.status,
    required this.amount,
    required this.currencyCode,
    required this.createdAt,
    this.tenderedAmount,
    this.changeAmount = 0,
    this.refundedAmount = 0,
    this.refundableAmount,
    this.providerReference,
  });

  final String id;
  final String shiftId;
  final String employeeId;
  final String method;
  final String status;
  final double amount;
  final double? tenderedAmount;
  final double changeAmount;
  final double refundedAmount;
  final double? refundableAmount;
  final String currencyCode;
  final String? providerReference;
  final DateTime createdAt;

  factory PaymentDto.fromJson(Map<String, dynamic> json) => PaymentDto(
        id: json['id'] as String,
        shiftId: json['shiftId'] as String,
        employeeId: json['employeeId'] as String,
        method: json['method'] as String,
        status: json['status'] as String,
        amount: (json['amount'] as num).toDouble(),
        tenderedAmount: (json['tenderedAmount'] as num?)?.toDouble(),
        changeAmount: (json['changeAmount'] as num?)?.toDouble() ?? 0,
        refundedAmount: (json['refundedAmount'] as num?)?.toDouble() ?? 0,
        refundableAmount: (json['refundableAmount'] as num?)?.toDouble(),
        currencyCode: json['currencyCode'] as String,
        providerReference: json['providerReference'] as String?,
        createdAt: DateTime.parse(json['createdAt'] as String),
      );
}

class PaymentResultDto {
  const PaymentResultDto({
    required this.payment,
    required this.order,
    required this.remaining,
  });

  final PaymentDto payment;
  final OrderDto order;
  final double remaining;

  factory PaymentResultDto.fromJson(Map<String, dynamic> json) =>
      PaymentResultDto(
        payment: PaymentDto.fromJson(json['payment'] as Map<String, dynamic>),
        order: OrderDto.fromJson(json['order'] as Map<String, dynamic>),
        remaining: (json['remaining'] as num).toDouble(),
      );
}

class TransferOrderItemsResultDto {
  const TransferOrderItemsResultDto({
    required this.sourceOrder,
    required this.targetOrder,
    required this.targetCreated,
    required this.movedItemIds,
  });

  final OrderDto sourceOrder;
  final OrderDto targetOrder;
  final bool targetCreated;
  final List<String> movedItemIds;

  factory TransferOrderItemsResultDto.fromJson(Map<String, dynamic> json) =>
      TransferOrderItemsResultDto(
        sourceOrder:
            OrderDto.fromJson(json['sourceOrder'] as Map<String, dynamic>),
        targetOrder:
            OrderDto.fromJson(json['targetOrder'] as Map<String, dynamic>),
        targetCreated: json['targetCreated'] as bool? ?? false,
        movedItemIds: ((json['movedItemIds'] as List<dynamic>?) ?? const [])
            .map((value) => value.toString())
            .toList(),
      );
}

class OrderHistoryItemDto {
  const OrderHistoryItemDto({
    required this.order,
    required this.cashierName,
    this.hallName,
    this.tableName,
  });

  final OrderDto order;
  final String cashierName;
  final String? hallName;
  final String? tableName;

  factory OrderHistoryItemDto.fromJson(Map<String, dynamic> json) =>
      OrderHistoryItemDto(
        order: OrderDto.fromJson(json['order'] as Map<String, dynamic>),
        cashierName: json['cashierName'] as String? ?? 'Employee',
        hallName: json['hallName'] as String?,
        tableName: json['tableName'] as String?,
      );
}

class OrderDto {
  const OrderDto({
    required this.id,
    required this.displayNumber,
    required this.status,
    required this.total,
    required this.paidTotal,
    required this.version,
    required this.items,
    required this.payments,
    required this.guestCount,
    this.tableId,
    this.closedAt,
  });

  final String id;
  final int displayNumber;
  final String status;
  final String? tableId;
  final int guestCount;
  final double total;
  final double paidTotal;
  final int version;
  final List<OrderLineDto> items;
  final List<PaymentDto> payments;
  final DateTime? closedAt;

  bool get hasNewItems => items.any((item) => item.status == 'NEW');
  bool get isPaid => status == 'PAID' || status == 'CLOSED';
  bool get isClosed => status == 'CLOSED';
  double get remaining => total > paidTotal ? total - paidTotal : 0;

  PaymentDto? get latestCompletedPayment {
    for (final payment in payments.reversed) {
      if (payment.status == 'COMPLETED') return payment;
    }
    return null;
  }

  factory OrderDto.fromJson(Map<String, dynamic> json) => OrderDto(
        id: json['id'] as String,
        displayNumber: (json['displayNumber'] as num).toInt(),
        status: json['status'] as String,
        tableId: json['tableId'] as String?,
        guestCount: (json['guestCount'] as num).toInt(),
        total: (json['total'] as num).toDouble(),
        paidTotal: (json['paidTotal'] as num?)?.toDouble() ?? 0,
        version: (json['version'] as num).toInt(),
        closedAt: json['closedAt'] == null
            ? null
            : DateTime.tryParse(json['closedAt'] as String),
        payments: ((json['payments'] as List<dynamic>?) ?? const [])
            .map((e) => PaymentDto.fromJson(e as Map<String, dynamic>))
            .toList(),
        items: (json['items'] as List<dynamic>)
            .map((e) => OrderLineDto.fromJson(e as Map<String, dynamic>))
            .toList(),
      );
}
