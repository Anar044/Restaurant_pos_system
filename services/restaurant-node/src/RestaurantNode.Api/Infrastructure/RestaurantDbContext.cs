using Microsoft.EntityFrameworkCore;
using RestaurantNode.Api.Domain;

namespace RestaurantNode.Api.Infrastructure;

public sealed class RestaurantDbContext(DbContextOptions<RestaurantDbContext> options) : DbContext(options)
{
    public DbSet<Organization> Organizations => Set<Organization>();
    public DbSet<Restaurant> Restaurants => Set<Restaurant>();
    public DbSet<Role> Roles => Set<Role>();
    public DbSet<Employee> Employees => Set<Employee>();
    public DbSet<Printer> Printers => Set<Printer>();
    public DbSet<Device> Devices => Set<Device>();
    public DbSet<Hall> Halls => Set<Hall>();
    public DbSet<DiningTable> DiningTables => Set<DiningTable>();
    public DbSet<Category> Categories => Set<Category>();
    public DbSet<KitchenStation> KitchenStations => Set<KitchenStation>();
    public DbSet<Product> Products => Set<Product>();
    public DbSet<ProductPrice> ProductPrices => Set<ProductPrice>();
    public DbSet<ModifierGroup> ModifierGroups => Set<ModifierGroup>();
    public DbSet<Modifier> Modifiers => Set<Modifier>();
    public DbSet<ProductModifierGroup> ProductModifierGroups => Set<ProductModifierGroup>();
    public DbSet<ModifierGroupModifier> ModifierGroupModifiers => Set<ModifierGroupModifier>();
    public DbSet<Shift> Shifts => Set<Shift>();
    public DbSet<Order> Orders => Set<Order>();
    public DbSet<OrderItem> OrderItems => Set<OrderItem>();
    public DbSet<OrderItemModifier> OrderItemModifiers => Set<OrderItemModifier>();
    public DbSet<Payment> Payments => Set<Payment>();
    public DbSet<CashTransaction> CashTransactions => Set<CashTransaction>();
    public DbSet<KitchenTicket> KitchenTickets => Set<KitchenTicket>();
    public DbSet<PrintJob> PrintJobs => Set<PrintJob>();
    public DbSet<AuditEvent> AuditEvents => Set<AuditEvent>();
    public DbSet<OutboxEvent> OutboxEvents => Set<OutboxEvent>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasSequence<long>("order_display_number_seq").StartsAt(1);

        modelBuilder.Entity<Organization>().ToTable("organizations");
        modelBuilder.Entity<Restaurant>().ToTable("restaurants");
        modelBuilder.Entity<Role>().ToTable("roles");
        modelBuilder.Entity<Employee>().ToTable("employees");
        modelBuilder.Entity<Printer>().ToTable("printers");
        modelBuilder.Entity<Device>().ToTable("devices");
        modelBuilder.Entity<Hall>().ToTable("halls");
        modelBuilder.Entity<DiningTable>().ToTable("dining_tables");
        modelBuilder.Entity<Category>().ToTable("categories");
        modelBuilder.Entity<KitchenStation>().ToTable("kitchen_stations");
        modelBuilder.Entity<Product>().ToTable("products");
        modelBuilder.Entity<ProductPrice>().ToTable("product_prices");
        modelBuilder.Entity<ModifierGroup>().ToTable("modifier_groups");
        modelBuilder.Entity<Modifier>().ToTable("modifiers");
        modelBuilder.Entity<ProductModifierGroup>().ToTable("product_modifier_groups");
        modelBuilder.Entity<ModifierGroupModifier>().ToTable("modifier_group_modifiers");
        modelBuilder.Entity<Shift>().ToTable("shifts");
        modelBuilder.Entity<Order>().ToTable("orders");
        modelBuilder.Entity<OrderItem>().ToTable("order_items");
        modelBuilder.Entity<OrderItemModifier>().ToTable("order_item_modifiers");
        modelBuilder.Entity<Payment>().ToTable("payments");
        modelBuilder.Entity<CashTransaction>().ToTable("cash_transactions");
        modelBuilder.Entity<KitchenTicket>().ToTable("kitchen_tickets");
        modelBuilder.Entity<PrintJob>().ToTable("print_jobs");
        modelBuilder.Entity<AuditEvent>().ToTable("audit_events");
        modelBuilder.Entity<OutboxEvent>().ToTable("outbox_events");

        modelBuilder.Entity<Role>().Property(x => x.Permissions).HasColumnType("text[]");
        modelBuilder.Entity<PrintJob>().Property(x => x.PayloadJson).HasColumnType("jsonb");
        modelBuilder.Entity<AuditEvent>().Property(x => x.PayloadJson).HasColumnType("jsonb");
        modelBuilder.Entity<OutboxEvent>().Property(x => x.PayloadJson).HasColumnType("jsonb");

        modelBuilder.Entity<Printer>().Property(x => x.ConnectionType).HasConversion<string>();
        modelBuilder.Entity<Device>().Property(x => x.Type).HasConversion<string>();
        modelBuilder.Entity<Order>().Property(x => x.Status).HasConversion<string>();
        modelBuilder.Entity<OrderItem>().Property(x => x.Status).HasConversion<string>();
        modelBuilder.Entity<Shift>().Property(x => x.Status).HasConversion<string>();
        modelBuilder.Entity<Payment>().Property(x => x.Method).HasConversion<string>();
        modelBuilder.Entity<Payment>().Property(x => x.Status).HasConversion<string>();
        modelBuilder.Entity<CashTransaction>().Property(x => x.Type).HasConversion<string>();
        modelBuilder.Entity<KitchenTicket>().Property(x => x.Status).HasConversion<string>();
        modelBuilder.Entity<PrintJob>().Property(x => x.Status).HasConversion<string>();

