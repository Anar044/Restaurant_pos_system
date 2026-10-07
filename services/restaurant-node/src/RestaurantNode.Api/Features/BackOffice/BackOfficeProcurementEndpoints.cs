using System.Security.Claims;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using RestaurantNode.Api.Domain;
using RestaurantNode.Api.Infrastructure;
using RestaurantNode.Api.Security;

namespace RestaurantNode.Api.Features.BackOffice;

public static class BackOfficeProcurementEndpoints
{
    private const string PurchaseRequisitionReference = "PURCHASE_REQUISITION";
    private const string PurchaseOrderReceiptReferencePrefix = "PURCHASE_ORDER_RECEIPT_";

    public static IEndpointRouteBuilder MapBackOfficeProcurementEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/backoffice/procurement")
            .RequireAuthorization(Permissions.BackOfficeRead);

        group.MapGet("", async (
            ClaimsPrincipal user,
            RestaurantDbContext db,
            CancellationToken ct) =>
        {
            if (!TryClaims(user, out var restaurantId, out _))
                return Results.Unauthorized();

            var warehouses = await db.Warehouses
                .AsNoTracking()
                .Where(x => x.RestaurantId == restaurantId)
                .OrderByDescending(x => x.IsActive)
                .ThenBy(x => x.Name)
                .Select(x => new
                {
                    x.Id,
                    x.Name,
                    x.IsActive
                })
                .ToListAsync(ct);

            var suppliers = await db.Suppliers
                .AsNoTracking()
                .Where(x => x.RestaurantId == restaurantId)
                .OrderByDescending(x => x.IsActive)
                .ThenBy(x => x.Name)
                .Select(x => new
                {
                    x.Id,
                    x.Name,
                    x.IsActive,
                    x.TaxId,
                    x.Phone
                })
                .ToListAsync(ct);

            var items = await db.Products
                .AsNoTracking()
                .Where(x => x.RestaurantId == restaurantId && x.TrackStock)
                .OrderByDescending(x => x.IsActive)
                .ThenBy(x => x.Name)
                .Select(x => new
                {
                    x.Id,
                    x.Name,
                    x.Sku,
                    x.Unit,
                    x.MinStock,
                    x.IsActive
                })
                .ToListAsync(ct);

            var balances = await db.StockMovements
                .AsNoTracking()
                .Where(x => x.RestaurantId == restaurantId)
                .GroupBy(x => x.ProductId)
                .Select(group => new
                {
                    ProductId = group.Key,
                    Quantity = group.Sum(x => x.QuantityDelta)
                })
                .ToListAsync(ct);

            var balanceLookup = balances.ToDictionary(x => x.ProductId, x => x.Quantity);

            var receiptHistory = await db.StockDocuments
                .AsNoTracking()
                .Where(x =>
                    x.RestaurantId == restaurantId &&
                    x.Type == StockDocumentType.Receipt &&
                    x.Status == StockDocumentStatus.Posted)
                .Include(x => x.Lines)
                .OrderByDescending(x => x.DocumentDate)
                .ThenByDescending(x => x.CreatedAt)
                .Take(250)
                .ToListAsync(ct);

            var lastPriceLookup = new Dictionary<Guid, decimal>();
            foreach (var receipt in receiptHistory)
            {
                foreach (var line in receipt.Lines)
                {
                    if (!lastPriceLookup.ContainsKey(line.ProductId))
                        lastPriceLookup[line.ProductId] = line.UnitPrice;
                }
            }

            var procurementDocuments = await db.StockDocuments
                .AsNoTracking()
                .Where(x =>
                    x.RestaurantId == restaurantId &&
                    (x.Type == StockDocumentType.PurchaseRequisition ||
                     x.Type == StockDocumentType.PurchaseOrder))
                .Include(x => x.Lines)
                .OrderByDescending(x => x.DocumentDate)
                .ThenByDescending(x => x.CreatedAt)
                .Take(500)
                .ToListAsync(ct);

            var orderIds = procurementDocuments
                .Where(x => x.Type == StockDocumentType.PurchaseOrder)
                .Select(x => x.Id)
                .ToHashSet();

            var linkedReceipts = await db.StockDocuments
                .AsNoTracking()
                .Where(x =>
                    x.RestaurantId == restaurantId &&
                    x.Type == StockDocumentType.Receipt &&
                    x.ReferenceId.HasValue &&
                    x.ReferenceType != null &&
                    x.ReferenceType.StartsWith(PurchaseOrderReceiptReferencePrefix))
                .Include(x => x.Lines)
                .OrderBy(x => x.DocumentDate)
                .ThenBy(x => x.CreatedAt)
                .ToListAsync(ct);

            linkedReceipts = linkedReceipts
                .Where(x => x.ReferenceId.HasValue && orderIds.Contains(x.ReferenceId.Value))
                .ToList();

            var itemLookup = items.ToDictionary(x => x.Id, x => new
            {
                x.Name,
                x.Sku,
                x.Unit
            });
            var warehouseLookup = warehouses.ToDictionary(x => x.Id, x => x.Name);
            var supplierLookup = suppliers.ToDictionary(x => x.Id, x => x.Name);

            var requisitions = procurementDocuments
                .Where(x => x.Type == StockDocumentType.PurchaseRequisition)
                .Select(document => new
                {
                    id = document.Id,
                    document.Number,
                    status = EnumText(document.Status),
                    document.DocumentDate,
                    document.WarehouseId,
                    warehouseName = document.WarehouseId.HasValue
                        ? warehouseLookup.GetValueOrDefault(document.WarehouseId.Value)
                        : null,
                    document.Comment,
                    document.TotalAmount,
                    document.CreatedByEmployeeId,
                    document.CreatedAt,
                    lines = document.Lines
                        .OrderBy(x => itemLookup.GetValueOrDefault(x.ProductId)?.Name)
                        .Select(line => new
                        {
                            id = line.Id,
                            line.ProductId,
                            productName = itemLookup.GetValueOrDefault(line.ProductId)?.Name ?? "Номенклатура",
                            sku = itemLookup.GetValueOrDefault(line.ProductId)?.Sku,
                            unit = itemLookup.GetValueOrDefault(line.ProductId)?.Unit ?? "pcs",
                            line.Quantity,
                            expectedUnitPrice = line.UnitPrice,
                            line.Amount
                        })
                })
                .ToList();

            var orders = procurementDocuments
                .Where(x => x.Type == StockDocumentType.PurchaseOrder)
                .Select(document =>
                {
                    var receipts = linkedReceipts
                        .Where(x => x.ReferenceId == document.Id)
                        .ToList();
                    var postedReceipts = receipts
                        .Where(x => x.Status == StockDocumentStatus.Posted)
                        .ToList();

                    var receivedByProduct = postedReceipts
                        .SelectMany(x => x.Lines)
                        .GroupBy(x => x.ProductId)
                        .ToDictionary(group => group.Key, group => group.Sum(x => x.Quantity));

                    var orderedQuantity = document.Lines.Sum(x => x.Quantity);
                    var receivedQuantity = document.Lines.Sum(line =>
                        Math.Min(line.Quantity, receivedByProduct.GetValueOrDefault(line.ProductId)));

                    var completed = orderedQuantity > 0m &&
                                    document.Lines.All(line =>
                                        receivedByProduct.GetValueOrDefault(line.ProductId) >= line.Quantity);
                    var partiallyReceived = receivedQuantity > 0m && !completed;

                    var effectiveStatus = document.Status == StockDocumentStatus.Cancelled
                        ? StockDocumentStatus.Cancelled
                        : completed
                            ? StockDocumentStatus.Completed
                            : partiallyReceived
                                ? StockDocumentStatus.PartiallyReceived
                                : document.Status;

                    var priceMismatchCount = postedReceipts
                        .SelectMany(x => x.Lines)
                        .Count(receiptLine =>
                        {
                            var orderLine = document.Lines.FirstOrDefault(x => x.ProductId == receiptLine.ProductId);
                            return orderLine is not null &&
                                   Math.Abs(orderLine.UnitPrice - receiptLine.UnitPrice) > 0.0001m;
                        });

                    var matchStatus = priceMismatchCount > 0
                        ? "PRICE_MISMATCH"
                        : completed
                            ? "MATCHED"
                            : receivedQuantity > 0m
                                ? "PARTIAL"
                                : "OPEN";

                    return new
                    {
                        id = document.Id,
                        document.Number,
                        status = EnumText(document.Status),
                        effectiveStatus = EnumText(effectiveStatus),
                        document.DocumentDate,
                        document.WarehouseId,
                        warehouseName = document.WarehouseId.HasValue
                            ? warehouseLookup.GetValueOrDefault(document.WarehouseId.Value)
                            : null,
                        document.SupplierId,
                        supplierName = document.SupplierId.HasValue
                            ? supplierLookup.GetValueOrDefault(document.SupplierId.Value)
                            : null,
                        requisitionId = document.ReferenceType == PurchaseRequisitionReference
                            ? document.ReferenceId
                            : null,
                        document.Comment,
                        document.TotalAmount,
                        orderedQuantity,
                        receivedQuantity,
                        completionPercent = orderedQuantity <= 0m
                            ? 0m
                            : decimal.Round(receivedQuantity / orderedQuantity * 100m, 1),
                        matchStatus,
                        priceMismatchCount,
                        document.CreatedByEmployeeId,
                        document.CreatedAt,
                        lines = document.Lines
                            .OrderBy(x => itemLookup.GetValueOrDefault(x.ProductId)?.Name)
                            .Select(line => new
                            {
                                id = line.Id,
                                line.ProductId,
                                productName = itemLookup.GetValueOrDefault(line.ProductId)?.Name ?? "Номенклатура",
                                sku = itemLookup.GetValueOrDefault(line.ProductId)?.Sku,
                                unit = itemLookup.GetValueOrDefault(line.ProductId)?.Unit ?? "pcs",
                                line.Quantity,
                                line.UnitPrice,
                                line.Amount,
                                receivedQuantity = receivedByProduct.GetValueOrDefault(line.ProductId),
                                remainingQuantity = Math.Max(
                                    line.Quantity - receivedByProduct.GetValueOrDefault(line.ProductId),
                                    0m)
                            }),
                        receipts = receipts.Select(receipt => new
                        {
                            id = receipt.Id,
                            receipt.Number,
                            status = EnumText(receipt.Status),
                            receipt.DocumentDate,
                            receipt.TotalAmount,
                            receipt.PostedAt
                        })
                    };
                })
                .ToList();

            var needs = items
                .Where(x => x.IsActive && x.MinStock > 0m)
                .Select(item =>
                {
                    var stock = balanceLookup.GetValueOrDefault(item.Id);
                    var shortage = Math.Max(item.MinStock - stock, 0m);
                    return new
                    {
                        id = item.Id,
                        item.Name,
                        item.Sku,
                        item.Unit,
                        minStock = item.MinStock,
                        currentStock = stock,
                        shortage,
                        lastPurchasePrice = lastPriceLookup.TryGetValue(item.Id, out var price)
                            ? price
                            : (decimal?)null,
                        estimatedAmount = lastPriceLookup.TryGetValue(item.Id, out var estimatePrice)
                            ? Money(shortage * estimatePrice)
                            : 0m
                    };
                })
                .Where(x => x.shortage > 0m)
                .OrderByDescending(x => x.shortage)
                .ThenBy(x => x.Name)
                .ToList();

            var activeOrderStatusCodes = new HashSet<string>
            {
                "APPROVED",
                "SENT",
                "CONFIRMED",
                "PARTIALLY_RECEIVED"
            };

            var stats = new
            {
                shortageItems = needs.Count,
                shortageEstimatedAmount = Money(needs.Sum(x => x.estimatedAmount)),
                pendingApprovals = procurementDocuments.Count(x =>
                    x.Type == StockDocumentType.PurchaseRequisition &&
                    x.Status == StockDocumentStatus.PendingApproval),
                activeOrders = orders.Count(x => activeOrderStatusCodes.Contains(x.effectiveStatus)),
                activeOrderAmount = Money(orders
                    .Where(x => activeOrderStatusCodes.Contains(x.effectiveStatus))
                    .Sum(x => x.TotalAmount))
            };

            return Results.Ok(new
            {
                stats,
                warehouses,
                suppliers,
                items = items.Select(x => new
                {
                    x.Id,
                    x.Name,
                    x.Sku,
                    x.Unit,
                    x.MinStock,
                    x.IsActive,
                    currentStock = balanceLookup.GetValueOrDefault(x.Id),
                    lastPurchasePrice = lastPriceLookup.TryGetValue(x.Id, out var price)
                        ? price
                        : (decimal?)null
                }),
                needs,
                requisitions,
                orders
            });
        });

