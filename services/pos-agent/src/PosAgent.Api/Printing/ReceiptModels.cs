namespace PosAgent.Api.Printing;

public sealed record ReceiptLineRequest(
    string Name,
    decimal Quantity,
    decimal UnitPrice,
    decimal LineTotal);

public sealed record ReceiptPrintRequest(
    string RestaurantName,
    long OrderNumber,
    string? HallName,
    string? TableName,
    string CashierName,
    int GuestCount,
    string CurrencyCode,
    decimal Total,
    string? PaymentMethod,
    decimal? PaidAmount,
    DateTimeOffset? CompletedAt,
    IReadOnlyList<ReceiptLineRequest> Items);

public sealed record ReceiptPrintResult(
    Guid PrinterId,
    string PrinterName,
    string ConnectionType,
    string Address,
    int? Port,
    string PrintMode,
    long OrderNumber,
    DateTimeOffset PrintedAt);

public sealed record PaidReceiptBundleResult(
    Guid PrinterId,
    string PrinterName,
    string ConnectionType,
    string Address,
    int? Port,
    string PrintMode,
    long OrderNumber,
    DateTimeOffset SaleReceiptPrintedAt,
    DateTimeOffset PickupTicketPrintedAt);
