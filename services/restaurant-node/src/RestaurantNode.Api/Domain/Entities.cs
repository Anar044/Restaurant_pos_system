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
    public string TaxRegime { get; set; } = "UNCONFIGURED";
    public string VatPriceMode { get; set; } = "INCLUDED";
    public bool IntegratedPosTaxReliefEnabled { get; set; }
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
    public bool IsConfigured { get; set; } = true;
    public bool IsDefault { get; set; }
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
    public Guid GroupId { get; set; }
    public RestaurantGroup? Group { get; set; }
    public Guid? PrecheckPrinterId { get; set; }
    public Printer? PrecheckPrinter { get; set; }
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

public sealed class RestaurantGroup : Entity
{
    public Guid RestaurantId { get; set; }
    public Guid? DefaultPrecheckPrinterId { get; set; }
    public Printer? DefaultPrecheckPrinter { get; set; }
    public required string Name { get; set; }
    public bool IsActive { get; set; } = true;
}

public sealed class RestaurantGroupDevice
{
    public Guid GroupId { get; set; }
    public RestaurantGroup? Group { get; set; }
    public Guid DeviceId { get; set; }
    public Device? Device { get; set; }
    public bool IsMainCashRegister { get; set; }
}

public sealed class RestaurantDepartment : Entity
{
    public Guid RestaurantId { get; set; }
    public Guid GroupId { get; set; }
    public RestaurantGroup? Group { get; set; }
    public required string Name { get; set; }
    public Guid? PreparationPlaceTypeId { get; set; }
    public PreparationPlaceType? PreparationPlaceType { get; set; }
    public Guid? WarehouseId { get; set; }
    public Warehouse? Warehouse { get; set; }
    public Guid? PrinterId { get; set; }
    public Printer? Printer { get; set; }
    public bool IsActive { get; set; } = true;
}

public sealed class PreparationPlaceType : Entity
{
    public Guid RestaurantId { get; set; }
    public required string Name { get; set; }
    public bool IsActive { get; set; } = true;
}

public sealed class Product : Entity
{
    public Guid RestaurantId { get; set; }
    public Guid? CategoryId { get; set; }
    public Category? Category { get; set; }
    public Guid? PreparationPlaceTypeId { get; set; }
    public PreparationPlaceType? PreparationPlaceType { get; set; }
    public required string Name { get; set; }
    public string? Sku { get; set; }
    public string Type { get; set; } = "DISH";
    public string Unit { get; set; } = "pcs";
    public decimal MinStock { get; set; }
    public bool TrackStock { get; set; }
    public string? InventoryAccountCode { get; set; }
    public string TaxStatus { get; set; } = "STANDARD";
    public bool? OwnAgricultureSameTaxpayer { get; set; }
    public bool? OwnAgricultureCriteriaMet { get; set; }
    public bool? OwnAgricultureUnprocessed { get; set; }
    public string? ProductionUnit { get; set; }
    public string? OriginDocument { get; set; }
    public DateOnly? ProductionOrHarvestDate { get; set; }
    public bool IsSellable { get; set; } = true;
    public bool IsActive { get; set; } = true;
    public int SortOrder { get; set; }
}

