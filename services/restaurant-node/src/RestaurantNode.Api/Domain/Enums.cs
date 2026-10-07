namespace RestaurantNode.Api.Domain;

public enum DeviceType { Pos, WaiterTablet, KitchenDisplay, Kiosk, CustomerDisplay, RestaurantNode }
public enum PrinterConnectionType { Network, WindowsQueue }
public enum OrderStatus { Draft, Open, PartiallySent, Sent, PartiallyPaid, Paid, Closed, Cancelled }
public enum OrderItemStatus { New, Sent, Voided }
public enum OrderAdjustmentType { Discount, ServiceCharge }
public enum OrderAdjustmentMode { Percent, Fixed }
public enum OrderAdjustmentScope { Order, Guest, Both }
public enum OrderAdjustmentApplicationMode { Manual, Automatic }
public enum OrderAdjustmentTimeBasis { OrderOpenedAt, ItemAddedAt }
public enum OrderAdjustmentTargetMode { AllItems, PresetSelection, PosSelection }
public enum ShiftStatus { Open, Closed }
public enum PaymentMethod { Cash, Card, Other }
public enum PaymentStatus { Pending, Completed, Cancelled, Refunded }
public enum CashTransactionType { Deposit, Withdrawal }
public enum PrintJobStatus { Pending, Printing, Printed, Failed }
public enum KitchenTicketStatus { Pending, Printed, Cancelled }

public enum MoneyAccountType { Cash, Bank, Card, Other }
public enum MoneyDirection { Income, Expense }
public enum LedgerAccountType { Asset, Liability, Equity, Income, Expense }

public enum SupplierType { External, Internal }
public enum StockDocumentType { Receipt, WriteOff, Transfer, Inventory, Realization, PurchaseRequisition, PurchaseOrder }
public enum StockDocumentStatus { Draft, PendingApproval, Approved, Sent, Confirmed, PartiallyReceived, Completed, Posted, Cancelled }
public enum InventoryCostMethod { WeightedAverage, Fifo }
