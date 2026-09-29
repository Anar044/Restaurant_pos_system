using System.Security.Claims;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using RestaurantNode.Api.Domain;
using RestaurantNode.Api.Infrastructure;
using RestaurantNode.Api.Security;

namespace RestaurantNode.Api.Features.BackOffice;

public static class BackOfficeStockDocumentEndpoints
{
    public static IEndpointRouteBuilder MapBackOfficeStockDocumentEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/backoffice/stock-documents")
            .RequireAuthorization(Permissions.BackOfficeRead);

        group.MapGet("", async (
            ClaimsPrincipal user,
            RestaurantDbContext db,
            CancellationToken ct) =>
        {
            if (!TryClaims(user, out var restaurantId, out _))
                return Results.Unauthorized();

            var suppliers = await db.Suppliers
                .AsNoTracking()
                .Where(x => x.RestaurantId == restaurantId)
                .OrderByDescending(x => x.IsActive)
                .ThenBy(x => x.Name)
                .Select(x => new
                {
                    id = x.Id,
                    name = x.Name,
                    type = EnumText(x.Type),
                    x.TaxId,
                    x.Phone,
                    x.IsActive,
                    x.CreatedAt
                })
                .ToListAsync(ct);

            var warehouses = await db.Warehouses
                .AsNoTracking()
                .Where(x => x.RestaurantId == restaurantId)
                .OrderByDescending(x => x.IsActive)
                .ThenBy(x => x.Name)
                .Select(x => new
                {
                    id = x.Id,
                    name = x.Name,
                    x.IsActive
                })
                .ToListAsync(ct);

            var items = await db.Products
                .AsNoTracking()
                .Where(x => x.RestaurantId == restaurantId && x.TrackStock)
                .OrderByDescending(x => x.IsActive)
                .ThenBy(x => x.Name)
                .Select(x => new
                {
                    id = x.Id,
                    name = x.Name,
                    x.Sku,
                    x.Unit,
                    x.IsActive
                })
                .ToListAsync(ct);

            var documents = await db.StockDocuments
                .AsNoTracking()
                .Where(x => x.RestaurantId == restaurantId)
                .Include(x => x.Lines)
                .OrderByDescending(x => x.DocumentDate)
                .ThenByDescending(x => x.CreatedAt)
                .Take(300)
                .ToListAsync(ct);

            var supplierLookup = suppliers.ToDictionary(x => x.id, x => x.name);
            var warehouseLookup = warehouses.ToDictionary(x => x.id, x => x.name);
            var itemLookup = items.ToDictionary(x => x.id, x => new { x.name, unit = x.Unit });

            return Results.Ok(new
            {
                supportedTypes = new[] { "RECEIPT" },
                suppliers,
                warehouses,
                items,
                documents = documents.Select(document => new
                {
                    id = document.Id,
                    type = EnumText(document.Type),
                    status = EnumText(document.Status),
                    document.Number,
                    document.DocumentDate,
                    document.WarehouseId,
                    warehouseName = document.WarehouseId.HasValue
                        ? warehouseLookup.GetValueOrDefault(document.WarehouseId.Value)
                        : null,
                    document.SupplierId,
                    supplierName = document.SupplierId.HasValue
                        ? supplierLookup.GetValueOrDefault(document.SupplierId.Value)
                        : null,
                    document.TotalAmount,
                    document.Comment,
                    document.CreatedByEmployeeId,
                    document.PostedByEmployeeId,
                    document.CreatedAt,
                    document.PostedAt,
                    lines = document.Lines
                        .OrderBy(x => x.Id)
                        .Select(line => new
                        {
                            id = line.Id,
                            line.ProductId,
                            productName = itemLookup.TryGetValue(line.ProductId, out var item)
                                ? item.name
                                : "Номенклатура",
                            unit = itemLookup.TryGetValue(line.ProductId, out var lineItem)
                                ? lineItem.unit
                                : "pcs",
                            line.Quantity,
                            line.UnitPrice,
                            line.Amount
                        })
                })
            });
        });

