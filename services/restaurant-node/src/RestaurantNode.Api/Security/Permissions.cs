namespace RestaurantNode.Api.Security;

public static class Permissions
{
    public const string MenuRead = "menu.read";
    public const string OrdersRead = "orders.read";
    public const string OrdersWrite = "orders.write";
    public const string OrdersVoid = "orders.void";
    public const string ShiftsManage = "shifts.manage";
    public const string PaymentsWrite = "payments.write";

    public const string BackOfficeRead = "backoffice.read";
    public const string RestaurantManage = "restaurant.manage";
    public const string HallsManage = "halls.manage";
    public const string MenuManage = "menu.manage";
    public const string KitchenManage = "kitchen.manage";
    public const string EmployeesManage = "employees.manage";
    public const string DevicesManage = "devices.manage";

    public static readonly string[] All =
    [
        MenuRead,
        OrdersRead,
        OrdersWrite,
        OrdersVoid,
        ShiftsManage,
        PaymentsWrite,
        BackOfficeRead,
        RestaurantManage,
        HallsManage,
        MenuManage,
        KitchenManage,
        EmployeesManage,
        DevicesManage
    ];
}