        group.MapPost("/requisitions", async (
            CreatePurchaseRequisitionRequest request,
            ClaimsPrincipal user,
            RestaurantDbContext db,
            CancellationToken ct) =>
        {
            if (!TryClaims(user, out var restaurantId, out var employeeId))
                return Results.Unauthorized();

            if (!await ActiveWarehouseExists(db, restaurantId, request.WarehouseId, ct))
                return Results.BadRequest(new { message = "Выберите активный склад назначения." });

            var validation = await ValidateLinesAsync(
                db,
                restaurantId,
                request.Lines,
                requirePrice: false,
                ct);
            if (validation.Error is not null)
                return validation.Error;

            var now = DateTimeOffset.UtcNow;
            var document = new StockDocument
            {
                RestaurantId = restaurantId,
                Type = StockDocumentType.PurchaseRequisition,
                Status = StockDocumentStatus.Draft,
                Number = CreateNumber("REQ", now),
                DocumentDate = now,
                WarehouseId = request.WarehouseId,
                PurchaseSource = "PROCUREMENT",
                PurchaseDocumentKind = "PURCHASE_REQUISITION",
                TaxRegimeSnapshot = "UNCONFIGURED",
                VatPriceMode = "INCLUDED",
                InputVatCreditStatus = "NOT_APPLICABLE",
                Comment = NormalizeOptional(request.Comment, 500),
                CreatedByEmployeeId = employeeId,
                NetAmount = validation.Lines!.Sum(x => x.Amount),
                InventoryCostAmount = validation.Lines!.Sum(x => x.Amount),
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
                    VatTaxCode = "NO_VAT",
                    NetAmount = line.Amount,
                    InventoryCostAmount = line.Amount,
                    Amount = line.Amount
                });
            }

