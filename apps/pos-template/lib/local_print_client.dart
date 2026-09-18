import 'dart:convert';

import 'package:http/http.dart' as http;

class LocalPrintException implements Exception {
  LocalPrintException(this.message, [this.statusCode]);

  final String message;
  final int? statusCode;

  @override
  String toString() => message;
}

class ReceiptPrintItem {
  const ReceiptPrintItem({
    required this.name,
    required this.quantity,
    required this.unitPrice,
    required this.lineTotal,
  });

  final String name;
  final double quantity;
  final double unitPrice;
  final double lineTotal;

  Map<String, dynamic> toJson() => {
        'name': name,
        'quantity': quantity,
        'unitPrice': unitPrice,
        'lineTotal': lineTotal,
      };
}

class LocalReceiptPrintResult {
  const LocalReceiptPrintResult({
    required this.orderNumber,
    required this.printerName,
    required this.printMode,
    required this.printedAt,
  });

  final int orderNumber;
  final String printerName;
  final String printMode;
  final DateTime? printedAt;

  factory LocalReceiptPrintResult.fromJson(Map<String, dynamic> json) =>
      LocalReceiptPrintResult(
        orderNumber: (json['orderNumber'] as num).toInt(),
        printerName: json['printerName'] as String? ?? 'Printer',
        printMode: json['printMode'] as String? ?? 'Unknown',
        printedAt: json['printedAt'] == null
            ? null
            : DateTime.tryParse(json['printedAt'] as String),
      );
}

class PaidReceiptBundleResult {
  const PaidReceiptBundleResult({
    required this.orderNumber,
    required this.printerName,
    required this.printMode,
    required this.documentsPrinted,
    required this.saleReceiptPrintedAt,
    required this.pickupTicketPrintedAt,
  });

  final int orderNumber;
  final String printerName;
  final String printMode;
  final int documentsPrinted;
  final DateTime? saleReceiptPrintedAt;
  final DateTime? pickupTicketPrintedAt;

  factory PaidReceiptBundleResult.fromJson(Map<String, dynamic> json) =>
      PaidReceiptBundleResult(
        orderNumber: (json['orderNumber'] as num).toInt(),
        printerName: json['printerName'] as String? ?? 'Printer',
        printMode: json['printMode'] as String? ?? 'Unknown',
        documentsPrinted: (json['documentsPrinted'] as num?)?.toInt() ?? 2,
        saleReceiptPrintedAt: json['saleReceiptPrintedAt'] == null
            ? null
            : DateTime.tryParse(json['saleReceiptPrintedAt'] as String),
        pickupTicketPrintedAt: json['pickupTicketPrintedAt'] == null
            ? null
            : DateTime.tryParse(json['pickupTicketPrintedAt'] as String),
      );
}

class PosAgentClient {
  PosAgentClient(this.baseUrl, {http.Client? httpClient})
      : _http = httpClient ?? http.Client();

  final String baseUrl;
  final http.Client _http;

  Uri _uri(String path) => Uri.parse('$baseUrl$path');

  Future<LocalReceiptPrintResult> printReceipt({
    required String restaurantName,
    required int orderNumber,
    required String? hallName,
    required String? tableName,
    required String cashierName,
    required int guestCount,
    required String currencyCode,
    required double total,
    required List<ReceiptPrintItem> items,
    String? paymentMethod,
    double? paidAmount,
    DateTime? completedAt,
  }) async {
    final response = await _http.post(
      _uri('/api/v1/print/receipt'),
      headers: {'Content-Type': 'application/json'},
      body: jsonEncode(
        _receiptBody(
          restaurantName: restaurantName,
          orderNumber: orderNumber,
          hallName: hallName,
          tableName: tableName,
          cashierName: cashierName,
          guestCount: guestCount,
          currencyCode: currencyCode,
          total: total,
          items: items,
          paymentMethod: paymentMethod,
          paidAmount: paidAmount,
          completedAt: completedAt,
        ),
      ),
    );

    final data = _decode(response);
    return LocalReceiptPrintResult.fromJson(data);
  }

  Future<PaidReceiptBundleResult> printPaidBundle({
    required String restaurantName,
    required int orderNumber,
    required String? hallName,
    required String? tableName,
    required String cashierName,
    required int guestCount,
    required String currencyCode,
    required double total,
    required String paymentMethod,
    required double paidAmount,
    required List<ReceiptPrintItem> items,
    DateTime? completedAt,
  }) async {
    final response = await _http.post(
      _uri('/api/v1/print/paid-bundle'),
      headers: {'Content-Type': 'application/json'},
      body: jsonEncode(
        _receiptBody(
          restaurantName: restaurantName,
          orderNumber: orderNumber,
          hallName: hallName,
          tableName: tableName,
          cashierName: cashierName,
          guestCount: guestCount,
          currencyCode: currencyCode,
          total: total,
          items: items,
          paymentMethod: paymentMethod,
          paidAmount: paidAmount,
          completedAt: completedAt ?? DateTime.now(),
        ),
      ),
    );

    final data = _decode(response);
    return PaidReceiptBundleResult.fromJson(data);
  }

  Map<String, dynamic> _receiptBody({
    required String restaurantName,
    required int orderNumber,
    required String? hallName,
    required String? tableName,
    required String cashierName,
    required int guestCount,
    required String currencyCode,
    required double total,
    required List<ReceiptPrintItem> items,
    String? paymentMethod,
    double? paidAmount,
    DateTime? completedAt,
  }) =>
      {
        'restaurantName': restaurantName,
        'orderNumber': orderNumber,
        'hallName': hallName,
        'tableName': tableName,
        'cashierName': cashierName,
        'guestCount': guestCount,
        'currencyCode': currencyCode,
        'total': total,
        'paymentMethod': paymentMethod,
        'paidAmount': paidAmount,
        'completedAt': completedAt?.toUtc().toIso8601String(),
        'items': items.map((item) => item.toJson()).toList(),
      };

  Map<String, dynamic> _decode(http.Response response) {
    Map<String, dynamic> data = {};
    if (response.body.isNotEmpty) {
      final decoded = jsonDecode(utf8.decode(response.bodyBytes));
      if (decoded is Map<String, dynamic>) data = decoded;
    }

    if (response.statusCode < 200 || response.statusCode >= 300) {
      throw LocalPrintException(
        data['message'] as String? ??
            data['detail'] as String? ??
            data['title'] as String? ??
            'Local print failed (${response.statusCode})',
        response.statusCode,
      );
    }

    return data;
  }
}