        group.MapPost("/suppliers", async (
            UpsertSupplierRequest request,
            ClaimsPrincipal user,
            RestaurantDbContext db,
            CancellationToken ct) =>
        {
            if (!TryClaims(user, out var restaurantId, out _))
                return Results.Unauthorized();

            var name = NormalizeRequired(request.Name, 160);
            if (name is null)
                return Results.BadRequest(new { message = "Название поставщика обязательно." });
            if (!Enum.TryParse<SupplierType>(request.Type, true, out var type) || !Enum.IsDefined(type))
                return Results.BadRequest(new { message = "Тип поставщика должен быть EXTERNAL или INTERNAL." });

            if (await db.Suppliers.AnyAsync(
                x => x.RestaurantId == restaurantId && x.Name == name, ct))
                return Results.Conflict(new { message = "Поставщик с таким названием уже существует." });

            var supplier = new Supplier
            {
                RestaurantId = restaurantId,
                Name = name,
                Type = type,
                TaxId = NormalizeOptional(request.TaxId, 80),
                Phone = NormalizeOptional(request.Phone, 80),
                IsActive = true
            };

            db.Suppliers.Add(supplier);
            AddAudit(db, user, restaurantId, "SUPPLIER_CREATED", "Supplier", supplier.Id, new
            {
                supplier.Name,
                type = EnumText(supplier.Type),
                supplier.TaxId,
                supplier.Phone
            });
            await db.SaveChangesAsync(ct);

            return Results.Created($"/api/v1/backoffice/stock-documents/suppliers/{supplier.Id}", new { id = supplier.Id });
        }).RequireAuthorization(Permissions.InventoryManage);

        group.MapPut("/suppliers/{id:guid}", async (
            Guid id,
            UpdateSupplierRequest request,
            ClaimsPrincipal user,
            RestaurantDbContext db,
            CancellationToken ct) =>
        {
            if (!TryClaims(user, out var restaurantId, out _))
                return Results.Unauthorized();

            var supplier = await db.Suppliers.FirstOrDefaultAsync(
                x => x.Id == id && x.RestaurantId == restaurantId, ct);
            if (supplier is null) return Results.NotFound();

            var name = NormalizeRequired(request.Name, 160);
            if (name is null)
                return Results.BadRequest(new { message = "Название поставщика обязательно." });
            if (!Enum.TryParse<SupplierType>(request.Type, true, out var type) || !Enum.IsDefined(type))
                return Results.BadRequest(new { message = "Тип поставщика должен быть EXTERNAL или INTERNAL." });

            if (await db.Suppliers.AnyAsync(
                x => x.RestaurantId == restaurantId && x.Id != id && x.Name == name, ct))
                return Results.Conflict(new { message = "Поставщик с таким названием уже существует." });

            supplier.Name = name;
            supplier.Type = type;
            supplier.TaxId = NormalizeOptional(request.TaxId, 80);
            supplier.Phone = NormalizeOptional(request.Phone, 80);
            supplier.IsActive = request.IsActive;

            AddAudit(db, user, restaurantId, "SUPPLIER_UPDATED", "Supplier", supplier.Id, new
            {
                supplier.Name,
                type = EnumText(supplier.Type),
                supplier.TaxId,
                supplier.Phone,
                supplier.IsActive
            });
            await db.SaveChangesAsync(ct);

            return Results.Ok(new { id = supplier.Id });
        }).RequireAuthorization(Permissions.InventoryManage);