        modelBuilder.Entity<ProductPrice>().Property(x => x.Amount).HasPrecision(18, 4);
        modelBuilder.Entity<Modifier>().Property(x => x.PriceDelta).HasPrecision(18, 4);
        modelBuilder.Entity<Shift>().Property(x => x.OpeningCash).HasPrecision(18, 4);
        modelBuilder.Entity<Shift>().Property(x => x.ClosingCash).HasPrecision(18, 4);
        modelBuilder.Entity<Order>().Property(x => x.Subtotal).HasPrecision(18, 4);
        modelBuilder.Entity<Order>().Property(x => x.DiscountTotal).HasPrecision(18, 4);
        modelBuilder.Entity<Order>().Property(x => x.SurchargeTotal).HasPrecision(18, 4);
        modelBuilder.Entity<Order>().Property(x => x.Total).HasPrecision(18, 4);
        modelBuilder.Entity<Order>().Property(x => x.PaidTotal).HasPrecision(18, 4);
        modelBuilder.Entity<OrderItem>().Property(x => x.Quantity).HasPrecision(18, 3);
        modelBuilder.Entity<OrderItem>().Property(x => x.UnitPrice).HasPrecision(18, 4);
        modelBuilder.Entity<OrderItem>().Property(x => x.ModifiersTotal).HasPrecision(18, 4);
        modelBuilder.Entity<OrderItem>().Property(x => x.LineTotal).HasPrecision(18, 4);
        modelBuilder.Entity<OrderItemModifier>().Property(x => x.Quantity).HasPrecision(18, 3);
        modelBuilder.Entity<OrderItemModifier>().Property(x => x.PriceDelta).HasPrecision(18, 4);
        modelBuilder.Entity<OrderItemModifier>().Property(x => x.Total).HasPrecision(18, 4);
        modelBuilder.Entity<Payment>().Property(x => x.Amount).HasPrecision(18, 4);
        modelBuilder.Entity<CashTransaction>().Property(x => x.Amount).HasPrecision(18, 4);

        modelBuilder.Entity<Order>()
            .Property(x => x.DisplayNumber)
            .HasDefaultValueSql("nextval('order_display_number_seq')")
            .ValueGeneratedOnAdd();

        modelBuilder.Entity<Organization>().HasIndex(x => x.Name);
        modelBuilder.Entity<Restaurant>().HasIndex(x => new { x.OrganizationId, x.Name });
        modelBuilder.Entity<Employee>().HasIndex(x => new { x.RestaurantId, x.Name });
        modelBuilder.Entity<Printer>().HasIndex(x => new { x.RestaurantId, x.Name });
        modelBuilder.Entity<Printer>().HasIndex(x => new { x.HostDeviceId, x.Address }).IsUnique();
        modelBuilder.Entity<Device>().HasIndex(x => new { x.RestaurantId, x.Name }).IsUnique();
        modelBuilder.Entity<Hall>().HasIndex(x => new { x.RestaurantId, x.Name }).IsUnique();
        modelBuilder.Entity<DiningTable>().HasIndex(x => new { x.HallId, x.Name }).IsUnique();
        modelBuilder.Entity<Category>().HasIndex(x => new { x.RestaurantId, x.Name }).IsUnique();
        modelBuilder.Entity<Product>().HasIndex(x => new { x.RestaurantId, x.Name });
        modelBuilder.Entity<ProductPrice>().HasIndex(x => new { x.ProductId, x.ValidFrom });
        modelBuilder.Entity<Order>().HasIndex(x => new { x.RestaurantId, x.Status, x.CreatedAt });
        modelBuilder.Entity<OutboxEvent>().HasIndex(x => new { x.ProcessedAt, x.OccurredAt });
        modelBuilder.Entity<AuditEvent>().HasIndex(x => new { x.RestaurantId, x.EntityId, x.CreatedAt });

        modelBuilder.Entity<ProductModifierGroup>().HasKey(x => new { x.ProductId, x.ModifierGroupId });
        modelBuilder.Entity<ModifierGroupModifier>().HasKey(x => new { x.ModifierGroupId, x.ModifierId });

        modelBuilder.Entity<Restaurant>()
            .HasOne(x => x.Organization)
            .WithMany()
            .HasForeignKey(x => x.OrganizationId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<Printer>()
            .HasOne(x => x.HostDevice)
            .WithMany()
            .HasForeignKey(x => x.HostDeviceId)
            .OnDelete(DeleteBehavior.SetNull);

        modelBuilder.Entity<Device>()
            .HasOne(x => x.ReceiptPrinter)
            .WithMany()
            .HasForeignKey(x => x.ReceiptPrinterId)
            .OnDelete(DeleteBehavior.SetNull);

        modelBuilder.Entity<KitchenStation>()
            .HasOne(x => x.Printer)
            .WithMany()
            .HasForeignKey(x => x.PrinterId)
            .OnDelete(DeleteBehavior.SetNull);

        modelBuilder.Entity<Order>()
            .HasMany(x => x.Items)
            .WithOne(x => x.Order)
            .HasForeignKey(x => x.OrderId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<Order>()
            .HasMany(x => x.Payments)
            .WithOne()
            .HasForeignKey(x => x.OrderId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<OrderItem>()
            .HasMany(x => x.Modifiers)
            .WithOne()
            .HasForeignKey(x => x.OrderItemId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}