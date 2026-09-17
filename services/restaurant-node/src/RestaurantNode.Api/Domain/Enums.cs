namespace RestaurantNode.Api.Domain;

public enum DeviceType { Pos, WaiterTablet, KitchenDisplay, Kiosk, CustomerDisplay, RestaurantNode }
public enum PrinterConnectionType { Network, WindowsQueue }
public enum OrderStatus { Draft, Open, PartiallySent, Sent, PartiallyPaid, Paid, Closed, Cancelled }
public enum OrderItemStatus { New, Sent, Voided }
public enum ShiftStatus { Open, Closed }
public enum PaymentMethod { Cash, Card, Other }
public enum PaymentStatus { Pending, Completed, Cancelled, Refunded }
public enum CashTransactionType { Deposit, Withdrawal }
public enum PrintJobStatus { Pending, Printing, Printed, Failed }
public enum KitchenTicketStatus { Pending, Printed, Cancelled }