        group.MapPost("", async (
            UpsertReceiptDocumentRequest request,
            ClaimsPrincipal user,
            RestaurantDbContext db,
            CancellationToken ct) =>
        {
            if (!TryClaims(user, out var restaurantId, out var employeeId))
                return Results.Unauthorized();

            var validation = await ValidateReceiptRequestAsync(db, restaurantId, request, null, ct);
            if (validation.Error is not null) return validation.Error;

            var document = new StockDocument
            {
                RestaurantId = restaurantId,
                Type = StockDocumentType.Receipt,
                Status = StockDocumentStatus.Draft,
                Number = NormalizeOptional(request.Number, 80) ?? CreateNumber("PR", DateTimeOffset.UtcNow),
                DocumentDate = request.DocumentDate ?? DateTimeOffset.UtcNow,
                WarehouseId = request.WarehouseId,
                SupplierId = request.SupplierId,
                Comment = NormalizeOptional(request.Comment, 500),
                CreatedByEmployeeId = employeeId,
                TotalAmount = validation.Lines!.Sum(x => x.Amount)
            };

            foreach (var line in validation.Lines!)
            {
                document.Lines.Add(new StockDocumentLine
                {
                    RestaurantId = restaurantId,
                    ProductId = line.ProductId,
                    Quantity = line.Quantity,
                    UnitPrice = line.UnitPrice,
                    Amount = line.Amount
                });
            }

            db.StockDocuments.Add(document);
            AddAudit(db, user, restaurantId, "STOCK_DOCUMENT_CREATED", "StockDocument", document.Id, new
            {
                document.Number,
                type = EnumText(document.Type),
                status = EnumText(document.Status),
                document.WarehouseId,
                document.SupplierId,
                document.DocumentDate,
                document.TotalAmount,
                lineCount = document.Lines.Count
            });
            await db.SaveChangesAsync(ct);

            return Results.Created($"/api/v1/backoffice/stock-documents/{document.Id}", new { id = document.Id });
        }).RequireAuthorization(Permissions.InventoryManage);

        group.MapPut("/{id:guid}", async (
            Guid id,
            UpsertReceiptDocumentRequest request,
            ClaimsPrincipal user,
            RestaurantDbContext db,
            CancellationToken ct) =>
        {
            if (!TryClaims(user, out var restaurantId, out _))
                return Results.Unauthorized();

            var document = await db.StockDocuments
                .Include(x => x.Lines)
                .FirstOrDefaultAsync(x => x.Id == id && x.RestaurantId == restaurantId, ct);
            if (document is null) return Results.NotFound();

            if (document.Status != StockDocumentStatus.Draft)
                return Results.Conflict(new { message = "Изменять можно только черновик документа." });
            if (document.Type != StockDocumentType.Receipt)
                return Results.BadRequest(new { message = "Этот редактор поддерживает только приходные накладные." });

            var validation = await ValidateReceiptRequestAsync(db, restaurantId, request, id, ct);
            if (validation.Error is not null) return validation.Error;

            document.Number = NormalizeOptional(request.Number, 80) ?? document.Number;
            document.DocumentDate = request.DocumentDate ?? document.DocumentDate;
            document.WarehouseId = request.WarehouseId;
            document.SupplierId = request.SupplierId;
            document.Comment = NormalizeOptional(request.Comment, 500);
            document.TotalAmount = validation.Lines!.Sum(x => x.Amount);

            db.StockDocumentLines.RemoveRange(document.Lines);
            document.Lines.Clear();
            foreach (var line in validation.Lines!)
            {
                document.Lines.Add(new StockDocumentLine
                {
                    RestaurantId = restaurantId,
                    ProductId = line.ProductId,
                    Quantity = line.Quantity,
                    UnitPrice = line.UnitPrice,
                    Amount = line.Amount
                });
            }

            AddAudit(db, user, restaurantId, "STOCK_DOCUMENT_UPDATED", "StockDocument", document.Id, new
            {
                document.Number,
                document.WarehouseId,
                document.SupplierId,
                document.DocumentDate,
                document.TotalAmount,
                lineCount = document.Lines.Count
            });
            await db.SaveChangesAsync(ct);

            return Results.Ok(new { id = document.Id });
        }).RequireAuthorization(Permissions.InventoryManage);

