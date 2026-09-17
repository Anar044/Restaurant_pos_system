using RestaurantNode.Api.Common;

namespace RestaurantNode.Api.Domain;

public abstract class Entity
{
    public Guid Id { get; set; } = Ids.New();
}

public sealed class Organization : Entity
{
    public required string Name { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class Restaurant : Entity
{
    public Guid OrganizationId { get; set; }
    public Organization? Organization { get; set; }
    public required string Name { get; set; }
    public string CurrencyCode { get; set; } = "AZN";
    public string TimeZone { get; set; } = "Asia/Baku";
    public bool IsActive { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class Role : Entity
{
    public Guid RestaurantId { get; set; }
    public Restaurant? Restaurant { get; set; }
    public required string Name { get; set; }
    public string[] Permissions { get; set; } = [];
}

public sealed class Employee : Entity
{
    public Guid RestaurantId { get; set; }
    public Restaurant? Restaurant { get; set; }
    public Guid RoleId { get; set; }
    public Role? Role { get; set; }
    public required string Name { get; set; }
    public required string PinHash { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class Printer : Entity
{
    public Guid RestaurantId { get; set; }
    public Restaurant? Restaurant { get; set; }
    public Guid? HostDeviceId { get; set; }
    public Device? HostDevice { get; set; }
    public required string Name { get; set; }
    public PrinterConnectionType ConnectionType { get; set; } = PrinterConnectionType.Network;
    public required string Address { get; set; }
    public int? Port { get; set; } = 9100;
    public bool IsActive { get; set; } = true;
    public DateTimeOffset? LastSeenAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class Device : Entity
{
    public Guid RestaurantId { get; set; }
    public Restaurant? Restaurant { get; set; }
    public required string Name { get; set; }
    public DeviceType Type { get; set; }
    public Guid? ReceiptPrinterId { get; set; }
    public Printer? ReceiptPrinter { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTimeOffset? LastSeenAt { get; set; }
}

public sealed class Hall : Entity
{
    public Guid RestaurantId { get; set; }
    public Restaurant? Restaurant { get; set; }
    public required string Name { get; set; }
    public int SortOrder { get; set; }
    public bool IsActive { get; set; } = true;
}

public sealed class DiningTable : Entity
{
    public Guid RestaurantId { get; set; }
    public Guid HallId { get; set; }
    public Hall? Hall { get; set; }
    public required string Name { get; set; }
    public int Seats { get; set; }
    public int SortOrder { get; set; }
    public bool IsActive { get; set; } = true;
}

public sealed class Category : Entity
{
    public Guid RestaurantId { get; set; }
    public Restaurant? Restaurant { get; set; }
    public required string Name { get; set; }
    public int SortOrder { get; set; }
    public bool IsActive { get; set; } = true;
}

public sealed class KitchenStation : Entity
{
    public Guid RestaurantId { get; set; }
    public required string Name { get; set; }
    public Guid? PrinterId { get; set; }
    public Printer? Printer { get; set; }
    public bool IsActive { get; set; } = true;
}

public sealed class Product : Entity
{
    public Guid RestaurantId { get; set; }
    public Guid CategoryId { get; set; }
    public Category? Category { get; set; }
    public Guid? KitchenStationId { get; set; }
    public KitchenStation? KitchenStation { get; set; }
    public required string Name { get; set; }
    public string? Sku { get; set; }
    public bool IsActive { get; set; } = true;
    public int SortOrder { get; set; }
}

public sealed class ProductPrice : Entity
{
    public Guid RestaurantId { get; set; }
    public Guid ProductId { get; set; }
    public Product? Product { get; set; }
    public decimal Amount { get; set; }
    public string CurrencyCode { get; set; } = "AZN";
    public DateTimeOffset ValidFrom { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? ValidTo { get; set; }
}

public sealed class ModifierGroup : Entity
{
    public Guid RestaurantId { get; set; }
    public required string Name { get; set; }
    public int MinSelections { get; set; }
    public int MaxSelections { get; set; } = 1;
    public bool IsRequired { get; set; }
    public bool IsActive { get; set; } = true;
}

public sealed class Modifier : Entity
{
    public Guid RestaurantId { get; set; }
    public required string Name { get; set; }
    public decimal PriceDelta { get; set; }
    public bool IsActive { get; set; } = true;
}

public sealed class ProductModifierGroup
{
    public Guid ProductId { get; set; }
    public Guid ModifierGroupId { get; set; }
    public int SortOrder { get; set; }
}

public sealed class ModifierGroupModifier
{
    public Guid ModifierGroupId { get; set; }
    public Guid ModifierId { get; set; }
    public int SortOrder { get; set; }
}

public sealed class Shift : Entity
{
    public Guid RestaurantId { get; set; }
    public Guid DeviceId { get; set; }
    public Guid OpenedByEmployeeId { get; set; }
    public Guid? ClosedByEmployeeId { get; set; }
    public ShiftStatus Status { get; set; } = ShiftStatus.Open;
    public decimal OpeningCash { get; set; }
    public decimal? ClosingCash { get; set; }
    public DateTimeOffset OpenedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? ClosedAt { get; set; }
}

public sealed class Order : Entity
{
    public Guid RestaurantId { get; set; }
    public long DisplayNumber { get; set; }
    public Guid? TableId { get; set; }
    public DiningTable? Table { get; set; }
    public Guid CreatedByEmployeeId { get; set; }
    public OrderStatus Status { get; set; } = OrderStatus.Open;
    public int GuestCount { get; set; } = 1;
    public decimal Subtotal { get; set; }
    public decimal DiscountTotal { get; set; }
    public decimal SurchargeTotal { get; set; }
    public decimal Total { get; set; }
    public decimal PaidTotal { get; set; }
    public int Version { get; set; } = 1;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? ClosedAt { get; set; }
    public List<OrderItem> Items { get; set; } = [];
    public List<Payment> Payments { get; set; } = [];
}

public sealed class OrderItem : Entity
{
    public Guid OrderId { get; set; }
    public Order? Order { get; set; }
    public Guid ProductId { get; set; }
    public string ProductNameSnapshot { get; set; } = string.Empty;
    public decimal Quantity { get; set; } = 1m;
    public decimal UnitPrice { get; set; }
    public decimal ModifiersTotal { get; set; }
    public decimal LineTotal { get; set; }
    public OrderItemStatus Status { get; set; } = OrderItemStatus.New;
    public string? Comment { get; set; }
    public Guid CreatedByEmployeeId { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? SentAt { get; set; }
    public DateTimeOffset? VoidedAt { get; set; }
    public List<OrderItemModifier> Modifiers { get; set; } = [];
}

public sealed class OrderItemModifier : Entity
{
    public Guid OrderItemId { get; set; }
    public Guid ModifierId { get; set; }
    public string ModifierNameSnapshot { get; set; } = string.Empty;
    public decimal Quantity { get; set; } = 1m;
    public decimal PriceDelta { get; set; }
    public decimal Total { get; set; }
}

public sealed class Payment : Entity
{
    public Guid RestaurantId { get; set; }
    public Guid OrderId { get; set; }
    public Guid ShiftId { get; set; }
    public Guid EmployeeId { get; set; }
    public PaymentMethod Method { get; set; }
    public PaymentStatus Status { get; set; } = PaymentStatus.Pending;
    public decimal Amount { get; set; }
    public string CurrencyCode { get; set; } = "AZN";
    public string? ProviderReference { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class CashTransaction : Entity
{
    public Guid RestaurantId { get; set; }
    public Guid ShiftId { get; set; }
    public Guid EmployeeId { get; set; }
    public CashTransactionType Type { get; set; }
    public decimal Amount { get; set; }
    public string? Reason { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class KitchenTicket : Entity
{
    public Guid RestaurantId { get; set; }
    public Guid OrderId { get; set; }
    public Guid KitchenStationId { get; set; }
    public KitchenTicketStatus Status { get; set; } = KitchenTicketStatus.Pending;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? PrintedAt { get; set; }
}

public sealed class PrintJob : Entity
{
    public Guid RestaurantId { get; set; }
    public string PrinterKey { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public string PayloadJson { get; set; } = "{}";
    public PrintJobStatus Status { get; set; } = PrintJobStatus.Pending;
    public int Attempts { get; set; }
    public string? LastError { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? PrintedAt { get; set; }
}

public sealed class AuditEvent : Entity
{
    public Guid RestaurantId { get; set; }
    public Guid? EmployeeId { get; set; }
    public Guid? DeviceId { get; set; }
    public required string EventType { get; set; }
    public required string EntityType { get; set; }
    public Guid EntityId { get; set; }
    public string PayloadJson { get; set; } = "{}";
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class OutboxEvent : Entity
{
    public Guid RestaurantId { get; set; }
    public required string EventType { get; set; }
    public required string AggregateType { get; set; }
    public Guid AggregateId { get; set; }
    public string PayloadJson { get; set; } = "{}";
    public DateTimeOffset OccurredAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? ProcessedAt { get; set; }
    public int Attempts { get; set; }
    public string? LastError { get; set; }
}