            db.StockDocuments.Add(document);
            AddAudit(db, user, restaurantId, "PURCHASE_REQUISITION_CREATED", "StockDocument", document.Id, new
            {
                document.Number,
                document.WarehouseId,
                lineCount = document.Lines.Count,
                document.TotalAmount
            });

            await db.SaveChangesAsync(ct);
            return Results.Created($"/api/v1/backoffice/procurement/requisitions/{document.Id}", new
            {
                id = document.Id,
                document.Number,
                status = EnumText(document.Status)
            });
        }).RequireAuthorization(Permissions.InventoryManage);

        group.MapPost("/requisitions/{id:guid}/submit", async (
            Guid id,
            ClaimsPrincipal user,
            RestaurantDbContext db,
            CancellationToken ct) =>
        {
            if (!TryClaims(user, out var restaurantId, out _))
                return Results.Unauthorized();

            var document = await FindProcurementDocument(
                db,
                restaurantId,
                id,
                StockDocumentType.PurchaseRequisition,
                ct);
            if (document is null) return Results.NotFound();

            if (document.Status == StockDocumentStatus.PendingApproval)
                return Results.Ok(new { id = document.Id, status = EnumText(document.Status) });
            if (document.Status != StockDocumentStatus.Draft)
                return Results.Conflict(new { message = "На согласование можно отправить только черновик заявки." });

            document.Status = StockDocumentStatus.PendingApproval;
            AddAudit(db, user, restaurantId, "PURCHASE_REQUISITION_SUBMITTED", "StockDocument", document.Id, new
            {
                document.Number
            });
            await db.SaveChangesAsync(ct);

            return Results.Ok(new { id = document.Id, status = EnumText(document.Status) });
        }).RequireAuthorization(Permissions.InventoryManage);

        group.MapPost("/requisitions/{id:guid}/approve", async (
            Guid id,
            ClaimsPrincipal user,
            RestaurantDbContext db,
            CancellationToken ct) =>
        {
            if (!TryClaims(user, out var restaurantId, out _))
                return Results.Unauthorized();

            var document = await FindProcurementDocument(
                db,
                restaurantId,
                id,
                StockDocumentType.PurchaseRequisition,
                ct);
            if (document is null) return Results.NotFound();

            if (document.Status == StockDocumentStatus.Approved)
                return Results.Ok(new { id = document.Id, status = EnumText(document.Status) });
            if (document.Status != StockDocumentStatus.PendingApproval)
                return Results.Conflict(new { message = "Согласовать можно только заявку, ожидающую согласования." });

            document.Status = StockDocumentStatus.Approved;
            AddAudit(db, user, restaurantId, "PURCHASE_REQUISITION_APPROVED", "StockDocument", document.Id, new
            {
                document.Number
            });
            await db.SaveChangesAsync(ct);

            return Results.Ok(new { id = document.Id, status = EnumText(document.Status) });
        }).RequireAuthorization(Permissions.InventoryManage);

        group.MapPost("/requisitions/{id:guid}/cancel", async (
            Guid id,
            ClaimsPrincipal user,
            RestaurantDbContext db,
            CancellationToken ct) =>
        {
            if (!TryClaims(user, out var restaurantId, out _))
                return Results.Unauthorized();

            var document = await FindProcurementDocument(
                db,
                restaurantId,
                id,
                StockDocumentType.PurchaseRequisition,
                ct);
            if (document is null) return Results.NotFound();

            var hasOrder = await db.StockDocuments.AnyAsync(x =>
                x.RestaurantId == restaurantId &&
                x.Type == StockDocumentType.PurchaseOrder &&
                x.ReferenceType == PurchaseRequisitionReference &&
                x.ReferenceId == document.Id &&
                x.Status != StockDocumentStatus.Cancelled,
                ct);
            if (hasOrder)
                return Results.Conflict(new { message = "По заявке уже создан активный заказ поставщику." });

            document.Status = StockDocumentStatus.Cancelled;
            AddAudit(db, user, restaurantId, "PURCHASE_REQUISITION_CANCELLED", "StockDocument", document.Id, new
            {
                document.Number
            });
            await db.SaveChangesAsync(ct);

            return Results.Ok(new { id = document.Id, status = EnumText(document.Status) });
        }).RequireAuthorization(Permissions.InventoryManage);

        group.MapPost("/orders", async (
            CreatePurchaseOrderRequest request,
            ClaimsPrincipal user,
            RestaurantDbContext db,
            CancellationToken ct) =>
        {
            if (!TryClaims(user, out var restaurantId, out var employeeId))
                return Results.Unauthorized();

            var requisition = await db.StockDocuments
                .Include(x => x.Lines)
                .FirstOrDefaultAsync(x =>
                    x.Id == request.RequisitionId &&
                    x.RestaurantId == restaurantId &&
                    x.Type == StockDocumentType.PurchaseRequisition,
                    ct);
            if (requisition is null)
                return Results.BadRequest(new { message = "Заявка на закупку не найдена." });
            if (requisition.Status != StockDocumentStatus.Approved)
                return Results.Conflict(new { message = "Заказ поставщику можно создать только из согласованной заявки." });
            if (!requisition.WarehouseId.HasValue)
                return Results.BadRequest(new { message = "В заявке не указан склад назначения." });

            var supplierExists = await db.Suppliers.AnyAsync(x =>
                x.Id == request.SupplierId &&
                x.RestaurantId == restaurantId &&
                x.IsActive,
                ct);
            if (!supplierExists)
                return Results.BadRequest(new { message = "Выберите активного поставщика." });

            var existingOrder = await db.StockDocuments.AnyAsync(x =>
                x.RestaurantId == restaurantId &&
                x.Type == StockDocumentType.PurchaseOrder &&
                x.ReferenceType == PurchaseRequisitionReference &&
                x.ReferenceId == requisition.Id &&
                x.Status != StockDocumentStatus.Cancelled,
                ct);
            if (existingOrder)
                return Results.Conflict(new { message = "По этой заявке уже есть активный заказ поставщику." });

            var validation = await ValidateLinesAsync(
                db,
                restaurantId,
                request.Lines,
                requirePrice: true,
                ct);
            if (validation.Error is not null)
                return validation.Error;

            var requisitionProducts = requisition.Lines.Select(x => x.ProductId).ToHashSet();
            if (validation.Lines!.Any(x => !requisitionProducts.Contains(x.ProductId)))
                return Results.BadRequest(new { message = "В заказ нельзя добавить позиции, которых нет в исходной заявке." });

            var now = DateTimeOffset.UtcNow;
            var order = new StockDocument
            {
                RestaurantId = restaurantId,
                Type = StockDocumentType.PurchaseOrder,
                Status = StockDocumentStatus.Approved,
                Number = CreateNumber("PO", now),
                DocumentDate = now,
                WarehouseId = requisition.WarehouseId,
                SupplierId = request.SupplierId,
                PurchaseSource = "PROCUREMENT",
                PurchaseDocumentKind = "PURCHASE_ORDER",
                ReferenceType = PurchaseRequisitionReference,
                ReferenceId = requisition.Id,
                TaxRegimeSnapshot = "UNCONFIGURED",
                VatPriceMode = "INCLUDED",
                InputVatCreditStatus = "NOT_APPLICABLE",
                Comment = NormalizeOptional(request.Comment, 500),
                CreatedByEmployeeId = employeeId,
                NetAmount = Money(validation.Lines.Sum(x => x.Amount)),
                InventoryCostAmount = Money(validation.Lines.Sum(x => x.Amount)),
                TotalAmount = Money(validation.Lines.Sum(x => x.Amount))
            };

            foreach (var line in validation.Lines)
            {
                order.Lines.Add(new StockDocumentLine
                {
                    RestaurantId = restaurantId,
                    ProductId = line.ProductId,
                    Quantity = line.Quantity,
                    UnitPrice = line.UnitPrice,
                    VatTaxCode = "NO_VAT",
                    NetAmount = line.Amount,
                    InventoryCostAmount = line.Amount,
                    Amount = line.Amount
                });
            }

            db.StockDocuments.Add(order);
            AddAudit(db, user, restaurantId, "PURCHASE_ORDER_CREATED", "StockDocument", order.Id, new
            {
                order.Number,
                requisitionId = requisition.Id,
                order.SupplierId,
                order.WarehouseId,
                lineCount = order.Lines.Count,
                order.TotalAmount
            });
            await db.SaveChangesAsync(ct);

            return Results.Created($"/api/v1/backoffice/procurement/orders/{order.Id}", new
            {
                id = order.Id,
                order.Number,
                status = EnumText(order.Status)
            });
        }).RequireAuthorization(Permissions.InventoryManage);

        group.MapPost("/orders/{id:guid}/send", async (
            Guid id,
            ClaimsPrincipal user,
            RestaurantDbContext db,
            CancellationToken ct) =>
        {
            if (!TryClaims(user, out var restaurantId, out _))
                return Results.Unauthorized();

            var order = await FindProcurementDocument(
                db,
                restaurantId,
                id,
                StockDocumentType.PurchaseOrder,
                ct);
            if (order is null) return Results.NotFound();

            if (order.Status == StockDocumentStatus.Sent)
                return Results.Ok(new { id = order.Id, status = EnumText(order.Status) });
            if (order.Status != StockDocumentStatus.Approved)
                return Results.Conflict(new { message = "Отправить поставщику можно только согласованный заказ." });

            order.Status = StockDocumentStatus.Sent;
            AddAudit(db, user, restaurantId, "PURCHASE_ORDER_SENT", "StockDocument", order.Id, new { order.Number });
            await db.SaveChangesAsync(ct);
            return Results.Ok(new { id = order.Id, status = EnumText(order.Status) });
        }).RequireAuthorization(Permissions.InventoryManage);

        group.MapPost("/orders/{id:guid}/confirm", async (
            Guid id,
            ClaimsPrincipal user,
            RestaurantDbContext db,
            CancellationToken ct) =>
        {
            if (!TryClaims(user, out var restaurantId, out _))
                return Results.Unauthorized();

            var order = await FindProcurementDocument(
                db,
                restaurantId,
                id,
                StockDocumentType.PurchaseOrder,
                ct);
            if (order is null) return Results.NotFound();

            if (order.Status == StockDocumentStatus.Confirmed)
                return Results.Ok(new { id = order.Id, status = EnumText(order.Status) });
            if (order.Status != StockDocumentStatus.Sent)
                return Results.Conflict(new { message = "Подтвердить можно только отправленный поставщику заказ." });

            order.Status = StockDocumentStatus.Confirmed;
            AddAudit(db, user, restaurantId, "PURCHASE_ORDER_CONFIRMED", "StockDocument", order.Id, new { order.Number });
            await db.SaveChangesAsync(ct);
            return Results.Ok(new { id = order.Id, status = EnumText(order.Status) });
        }).RequireAuthorization(Permissions.InventoryManage);

        group.MapPost("/orders/{id:guid}/cancel", async (
            Guid id,
            ClaimsPrincipal user,
            RestaurantDbContext db,
            CancellationToken ct) =>
        {
            if (!TryClaims(user, out var restaurantId, out _))
                return Results.Unauthorized();

            var order = await FindProcurementDocument(
                db,
                restaurantId,
                id,
                StockDocumentType.PurchaseOrder,
                ct);
            if (order is null) return Results.NotFound();

            var hasPostedReceipt = await db.StockDocuments.AnyAsync(x =>
                x.RestaurantId == restaurantId &&
                x.Type == StockDocumentType.Receipt &&
                x.Status == StockDocumentStatus.Posted &&
                x.ReferenceId == order.Id &&
                x.ReferenceType != null &&
                x.ReferenceType.StartsWith(PurchaseOrderReceiptReferencePrefix),
                ct);
            if (hasPostedReceipt)
                return Results.Conflict(new { message = "Нельзя отменить заказ после фактической приёмки товара." });

            var requisitionId = order.ReferenceType == PurchaseRequisitionReference
                ? order.ReferenceId
                : null;

            order.Status = StockDocumentStatus.Cancelled;
            order.ReferenceType = "CANCELLED_PURCHASE_REQUISITION";

            AddAudit(db, user, restaurantId, "PURCHASE_ORDER_CANCELLED", "StockDocument", order.Id, new
            {
                order.Number,
                requisitionId
            });
            await db.SaveChangesAsync(ct);
            return Results.Ok(new { id = order.Id, status = EnumText(order.Status) });
        }).RequireAuthorization(Permissions.InventoryManage);

        group.MapPost("/orders/{id:guid}/receipt-draft", async (
            Guid id,
            ClaimsPrincipal user,
            RestaurantDbContext db,
            CancellationToken ct) =>
        {
            if (!TryClaims(user, out var restaurantId, out var employeeId))
                return Results.Unauthorized();

            var order = await db.StockDocuments
                .Include(x => x.Lines)
                .FirstOrDefaultAsync(x =>
                    x.Id == id &&
                    x.RestaurantId == restaurantId &&
                    x.Type == StockDocumentType.PurchaseOrder,
                    ct);
            if (order is null) return Results.NotFound();
            if (order.Status == StockDocumentStatus.Cancelled)
                return Results.Conflict(new { message = "Отменённый заказ нельзя принимать." });
            if (!order.WarehouseId.HasValue || !order.SupplierId.HasValue)
                return Results.BadRequest(new { message = "В заказе не указан склад или поставщик." });

            var existingDraft = await db.StockDocuments
                .AsNoTracking()
                .Where(x =>
                    x.RestaurantId == restaurantId &&
                    x.Type == StockDocumentType.Receipt &&
                    x.Status == StockDocumentStatus.Draft &&
                    x.ReferenceId == order.Id &&
                    x.ReferenceType != null &&
                    x.ReferenceType.StartsWith(PurchaseOrderReceiptReferencePrefix))
                .OrderByDescending(x => x.CreatedAt)
                .FirstOrDefaultAsync(ct);

            if (existingDraft is not null)
            {
                return Results.Ok(new
                {
                    id = existingDraft.Id,
                    existing = true,
                    existingDraft.Number
                });
            }

            var postedReceipts = await db.StockDocuments
                .AsNoTracking()
                .Where(x =>
                    x.RestaurantId == restaurantId &&
                    x.Type == StockDocumentType.Receipt &&
                    x.Status == StockDocumentStatus.Posted &&
                    x.ReferenceId == order.Id &&
                    x.ReferenceType != null &&
                    x.ReferenceType.StartsWith(PurchaseOrderReceiptReferencePrefix))
                .Include(x => x.Lines)
                .ToListAsync(ct);

            var receivedByProduct = postedReceipts
                .SelectMany(x => x.Lines)
                .GroupBy(x => x.ProductId)
                .ToDictionary(group => group.Key, group => group.Sum(x => x.Quantity));

            var remaining = order.Lines
                .Select(line => new
                {
                    Line = line,
                    Quantity = Math.Max(
                        line.Quantity - receivedByProduct.GetValueOrDefault(line.ProductId),
                        0m)
                })
                .Where(x => x.Quantity > 0m)
                .ToList();

            if (remaining.Count == 0)
                return Results.Conflict(new { message = "Заказ уже полностью принят." });

            var restaurant = await db.Restaurants
                .AsNoTracking()
                .Where(x => x.Id == restaurantId)
                .Select(x => new
                {
                    x.TaxRegime,
                    x.VatPriceMode
                })
                .FirstAsync(ct);

            var receiptSequence = await db.StockDocuments.CountAsync(x =>
                x.RestaurantId == restaurantId &&
                x.Type == StockDocumentType.Receipt &&
                x.ReferenceId == order.Id &&
                x.ReferenceType != null &&
                x.ReferenceType.StartsWith(PurchaseOrderReceiptReferencePrefix),
                ct) + 1;

            var now = DateTimeOffset.UtcNow;
            var receipt = new StockDocument
            {
                RestaurantId = restaurantId,
                Type = StockDocumentType.Receipt,
                Status = StockDocumentStatus.Draft,
                Number = CreateNumber("RC", now),
                DocumentDate = now,
                WarehouseId = order.WarehouseId,
                SupplierId = order.SupplierId,
                PurchaseSource = "PROCUREMENT",
                PurchaseDocumentKind = TaxPolicy.PurchaseDocumentSupplierInvoice,
                PurchaseReferenceNumber = null,
                ReferenceType = PurchaseOrderReceiptReferencePrefix + receiptSequence,
                ReferenceId = order.Id,
                TaxRegimeSnapshot = restaurant.TaxRegime,
                VatPriceMode = restaurant.VatPriceMode,
                InputVatCreditStatus = restaurant.TaxRegime == TaxPolicy.Vat18Regime
                    ? TaxPolicy.InputVatPending
                    : TaxPolicy.InputVatNonCreditable,
                Comment = $"Приёмка по заказу {order.Number}",
                CreatedByEmployeeId = employeeId
            };

            foreach (var remainingLine in remaining)
            {
                var amount = Money(remainingLine.Quantity * remainingLine.Line.UnitPrice);
                receipt.Lines.Add(new StockDocumentLine
                {
                    RestaurantId = restaurantId,
                    ProductId = remainingLine.Line.ProductId,
                    Quantity = remainingLine.Quantity,
                    UnitPrice = remainingLine.Line.UnitPrice,
                    VatTaxCode = TaxPolicy.PurchaseNoVat,
                    NetAmount = amount,
                    VatAmount = 0m,
                    InventoryCostAmount = amount,
                    Amount = amount
                });
            }

            receipt.NetAmount = Money(receipt.Lines.Sum(x => x.NetAmount));
            receipt.VatAmount = 0m;
            receipt.InventoryCostAmount = Money(receipt.Lines.Sum(x => x.InventoryCostAmount));
            receipt.TotalAmount = Money(receipt.Lines.Sum(x => x.Amount));

            db.StockDocuments.Add(receipt);
            AddAudit(db, user, restaurantId, "PURCHASE_ORDER_RECEIPT_DRAFT_CREATED", "StockDocument", receipt.Id, new
            {
                orderId = order.Id,
                order.Number,
                receipt.Number,
                receipt.WarehouseId,
                receipt.SupplierId,
                lineCount = receipt.Lines.Count,
                receipt.TotalAmount
            });
            await db.SaveChangesAsync(ct);

            return Results.Created($"/api/v1/backoffice/stock-documents/{receipt.Id}", new
            {
                id = receipt.Id,
                existing = false,
                receipt.Number
            });
        }).RequireAuthorization(Permissions.InventoryManage);

        return app;
    }

    private sealed record ValidatedProcurementLine(
        Guid ProductId,
        decimal Quantity,
        decimal UnitPrice,
        decimal Amount);

    private sealed record ProcurementLineValidation(
        List<ValidatedProcurementLine>? Lines,
        IResult? Error);

    private static async Task<ProcurementLineValidation> ValidateLinesAsync(
        RestaurantDbContext db,
        Guid restaurantId,
        IReadOnlyList<ProcurementLineRequest>? source,
        bool requirePrice,
        CancellationToken ct)
    {
        if (source is null || source.Count == 0)
            return new(null, Results.BadRequest(new { message = "Добавьте хотя бы одну позицию." }));
        if (source.GroupBy(x => x.ProductId).Any(x => x.Count() > 1))
            return new(null, Results.BadRequest(new { message = "Одна позиция не может повторяться в документе." }));

        var productIds = source.Select(x => x.ProductId).ToArray();
        if (productIds.Any(x => x == Guid.Empty))
            return new(null, Results.BadRequest(new { message = "Выберите номенклатуру во всех строках." }));

        var validProducts = await db.Products.CountAsync(x =>
            x.RestaurantId == restaurantId &&
            x.IsActive &&
            x.TrackStock &&
            productIds.Contains(x.Id),
            ct);
        if (validProducts != productIds.Length)
            return new(null, Results.BadRequest(new { message = "Одна или несколько позиций недоступны для складского учёта." }));

        var lines = new List<ValidatedProcurementLine>();
        foreach (var line in source)
        {
            if (line.Quantity <= 0m || line.Quantity > 1_000_000m)
                return new(null, Results.BadRequest(new { message = "Количество должно быть больше нуля." }));
            if (line.UnitPrice < 0m || line.UnitPrice > 1_000_000_000m)
                return new(null, Results.BadRequest(new { message = "Цена не может быть отрицательной." }));
            if (requirePrice && line.UnitPrice <= 0m)
                return new(null, Results.BadRequest(new { message = "Для заказа поставщику укажите цену по каждой позиции." }));

            var quantity = decimal.Round(line.Quantity, 3, MidpointRounding.AwayFromZero);
            var unitPrice = Money(line.UnitPrice);
            lines.Add(new(
                line.ProductId,
                quantity,
                unitPrice,
                Money(quantity * unitPrice)));
        }

        return new(lines, null);
    }

    private static Task<bool> ActiveWarehouseExists(
        RestaurantDbContext db,
        Guid restaurantId,
        Guid warehouseId,
        CancellationToken ct) =>
        db.Warehouses.AnyAsync(x =>
            x.Id == warehouseId &&
            x.RestaurantId == restaurantId &&
            x.IsActive,
            ct);

    private static Task<StockDocument?> FindProcurementDocument(
        RestaurantDbContext db,
        Guid restaurantId,
        Guid id,
        StockDocumentType type,
        CancellationToken ct) =>
        db.StockDocuments.FirstOrDefaultAsync(x =>
            x.Id == id &&
            x.RestaurantId == restaurantId &&
            x.Type == type,
            ct);

    private static string CreateNumber(string prefix, DateTimeOffset now) =>
        $"{prefix}-{now:yyyyMMdd}-{Guid.NewGuid():N}"[..Math.Min(20, prefix.Length + 1 + 8 + 1 + 6)].ToUpperInvariant();

    private static decimal Money(decimal value) =>
        decimal.Round(value, 4, MidpointRounding.AwayFromZero);

    private static string? NormalizeOptional(string? value, int maxLength)
    {
        var normalized = value?.Trim();
        if (string.IsNullOrWhiteSpace(normalized)) return null;
        return normalized.Length <= maxLength ? normalized : normalized[..maxLength];
    }

    private static string EnumText<TEnum>(TEnum value)
        where TEnum : struct, Enum =>
        System.Text.RegularExpressions.Regex.Replace(
            value.ToString(),
            "([a-z0-9])([A-Z])",
            "$1_$2").ToUpperInvariant();

    private static bool TryClaims(
        ClaimsPrincipal user,
        out Guid restaurantId,
        out Guid employeeId)
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
        Guid? employeeId = Guid.TryParse(
            user.FindFirstValue("employee_id"),
            out var parsedEmployeeId)
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

public sealed record ProcurementLineRequest(
    Guid ProductId,
    decimal Quantity,
    decimal UnitPrice);

public sealed record CreatePurchaseRequisitionRequest(
    Guid WarehouseId,
    string? Comment,
    IReadOnlyList<ProcurementLineRequest>? Lines);

public sealed record CreatePurchaseOrderRequest(
    Guid RequisitionId,
    Guid SupplierId,
    string? Comment,
    IReadOnlyList<ProcurementLineRequest>? Lines);