        group.MapPost("/{id:guid}/post", async (
            Guid id,
            ClaimsPrincipal user,
            RestaurantDbContext db,
            CancellationToken ct) =>
        {
            if (!TryClaims(user, out var restaurantId, out var employeeId))
                return Results.Unauthorized();

            await using var tx = await db.Database.BeginTransactionAsync(ct);

            var document = await db.StockDocuments
                .Include(x => x.Lines)
                .FirstOrDefaultAsync(x => x.Id == id && x.RestaurantId == restaurantId, ct);
            if (document is null) return Results.NotFound();

            if (document.Status != StockDocumentStatus.Draft)
                return Results.Conflict(new { message = "Документ уже проведён или отменён." });
            if (document.Type != StockDocumentType.Receipt)
                return Results.BadRequest(new { message = "Сейчас проведение поддерживается для приходной накладной." });
            if (!document.WarehouseId.HasValue || !document.SupplierId.HasValue)
                return Results.BadRequest(new { message = "Для прихода обязательны склад и поставщик." });
            if (document.Lines.Count == 0)
                return Results.BadRequest(new { message = "В документе нет позиций." });

            var warehouse = await db.Warehouses.FirstOrDefaultAsync(x =>
                x.Id == document.WarehouseId.Value &&
                x.RestaurantId == restaurantId &&
                x.IsActive, ct);
            if (warehouse is null)
                return Results.BadRequest(new { message = "Склад отключён или не найден." });

            var supplier = await db.Suppliers.FirstOrDefaultAsync(x =>
                x.Id == document.SupplierId.Value &&
                x.RestaurantId == restaurantId &&
                x.IsActive, ct);
            if (supplier is null)
                return Results.BadRequest(new { message = "Поставщик отключён или не найден." });

            var productIds = document.Lines.Select(x => x.ProductId).Distinct().ToArray();
            var validProducts = await db.Products.CountAsync(x =>
                x.RestaurantId == restaurantId &&
                x.IsActive &&
                x.TrackStock &&
                productIds.Contains(x.Id), ct);
            if (validProducts != productIds.Length)
                return Results.BadRequest(new { message = "Одна или несколько позиций больше недоступны для складского учёта." });

            var alreadyPosted = await db.StockMovements.AnyAsync(x =>
                x.RestaurantId == restaurantId &&
                x.ReferenceType == "STOCK_DOCUMENT" &&
                x.ReferenceId == document.Id, ct);
            if (alreadyPosted)
                return Results.Conflict(new { message = "По этому документу уже существуют складские проводки." });

            var now = DateTimeOffset.UtcNow;
            foreach (var line in document.Lines)
            {
                db.StockMovements.Add(new StockMovement
                {
                    RestaurantId = restaurantId,
                    WarehouseId = document.WarehouseId.Value,
                    ProductId = line.ProductId,
                    EmployeeId = employeeId,
                    OperationId = document.Id,
                    Type = "RECEIPT",
                    QuantityDelta = line.Quantity,
                    UnitCost = line.UnitPrice,
                    CostDelta = line.Amount,
                    ReferenceType = "STOCK_DOCUMENT",
                    ReferenceId = document.Id,
                    Note = document.Comment,
                    CreatedAt = document.DocumentDate
                });
            }

            if (document.TotalAmount > 0m)
            {
                await AccountingLedger.EnsureFoundationAsync(db, restaurantId, ct);
                var inventoryAccount = await AccountingLedger.EnsureWarehouseAccountAsync(
                    db, restaurantId, warehouse, ct);
                var payableAccount = await AccountingLedger.EnsureSystemAccountAsync(
                    db, restaurantId, AccountingLedger.SupplierPayableKey,
                    "2.10", "Задолженность перед поставщиками", LedgerAccountType.Liability, ct);
                var advanceAccount = await AccountingLedger.EnsureSystemAccountAsync(
                    db, restaurantId, AccountingLedger.SupplierAdvanceKey,
                    "1.30", "Авансы поставщикам", LedgerAccountType.Asset, ct);

                var advanceTotals = await db.LedgerLines
                    .AsNoTracking()
                    .Where(x => x.RestaurantId == restaurantId &&
                                x.SupplierId == supplier.Id &&
                                x.AccountId == advanceAccount.Id)
                    .GroupBy(_ => 1)
                    .Select(g => new { Debit = g.Sum(x => x.Debit), Credit = g.Sum(x => x.Credit) })
                    .FirstOrDefaultAsync(ct);

                var availableAdvance = AccountingLedger.Money(
                    (advanceTotals?.Debit ?? 0m) - (advanceTotals?.Credit ?? 0m));
                var appliedAdvance = AccountingLedger.Money(
                    Math.Min(document.TotalAmount, Math.Max(0m, availableAdvance)));

                var ledgerLines = new List<AccountingLedger.LineDraft>
                {
                    new(
                        inventoryAccount,
                        Debit: document.TotalAmount,
                        WarehouseId: warehouse.Id),
                    new(
                        payableAccount,
                        Credit: document.TotalAmount,
                        SupplierId: supplier.Id)
                };

                if (appliedAdvance > 0m)
                {
                    ledgerLines.Add(new AccountingLedger.LineDraft(
                        payableAccount,
                        Debit: appliedAdvance,
                        SupplierId: supplier.Id));
                    ledgerLines.Add(new AccountingLedger.LineDraft(
                        advanceAccount,
                        Credit: appliedAdvance,
                        SupplierId: supplier.Id));
                }

                await AccountingLedger.PostAsync(
                    db,
                    restaurantId,
                    "STOCK_RECEIPT",
                    document.Id,
                    document.DocumentDate,
                    $"Приходная накладная {document.Number}",
                    employeeId,
                    ledgerLines,
                    ct);
            }

            document.Status = StockDocumentStatus.Posted;
            document.PostedByEmployeeId = employeeId;
            document.PostedAt = now;

            AddAudit(db, user, restaurantId, "STOCK_DOCUMENT_POSTED", "StockDocument", document.Id, new
            {
                document.Number,
                type = EnumText(document.Type),
                document.WarehouseId,
                document.SupplierId,
                document.TotalAmount,
                lineCount = document.Lines.Count,
                document.PostedAt
            });

            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);

            return Results.Ok(new
            {
                id = document.Id,
                status = EnumText(document.Status),
                document.PostedAt,
                document.TotalAmount
            });
        }).RequireAuthorization(Permissions.InventoryManage);

        return app;
    }

    private sealed record ValidatedReceiptLine(Guid ProductId, decimal Quantity, decimal UnitPrice, decimal Amount);
    private sealed record ReceiptValidation(List<ValidatedReceiptLine>? Lines, IResult? Error);

    private static async Task<ReceiptValidation> ValidateReceiptRequestAsync(
        RestaurantDbContext db,
        Guid restaurantId,
        UpsertReceiptDocumentRequest request,
        Guid? documentId,
        CancellationToken ct)
    {
        if (request.WarehouseId == Guid.Empty)
            return new(null, Results.BadRequest(new { message = "Выберите склад." }));
        if (request.SupplierId == Guid.Empty)
            return new(null, Results.BadRequest(new { message = "Выберите поставщика." }));

        var warehouseExists = await db.Warehouses.AnyAsync(x =>
            x.Id == request.WarehouseId &&
            x.RestaurantId == restaurantId &&
            x.IsActive, ct);
        if (!warehouseExists)
            return new(null, Results.BadRequest(new { message = "Активный склад не найден." }));

        var supplierExists = await db.Suppliers.AnyAsync(x =>
            x.Id == request.SupplierId &&
            x.RestaurantId == restaurantId &&
            x.IsActive, ct);
        if (!supplierExists)
            return new(null, Results.BadRequest(new { message = "Активный поставщик не найден." }));

        var number = NormalizeOptional(request.Number, 80);
        if (number is not null && await db.StockDocuments.AnyAsync(x =>
            x.RestaurantId == restaurantId &&
            x.Id != documentId &&
            x.Number == number, ct))
        {
            return new(null, Results.Conflict(new { message = "Документ с таким номером уже существует." }));
        }

        var source = request.Lines ?? [];
        if (source.Count == 0)
            return new(null, Results.BadRequest(new { message = "Добавьте хотя бы одну позицию." }));
        if (source.GroupBy(x => x.ProductId).Any(x => x.Count() > 1))
            return new(null, Results.BadRequest(new { message = "Одна позиция не может повторяться в приходной накладной." }));

        var lines = new List<ValidatedReceiptLine>();
        foreach (var sourceLine in source)
        {
            if (sourceLine.ProductId == Guid.Empty)
                return new(null, Results.BadRequest(new { message = "Выберите номенклатуру во всех строках." }));
            if (sourceLine.Quantity <= 0 || sourceLine.Quantity > 1_000_000m)
                return new(null, Results.BadRequest(new { message = "Количество должно быть больше нуля." }));
            if (sourceLine.UnitPrice < 0 || sourceLine.UnitPrice > 1_000_000_000m)
                return new(null, Results.BadRequest(new { message = "Закупочная цена не может быть отрицательной." }));

            var quantity = decimal.Round(sourceLine.Quantity, 3, MidpointRounding.AwayFromZero);
            var unitPrice = Money(sourceLine.UnitPrice);
            lines.Add(new(
                sourceLine.ProductId,
                quantity,
                unitPrice,
                Money(quantity * unitPrice)));
        }

        var productIds = lines.Select(x => x.ProductId).ToArray();
        var validProducts = await db.Products.CountAsync(x =>
            x.RestaurantId == restaurantId &&
            x.IsActive &&
            x.TrackStock &&
            productIds.Contains(x.Id), ct);
        if (validProducts != productIds.Length)
            return new(null, Results.BadRequest(new { message = "Одна или несколько позиций не найдены или складской учёт у них отключён." }));

        return new(lines, null);
    }

    private static string CreateNumber(string prefix, DateTimeOffset now) =>
        $"{prefix}-{now:yyyyMMdd}-{Guid.NewGuid():N}"[..20].ToUpperInvariant();

    private static decimal Money(decimal value) =>
        decimal.Round(value, 4, MidpointRounding.AwayFromZero);

    private static string? NormalizeRequired(string? value, int maxLength)
    {
        var normalized = value?.Trim();
        return string.IsNullOrWhiteSpace(normalized) || normalized.Length > maxLength
            ? null
            : normalized;
    }

    private static string? NormalizeOptional(string? value, int maxLength)
    {
        var normalized = value?.Trim();
        if (string.IsNullOrWhiteSpace(normalized)) return null;
        return normalized.Length <= maxLength ? normalized : normalized[..maxLength];
    }

    private static string EnumText<TEnum>(TEnum value) where TEnum : struct, Enum =>
        System.Text.RegularExpressions.Regex.Replace(value.ToString(), "([a-z0-9])([A-Z])", "$1_$2").ToUpperInvariant();

    private static bool TryClaims(ClaimsPrincipal user, out Guid restaurantId, out Guid employeeId)
    {
        var okRestaurant = Guid.TryParse(user.FindFirstValue("restaurant_id"), out restaurantId);
        var okEmployee = Guid.TryParse(user.FindFirstValue("employee_id"), out employeeId);
        return okRestaurant && okEmployee;
    }

    private static void AddAudit(
        RestaurantDbContext db,
        ClaimsPrincipal user,
        Guid restaurantId,
        string eventType,
        string entityType,
        Guid entityId,
        object payload)
    {
        Guid? employeeId = Guid.TryParse(user.FindFirstValue("employee_id"), out var parsedEmployeeId)
            ? parsedEmployeeId
            : null;

        db.AuditEvents.Add(new AuditEvent
        {
            RestaurantId = restaurantId,
            EmployeeId = employeeId,
            EventType = eventType,
            EntityType = entityType,
            EntityId = entityId,
            PayloadJson = JsonSerializer.Serialize(payload)
        });
    }
}

public sealed record UpsertSupplierRequest(string Name, string Type, string? TaxId, string? Phone);
public sealed record UpdateSupplierRequest(string Name, string Type, string? TaxId, string? Phone, bool IsActive);
public sealed record ReceiptDocumentLineRequest(Guid ProductId, decimal Quantity, decimal UnitPrice);
public sealed record UpsertReceiptDocumentRequest(
    string? Number,
    DateTimeOffset? DocumentDate,
    Guid WarehouseId,
    Guid SupplierId,
    string? Comment,
    IReadOnlyList<ReceiptDocumentLineRequest>? Lines);