public sealed class RecipeLine : Entity
{
    public Guid RestaurantId { get; set; }
    public Guid ProductId { get; set; }
    public Product? Product { get; set; }
    public Guid IngredientProductId { get; set; }
    public Product? IngredientProduct { get; set; }
    public decimal Quantity { get; set; }
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

public sealed class OrderAdjustmentPreset : Entity
{
    public Guid RestaurantId { get; set; }
    public required string Name { get; set; }
    public OrderAdjustmentType Type { get; set; }
    public OrderAdjustmentMode Mode { get; set; }
    public OrderAdjustmentScope Scope { get; set; } = OrderAdjustmentScope.Order;
    public OrderAdjustmentApplicationMode ApplicationMode { get; set; } = OrderAdjustmentApplicationMode.Manual;
    public OrderAdjustmentTimeBasis TimeBasis { get; set; } = OrderAdjustmentTimeBasis.ItemAddedAt;
    public OrderAdjustmentTargetMode TargetMode { get; set; } = OrderAdjustmentTargetMode.AllItems;
    public decimal Value { get; set; }
    public int Priority { get; set; } = 100;
    public bool CanStack { get; set; } = true;
    public int WeekdayMask { get; set; } = 127;
    public int? StartMinute { get; set; }
    public int? EndMinute { get; set; }
    public bool RequireComment { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
    public List<OrderAdjustmentPresetRole> AllowedRoles { get; set; } = [];
    public List<OrderAdjustmentPresetProduct> Products { get; set; } = [];
    public List<OrderAdjustmentPresetCategory> Categories { get; set; } = [];
}

public sealed class OrderAdjustmentPresetRole
{
    public Guid PresetId { get; set; }
    public OrderAdjustmentPreset? Preset { get; set; }
    public Guid RoleId { get; set; }
    public Role? Role { get; set; }
}

public sealed class OrderAdjustmentPresetProduct
{
    public Guid PresetId { get; set; }
    public OrderAdjustmentPreset? Preset { get; set; }
    public Guid ProductId { get; set; }
    public Product? Product { get; set; }
}

public sealed class OrderAdjustmentPresetCategory
{
    public Guid PresetId { get; set; }
    public OrderAdjustmentPreset? Preset { get; set; }
    public Guid CategoryId { get; set; }
    public Category? Category { get; set; }
}

public sealed class Warehouse : Entity
{
    public Guid RestaurantId { get; set; }
    public required string Name { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class Supplier : Entity
{
    public Guid RestaurantId { get; set; }
    public required string Name { get; set; }
    public SupplierType Type { get; set; } = SupplierType.External;
    public string? TaxId { get; set; }
    public string? Phone { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class StockDocument : Entity
{
    public Guid RestaurantId { get; set; }
    public StockDocumentType Type { get; set; } = StockDocumentType.Receipt;
    public StockDocumentStatus Status { get; set; } = StockDocumentStatus.Draft;
    public required string Number { get; set; }
    public DateTimeOffset DocumentDate { get; set; } = DateTimeOffset.UtcNow;
    public Guid? WarehouseId { get; set; }
    public Warehouse? Warehouse { get; set; }
    public Guid? FromWarehouseId { get; set; }
    public Warehouse? FromWarehouse { get; set; }
    public Guid? ToWarehouseId { get; set; }
    public Warehouse? ToWarehouse { get; set; }
    public Guid? SupplierId { get; set; }
    public Supplier? Supplier { get; set; }
    public decimal TotalAmount { get; set; }
    public string? Comment { get; set; }
    public Guid CreatedByEmployeeId { get; set; }
    public Guid? PostedByEmployeeId { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? PostedAt { get; set; }
    public List<StockDocumentLine> Lines { get; set; } = [];
}

public sealed class StockDocumentLine : Entity
{
    public Guid RestaurantId { get; set; }
    public Guid DocumentId { get; set; }
    public StockDocument? Document { get; set; }
    public Guid ProductId { get; set; }
    public Product? Product { get; set; }
    public decimal Quantity { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal Amount { get; set; }
}

public sealed class StockMovement : Entity
{
    public Guid RestaurantId { get; set; }
    public Guid WarehouseId { get; set; }
    public Warehouse? Warehouse { get; set; }
    public Guid ProductId { get; set; }
    public Product? Product { get; set; }
    public Guid EmployeeId { get; set; }
    public Guid OperationId { get; set; }
    public string Type { get; set; } = "RECEIPT";
    public decimal QuantityDelta { get; set; }
    public decimal? UnitCost { get; set; }
    public decimal? CostDelta { get; set; }
    public string? ReferenceType { get; set; }
    public Guid? ReferenceId { get; set; }
    public string? Note { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
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
    public decimal? ExpectedCashAtClose { get; set; }
    public decimal? CashDifference { get; set; }
    public string? ClosingNote { get; set; }
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
    public Guid? OriginDeviceId { get; set; }
    public Guid? OpenedShiftId { get; set; }
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
    public List<OrderAdjustment> Adjustments { get; set; } = [];
}

public sealed class OrderItem : Entity
{
    public Guid OrderId { get; set; }
    public Order? Order { get; set; }
    public Guid ProductId { get; set; }
    public Guid CategoryIdSnapshot { get; set; }
    public string ProductNameSnapshot { get; set; } = string.Empty;
    public int GuestNumber { get; set; } = 1;
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

public sealed class OrderAdjustment : Entity
{
    public Guid OrderId { get; set; }
    public Order? Order { get; set; }
    public OrderAdjustmentType Type { get; set; }
    public OrderAdjustmentMode Mode { get; set; }
    public Guid? PresetId { get; set; }
    public string? PresetNameSnapshot { get; set; }
    public OrderAdjustmentApplicationMode ApplicationModeSnapshot { get; set; } = OrderAdjustmentApplicationMode.Manual;
    public OrderAdjustmentTimeBasis TimeBasisSnapshot { get; set; } = OrderAdjustmentTimeBasis.ItemAddedAt;
    public OrderAdjustmentTargetMode TargetModeSnapshot { get; set; } = OrderAdjustmentTargetMode.AllItems;
    public int PrioritySnapshot { get; set; } = 100;
    public bool CanStackSnapshot { get; set; } = true;
    public int WeekdayMaskSnapshot { get; set; } = 127;
    public int? StartMinuteSnapshot { get; set; }
    public int? EndMinuteSnapshot { get; set; }
    public string TimeZoneIdSnapshot { get; set; } = "Asia/Baku";
    public Guid[] ProductIdsSnapshot { get; set; } = [];
    public Guid[] CategoryIdsSnapshot { get; set; } = [];
    public Guid[] OrderItemIdsSnapshot { get; set; } = [];
    public int? GuestNumber { get; set; }
    public decimal Value { get; set; }
    public decimal CalculatedAmount { get; set; }
    public string? Reason { get; set; }
    public Guid AppliedByEmployeeId { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class Payment : Entity
{
    public Guid RestaurantId { get; set; }
    public Guid OrderId { get; set; }
    public Guid ShiftId { get; set; }
    public Guid EmployeeId { get; set; }
    public int? GuestNumber { get; set; }
    public PaymentMethod Method { get; set; }
    public PaymentStatus Status { get; set; } = PaymentStatus.Pending;
    public decimal Amount { get; set; }
    public decimal? TenderedAmount { get; set; }
    public decimal ChangeAmount { get; set; }
    public string CurrencyCode { get; set; } = "AZN";
    public string? ProviderReference { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class PaymentRefund : Entity
{
    public Guid RestaurantId { get; set; }
    public Guid PaymentId { get; set; }
    public Guid OrderId { get; set; }
    public Guid ShiftId { get; set; }
    public Guid EmployeeId { get; set; }
    public decimal Amount { get; set; }
    public string? Reason { get; set; }
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

public sealed class MoneyAccount : Entity
{
    public Guid RestaurantId { get; set; }
    public required string Name { get; set; }
    public MoneyAccountType Type { get; set; } = MoneyAccountType.Cash;
    public bool IsActive { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class MoneyCategory : Entity
{
    public Guid RestaurantId { get; set; }
    public required string Name { get; set; }
    public MoneyDirection Direction { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class MoneyTransaction : Entity
{
    public Guid RestaurantId { get; set; }
    public Guid AccountId { get; set; }
    public MoneyAccount? Account { get; set; }
    public Guid CategoryId { get; set; }
    public MoneyCategory? Category { get; set; }
    public Guid EmployeeId { get; set; }
    public MoneyDirection Direction { get; set; }
    public decimal Amount { get; set; }
    public string? Note { get; set; }
    public string? ReferenceType { get; set; }
    public Guid? ReferenceId { get; set; }
    public DateTimeOffset OccurredAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class LedgerAccount : Entity
{
    public Guid RestaurantId { get; set; }
    public required string Code { get; set; }
    public required string Name { get; set; }
    public LedgerAccountType Type { get; set; }
    public string? SystemKey { get; set; }
    public bool IsSystem { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class LedgerEntry : Entity
{
    public Guid RestaurantId { get; set; }
    public DateTimeOffset OccurredAt { get; set; } = DateTimeOffset.UtcNow;
    public required string ReferenceType { get; set; }
    public Guid ReferenceId { get; set; }
    public required string Description { get; set; }
    public Guid? EmployeeId { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public List<LedgerLine> Lines { get; set; } = [];
}

public sealed class LedgerLine : Entity
{
    public Guid RestaurantId { get; set; }
    public Guid EntryId { get; set; }
    public LedgerEntry? Entry { get; set; }
    public Guid AccountId { get; set; }
    public LedgerAccount? Account { get; set; }
    public decimal Debit { get; set; }
    public decimal Credit { get; set; }
    public Guid? SupplierId { get; set; }
    public Guid? WarehouseId { get; set; }
    public Guid? MoneyAccountId { get; set; }
}

public sealed class KitchenTicket : Entity
{
    public Guid RestaurantId { get; set; }
    public Guid OrderId { get; set; }
    public Guid DepartmentId { get; set; }
    public RestaurantDepartment? Department { get; set; }
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