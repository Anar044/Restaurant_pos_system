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
    decimal? CashReceived,
    decimal? ChangeAmount,
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
