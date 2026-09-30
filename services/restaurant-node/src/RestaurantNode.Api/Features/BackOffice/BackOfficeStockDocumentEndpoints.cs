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

            var taxProfile = await db.Restaurants
                .AsNoTracking()
                .Where(x => x.Id == restaurantId)
                .Select(x => new
                {
                    taxRegime = x.TaxRegime,
                    vatPriceMode = x.VatPriceMode
                })
                .FirstAsync(ct);

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
                taxProfile,
                purchaseDocumentKinds = new[]
                {
                    new { code = TaxPolicy.PurchaseDocumentSupplierInvoice, name = "e-Qaimə / накладная поставщика" },
                    new { code = TaxPolicy.PurchaseDocumentRetailReceipt, name = "Кассовый чек / розничная покупка" },
                    new { code = TaxPolicy.PurchaseDocumentOther, name = "Другой документ" }
                },
                retailVatModes = new[]
                {
                    new { code = TaxPolicy.RetailVatNotSpecified, name = "ƏDV в чеке не указан" },
                    new { code = TaxPolicy.RetailVat18, name = "В чеке указан ƏDV 18%" }
                },
                vatPriceModes = new[]
                {
                    new { code = TaxPolicy.VatPriceIncluded, name = "ƏDV включён в цену" },
                    new { code = TaxPolicy.VatPriceExcluded, name = "Цена без ƏDV" }
                },
                purchaseVatCodes = new[]
                {
                    new { code = TaxPolicy.PurchaseVat18, name = "ƏDV 18%" },
                    new { code = TaxPolicy.PurchaseVatZero, name = "ƏDV 0%" },
                    new { code = TaxPolicy.PurchaseVatExempt, name = "ƏDV-dən azad" },
                    new { code = TaxPolicy.PurchaseNoVat, name = "Без ƏDV" }
                },
                inputVatCreditStatuses = new[]
                {
                    new { code = TaxPolicy.InputVatPending, name = "Ожидает подтверждения" },
                    new { code = TaxPolicy.InputVatEligible, name = "Можно принять к əvəzləşdirmə" },
                    new { code = TaxPolicy.InputVatCredited, name = "Əvəzləşdirilib" },
                    new { code = TaxPolicy.InputVatNonCreditable, name = "Не подлежит əvəzləşdirmə" }
                },
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
                    document.PurchaseSource,
                    document.PurchaseDocumentKind,
                    document.PurchaseReferenceNumber,
                    document.TaxRegimeSnapshot,
                    document.VatPriceMode,
                    document.InputVatCreditStatus,
                    document.EInvoiceNumber,
                    document.NetAmount,
                    document.VatAmount,
                    document.InventoryCostAmount,
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
                            line.VatTaxCode,
                            line.NetAmount,
                            line.VatAmount,
                            line.InventoryCostAmount,
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
                PurchaseSource = "LOCAL",
                PurchaseDocumentKind = NormalizePurchaseDocumentKind(request.PurchaseDocumentKind),
                PurchaseReferenceNumber = NormalizeOptional(request.PurchaseReferenceNumber, 120),
                TaxRegimeSnapshot = validation.TaxRegime!,
                VatPriceMode = validation.VatPriceMode!,
                InputVatCreditStatus = validation.InputVatCreditStatus!,
                EInvoiceNumber = validation.EInvoiceNumber,
                Comment = NormalizeOptional(request.Comment, 500),
                CreatedByEmployeeId = employeeId,
                NetAmount = Money(validation.Lines!.Sum(x => x.NetAmount)),
                VatAmount = Money(validation.Lines!.Sum(x => x.VatAmount)),
                InventoryCostAmount = Money(validation.Lines!.Sum(x => x.InventoryCostAmount)),
                TotalAmount = Money(validation.Lines!.Sum(x => x.Amount))
            };

            foreach (var line in validation.Lines!)
            {
                document.Lines.Add(new StockDocumentLine
                {
                    RestaurantId = restaurantId,
                    ProductId = line.ProductId,
                    Quantity = line.Quantity,
                    UnitPrice = line.UnitPrice,
                    VatTaxCode = line.VatTaxCode,
                    NetAmount = line.NetAmount,
                    VatAmount = line.VatAmount,
                    InventoryCostAmount = line.InventoryCostAmount,
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
                document.PurchaseDocumentKind,
                document.PurchaseReferenceNumber,
                document.TaxRegimeSnapshot,
                document.VatPriceMode,
                document.InputVatCreditStatus,
                document.EInvoiceNumber,
                document.NetAmount,
                document.VatAmount,
                document.InventoryCostAmount,
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
                .FirstOrDefaultAsync(x => x.Id == id && x.RestaurantId == restaurantId, ct);
            if (document is null) return Results.NotFound();

            if (document.Status != StockDocumentStatus.Draft)
                return Results.Conflict(new { message = "Изменять можно только черновик документа." });
            if (document.Type != StockDocumentType.Receipt)
                return Results.BadRequest(new { message = "Этот редактор поддерживает только приходные накладные." });

            var validation = await ValidateReceiptRequestAsync(db, restaurantId, request, id, ct);
            if (validation.Error is not null) return validation.Error;

            await using var tx = await db.Database.BeginTransactionAsync(ct);

            document.Number = NormalizeOptional(request.Number, 80) ?? document.Number;
            document.DocumentDate = request.DocumentDate ?? document.DocumentDate;
            document.WarehouseId = request.WarehouseId;
            document.SupplierId = request.SupplierId;
            document.PurchaseSource = "LOCAL";
            document.PurchaseDocumentKind = NormalizePurchaseDocumentKind(request.PurchaseDocumentKind);
            document.PurchaseReferenceNumber = NormalizeOptional(request.PurchaseReferenceNumber, 120);
            document.TaxRegimeSnapshot = validation.TaxRegime!;
            document.VatPriceMode = validation.VatPriceMode!;
            document.InputVatCreditStatus = validation.InputVatCreditStatus!;
            document.EInvoiceNumber = validation.EInvoiceNumber;
            document.Comment = NormalizeOptional(request.Comment, 500);
            document.NetAmount = Money(validation.Lines!.Sum(x => x.NetAmount));
            document.VatAmount = Money(validation.Lines!.Sum(x => x.VatAmount));
            document.InventoryCostAmount = Money(validation.Lines!.Sum(x => x.InventoryCostAmount));
            document.TotalAmount = Money(validation.Lines!.Sum(x => x.Amount));

            await db.StockDocumentLines
                .Where(x => x.RestaurantId == restaurantId && x.DocumentId == document.Id)
                .ExecuteDeleteAsync(ct);

            foreach (var line in validation.Lines!)
            {
                db.StockDocumentLines.Add(new StockDocumentLine
                {
                    RestaurantId = restaurantId,
                    DocumentId = document.Id,
                    ProductId = line.ProductId,
                    Quantity = line.Quantity,
                    UnitPrice = line.UnitPrice,
                    VatTaxCode = line.VatTaxCode,
                    NetAmount = line.NetAmount,
                    VatAmount = line.VatAmount,
                    InventoryCostAmount = line.InventoryCostAmount,
                    Amount = line.Amount
                });
            }

            AddAudit(db, user, restaurantId, "STOCK_DOCUMENT_UPDATED", "StockDocument", document.Id, new
            {
                document.Number,
                document.WarehouseId,
                document.SupplierId,
                document.DocumentDate,
                document.PurchaseDocumentKind,
                document.PurchaseReferenceNumber,
                document.TaxRegimeSnapshot,
                document.VatPriceMode,
                document.InputVatCreditStatus,
                document.EInvoiceNumber,
                document.NetAmount,
                document.VatAmount,
                document.InventoryCostAmount,
                document.TotalAmount,
                lineCount = validation.Lines!.Count
            });
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);

            return Results.Ok(new { id = document.Id });
        }).RequireAuthorization(Permissions.InventoryManage);

        group.MapDelete("/{id:guid}", async (
            Guid id,
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
                return Results.Conflict(new { message = "Удалить можно только черновик. Проведённый документ нужно сторнировать." });

            AddAudit(db, user, restaurantId, "STOCK_DOCUMENT_DELETED", "StockDocument", document.Id, new
            {
                document.Number,
                type = EnumText(document.Type),
                lineCount = document.Lines.Count,
                document.TotalAmount
            });

            db.StockDocumentLines.RemoveRange(document.Lines);
            db.StockDocuments.Remove(document);
            await db.SaveChangesAsync(ct);

            return Results.NoContent();
        }).RequireAuthorization(Permissions.InventoryManage);

        group.MapPost("/{id:guid}/duplicate", async (
            Guid id,
            ClaimsPrincipal user,
            RestaurantDbContext db,
            CancellationToken ct) =>
        {
            if (!TryClaims(user, out var restaurantId, out var employeeId))
                return Results.Unauthorized();

            var source = await db.StockDocuments
                .AsNoTracking()
                .Include(x => x.Lines)
                .FirstOrDefaultAsync(x => x.Id == id && x.RestaurantId == restaurantId, ct);
            if (source is null) return Results.NotFound();
            if (source.Type != StockDocumentType.Receipt)
                return Results.BadRequest(new { message = "Дублирование сейчас поддерживается только для приходных накладных." });

            var copyResult = await CreateReceiptCopyAsync(
                db,
                restaurantId,
                employeeId,
                source,
                $"Копия документа {source.Number}",
                ct);
            if (copyResult.Error is not null) return copyResult.Error;

            var copy = copyResult.Document!;
            db.StockDocuments.Add(copy);
            AddAudit(db, user, restaurantId, "STOCK_DOCUMENT_DUPLICATED", "StockDocument", copy.Id, new
            {
                sourceDocumentId = source.Id,
                sourceNumber = source.Number,
                copyNumber = copy.Number
            });
            await db.SaveChangesAsync(ct);

            return Results.Ok(new { id = copy.Id });
        }).RequireAuthorization(Permissions.InventoryManage);

        group.MapPut("/{id:guid}/correct", async (
            Guid id,
            UpsertReceiptDocumentRequest request,
            ClaimsPrincipal user,
            RestaurantDbContext db,
            CancellationToken ct) =>
        {
            if (!TryClaims(user, out var restaurantId, out var employeeId))
                return Results.Unauthorized();

            await using var tx = await db.Database.BeginTransactionAsync(ct);

            var document = await db.StockDocuments
                .FirstOrDefaultAsync(x => x.Id == id && x.RestaurantId == restaurantId, ct);
            if (document is null) return Results.NotFound();
            if (document.Type != StockDocumentType.Receipt)
                return Results.BadRequest(new { message = "Исправление сейчас поддерживается только для приходной накладной." });
            if (document.Status != StockDocumentStatus.Posted)
                return Results.Conflict(new { message = "Исправлять можно только проведённый документ." });
            if (!document.WarehouseId.HasValue || !document.SupplierId.HasValue)
                return Results.BadRequest(new { message = "У документа не указан склад или поставщик." });

            var oldLines = await db.StockDocumentLines
                .AsNoTracking()
                .Where(x => x.RestaurantId == restaurantId && x.DocumentId == document.Id)
                .ToListAsync(ct);

            var correctionRequest = request with
            {
                Number = document.Number,
                DocumentDate = document.DocumentDate,
                WarehouseId = document.WarehouseId.Value,
                SupplierId = document.SupplierId.Value,
                PurchaseDocumentKind = document.PurchaseDocumentKind,
                VatPriceMode = document.VatPriceMode,
                InputVatCreditStatus = document.InputVatCreditStatus,
                RetailVatMode = document.PurchaseDocumentKind == TaxPolicy.PurchaseDocumentRetailReceipt
                    ? oldLines.Any(x => x.VatTaxCode == TaxPolicy.PurchaseVat18)
                        ? TaxPolicy.RetailVat18
                        : TaxPolicy.RetailVatNotSpecified
                    : null
            };

            var validation = await ValidateReceiptRequestAsync(
                db,
                restaurantId,
                correctionRequest,
                document.Id,
                document.TaxRegimeSnapshot,
                ct);
            if (validation.Error is not null) return validation.Error;

            var newLines = validation.Lines!;
            var productIds = oldLines.Select(x => x.ProductId)
                .Concat(newLines.Select(x => x.ProductId))
                .Distinct()
                .ToArray();

            var products = await db.Products
                .AsNoTracking()
                .Where(x => x.RestaurantId == restaurantId && productIds.Contains(x.Id))
                .Select(x => new { x.Id, x.InventoryAccountCode })
                .ToDictionaryAsync(x => x.Id, ct);
            if (products.Count != productIds.Length ||
                products.Values.Any(x => !AccountingLedger.IsSupportedInventoryAccountCode(x.InventoryAccountCode)))
            {
                return Results.BadRequest(new { message = "Для одной или нескольких позиций не настроен корректный счёт складского учёта." });
            }

            var operationId = Guid.NewGuid();
            var now = DateTimeOffset.UtcNow;
            var oldByProduct = oldLines.ToDictionary(x => x.ProductId);
            var newByProduct = newLines.ToDictionary(x => x.ProductId);

            foreach (var productId in productIds)
            {
                oldByProduct.TryGetValue(productId, out var oldLine);
                newByProduct.TryGetValue(productId, out var newLine);

                var oldQuantity = oldLine?.Quantity ?? 0m;
                var newQuantity = newLine?.Quantity ?? 0m;
                var oldInventoryCost = oldLine?.InventoryCostAmount ?? 0m;
                var newInventoryCost = newLine?.InventoryCostAmount ?? 0m;
                var quantityDelta = decimal.Round(newQuantity - oldQuantity, 3, MidpointRounding.AwayFromZero);
                var costDelta = Money(newInventoryCost - oldInventoryCost);

                if (quantityDelta == 0m && costDelta == 0m)
                    continue;

                decimal? unitCost = null;
                if (newQuantity > 0m)
                    unitCost = Money(newInventoryCost / newQuantity);
                else if (oldQuantity > 0m)
                    unitCost = Money(oldInventoryCost / oldQuantity);

                db.StockMovements.Add(new StockMovement
                {
                    RestaurantId = restaurantId,
                    WarehouseId = document.WarehouseId.Value,
                    ProductId = productId,
                    EmployeeId = employeeId,
                    OperationId = operationId,
                    Type = "RECEIPT_CORRECTION",
                    QuantityDelta = quantityDelta,
                    UnitCost = unitCost,
                    CostDelta = costDelta,
                    ReferenceType = "STOCK_DOCUMENT_CORRECTION",
                    ReferenceId = document.Id,
                    Note = $"Исправление приходной {document.Number}",
                    CreatedAt = now
                });
            }

            await AccountingLedger.EnsureFoundationAsync(db, restaurantId, ct);
            var payableAccount = await AccountingLedger.EnsureSystemAccountAsync(
                db, restaurantId, AccountingLedger.SupplierPayableKey,
                "531", "Malsatan və podratçılara qısamüddətli kreditor borcları",
                LedgerAccountType.Liability, ct);
            var advanceAccount = await AccountingLedger.EnsureSystemAccountAsync(
                db, restaurantId, AccountingLedger.SupplierAdvanceKey,
                "243", "Verilmiş qısamüddətli avanslar",
                LedgerAccountType.Asset, ct);

            var ledgerLines = new List<AccountingLedger.LineDraft>();
            var inventoryCodes = productIds
                .Select(productId => products[productId].InventoryAccountCode!)
                .Distinct()
                .ToArray();

            foreach (var inventoryCode in inventoryCodes)
            {
                var oldAmount = Money(oldLines
                    .Where(line => products[line.ProductId].InventoryAccountCode == inventoryCode)
                    .Sum(line => line.InventoryCostAmount));
                var newAmount = Money(newLines
                    .Where(line => products[line.ProductId].InventoryAccountCode == inventoryCode)
                    .Sum(line => line.InventoryCostAmount));
                var delta = Money(newAmount - oldAmount);
                if (delta == 0m) continue;

                var inventoryAccount = await AccountingLedger.EnsureInventoryAccountAsync(
                    db, restaurantId, inventoryCode, ct);
                ledgerLines.Add(delta > 0m
                    ? new AccountingLedger.LineDraft(
                        inventoryAccount,
                        Debit: delta,
                        WarehouseId: document.WarehouseId.Value)
                    : new AccountingLedger.LineDraft(
                        inventoryAccount,
                        Credit: -delta,
                        WarehouseId: document.WarehouseId.Value));
            }

            var oldRecoverableVat = TaxPolicy.IsInputVatRecognizedAsRecoverable(
                document.TaxRegimeSnapshot,
                document.InputVatCreditStatus)
                ? Money(document.VatAmount)
                : 0m;
            var newVatAmount = Money(newLines.Sum(x => x.VatAmount));
            var newRecoverableVat = TaxPolicy.IsInputVatRecognizedAsRecoverable(
                document.TaxRegimeSnapshot,
                validation.InputVatCreditStatus!)
                ? newVatAmount
                : 0m;
            var vatDelta = Money(newRecoverableVat - oldRecoverableVat);
            if (vatDelta != 0m)
            {
                var vatAccount = await AccountingLedger.EnsureSystemAccountAsync(
                    db,
                    restaurantId,
                    AccountingLedger.VatRecoverableKey,
                    "241-1",
                    "Əvəzləşdirilən əlavə dəyər vergisi",
                    LedgerAccountType.Asset,
                    ct);
                ledgerLines.Add(vatDelta > 0m
                    ? new AccountingLedger.LineDraft(
                        vatAccount,
                        Debit: vatDelta,
                        SupplierId: document.SupplierId.Value)
                    : new AccountingLedger.LineDraft(
                        vatAccount,
                        Credit: -vatDelta,
                        SupplierId: document.SupplierId.Value));
            }

            var oldTotal = Money(document.TotalAmount);
            var newTotal = Money(newLines.Sum(x => x.Amount));
            var totalDelta = Money(newTotal - oldTotal);
            if (totalDelta != 0m)
            {
                ledgerLines.Add(totalDelta > 0m
                    ? new AccountingLedger.LineDraft(
                        payableAccount,
                        Credit: totalDelta,
                        SupplierId: document.SupplierId.Value)
                    : new AccountingLedger.LineDraft(
                        payableAccount,
                        Debit: -totalDelta,
                        SupplierId: document.SupplierId.Value));
            }

            var correctionReferenceType = $"STOCK_RECEIPT_CORRECTION:{document.Id:N}";
            var relatedEntries = await db.LedgerEntries
                .AsNoTracking()
                .Include(x => x.Lines)
                .Where(x =>
                    x.RestaurantId == restaurantId &&
                    ((x.ReferenceType == "STOCK_RECEIPT" && x.ReferenceId == document.Id) ||
                     x.ReferenceType == correctionReferenceType))
                .ToListAsync(ct);

            var currentlyAppliedAdvance = Money(relatedEntries
                .SelectMany(x => x.Lines)
                .Where(x =>
                    x.AccountId == advanceAccount.Id &&
                    x.SupplierId == document.SupplierId.Value)
                .Sum(x => x.Credit - x.Debit));

            var advanceTotals = await db.LedgerLines
                .AsNoTracking()
                .Where(x =>
                    x.RestaurantId == restaurantId &&
                    x.SupplierId == document.SupplierId.Value &&
                    x.AccountId == advanceAccount.Id)
                .GroupBy(_ => 1)
                .Select(g => new
                {
                    Debit = g.Sum(x => x.Debit),
                    Credit = g.Sum(x => x.Credit)
                })
                .FirstOrDefaultAsync(ct);
            var availableAdvance = Money(Math.Max(
                0m,
                (advanceTotals?.Debit ?? 0m) - (advanceTotals?.Credit ?? 0m)));

            decimal desiredAppliedAdvance;
            if (newTotal >= oldTotal)
            {
                var additionalNeed = Money(newTotal - oldTotal);
                desiredAppliedAdvance = Money(
                    currentlyAppliedAdvance + Math.Min(additionalNeed, availableAdvance));
            }
            else
            {
                desiredAppliedAdvance = Money(Math.Min(currentlyAppliedAdvance, newTotal));
            }

            var appliedAdvanceDelta = Money(desiredAppliedAdvance - currentlyAppliedAdvance);
            if (appliedAdvanceDelta > 0m)
            {
                ledgerLines.Add(new AccountingLedger.LineDraft(
                    payableAccount,
                    Debit: appliedAdvanceDelta,
                    SupplierId: document.SupplierId.Value));
                ledgerLines.Add(new AccountingLedger.LineDraft(
                    advanceAccount,
                    Credit: appliedAdvanceDelta,
                    SupplierId: document.SupplierId.Value));
            }
            else if (appliedAdvanceDelta < 0m)
            {
                var restoreAdvance = -appliedAdvanceDelta;
                ledgerLines.Add(new AccountingLedger.LineDraft(
                    advanceAccount,
                    Debit: restoreAdvance,
                    SupplierId: document.SupplierId.Value));
                ledgerLines.Add(new AccountingLedger.LineDraft(
                    payableAccount,
                    Credit: restoreAdvance,
                    SupplierId: document.SupplierId.Value));
            }

            if (ledgerLines.Count > 0)
            {
                await AccountingLedger.PostAsync(
                    db,
                    restaurantId,
                    correctionReferenceType,
                    operationId,
                    now,
                    $"Исправление приходной накладной {document.Number}",
                    employeeId,
                    ledgerLines,
                    ct);
            }

            var before = new
            {
                document.NetAmount,
                document.VatAmount,
                document.InventoryCostAmount,
                document.TotalAmount,
                lines = oldLines.Select(x => new
                {
                    x.ProductId,
                    x.Quantity,
                    x.UnitPrice,
                    x.VatTaxCode,
                    x.NetAmount,
                    x.VatAmount,
                    x.InventoryCostAmount,
                    x.Amount
                }).ToArray()
            };

            document.PurchaseReferenceNumber = NormalizeOptional(request.PurchaseReferenceNumber, 120);
            document.EInvoiceNumber = validation.EInvoiceNumber;
            document.InputVatCreditStatus = validation.InputVatCreditStatus!;
            document.Comment = NormalizeOptional(request.Comment, 500);
            document.NetAmount = Money(newLines.Sum(x => x.NetAmount));
            document.VatAmount = newVatAmount;
            document.InventoryCostAmount = Money(newLines.Sum(x => x.InventoryCostAmount));
            document.TotalAmount = newTotal;

            await db.StockDocumentLines
                .Where(x => x.RestaurantId == restaurantId && x.DocumentId == document.Id)
                .ExecuteDeleteAsync(ct);

            foreach (var line in newLines)
            {
                db.StockDocumentLines.Add(new StockDocumentLine
                {
                    RestaurantId = restaurantId,
                    DocumentId = document.Id,
                    ProductId = line.ProductId,
                    Quantity = line.Quantity,
                    UnitPrice = line.UnitPrice,
                    VatTaxCode = line.VatTaxCode,
                    NetAmount = line.NetAmount,
                    VatAmount = line.VatAmount,
                    InventoryCostAmount = line.InventoryCostAmount,
                    Amount = line.Amount
                });
            }

            AddAudit(db, user, restaurantId, "STOCK_DOCUMENT_CORRECTED", "StockDocument", document.Id, new
            {
                document.Number,
                correctionOperationId = operationId,
                before,
                after = new
                {
                    document.NetAmount,
                    document.VatAmount,
                    document.InventoryCostAmount,
                    document.TotalAmount,
                    lines = newLines.Select(x => new
                    {
                        x.ProductId,
                        x.Quantity,
                        x.UnitPrice,
                        x.VatTaxCode,
                        x.NetAmount,
                        x.VatAmount,
                        x.InventoryCostAmount,
                        x.Amount
                    }).ToArray()
                }
            });

            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);

            return Results.Ok(new
            {
                id = document.Id,
                status = EnumText(document.Status),
                correctionOperationId = operationId,
                document.TotalAmount
            });
        }).RequireAuthorization(Permissions.InventoryManage);

        group.MapPost("/{id:guid}/reverse", async (
            Guid id,
            ReverseReceiptDocumentRequest request,
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
            if (document.Type != StockDocumentType.Receipt)
                return Results.BadRequest(new { message = "Сторно сейчас поддерживается только для приходной накладной." });
            if (document.Status != StockDocumentStatus.Posted)
                return Results.Conflict(new { message = "Сторнировать можно только проведённый документ." });
            if (!document.WarehouseId.HasValue)
                return Results.BadRequest(new { message = "У документа не указан склад." });

            var alreadyReversed = await db.StockMovements.AnyAsync(x =>
                x.RestaurantId == restaurantId &&
                x.ReferenceType == "STOCK_DOCUMENT_REVERSAL" &&
                x.ReferenceId == document.Id, ct);
            if (alreadyReversed)
                return Results.Conflict(new { message = "Этот документ уже сторнирован." });

            var postedMovements = await db.StockMovements
                .AsNoTracking()
                .Where(x =>
                    x.RestaurantId == restaurantId &&
                    x.ReferenceType == "STOCK_DOCUMENT" &&
                    x.ReferenceId == document.Id)
                .ToListAsync(ct);
            if (postedMovements.Count == 0)
                return Results.Conflict(new { message = "Не найдены исходные складские движения этого документа." });

            var productIds = postedMovements.Select(x => x.ProductId).Distinct().ToArray();
            var currentBalances = await db.StockMovements
                .AsNoTracking()
                .Where(x =>
                    x.RestaurantId == restaurantId &&
                    x.WarehouseId == document.WarehouseId.Value &&
                    productIds.Contains(x.ProductId))
                .GroupBy(x => x.ProductId)
                .Select(group => new
                {
                    ProductId = group.Key,
                    Quantity = group.Sum(x => x.QuantityDelta),
                    StockValue = group.Sum(x => x.CostDelta ?? 0m)
                })
                .ToDictionaryAsync(x => x.ProductId, ct);

            var reversalNeeds = postedMovements
                .GroupBy(x => x.ProductId)
                .Select(group => new
                {
                    ProductId = group.Key,
                    Quantity = group.Sum(x => x.QuantityDelta),
                    StockValue = group.Sum(x => x.CostDelta ?? 0m)
                })
                .ToList();

            foreach (var need in reversalNeeds)
            {
                var balance = currentBalances.GetValueOrDefault(need.ProductId);
                if ((balance?.Quantity ?? 0m) < need.Quantity ||
                    (balance?.StockValue ?? 0m) < need.StockValue)
                {
                    return Results.Conflict(new
                    {
                        message = "Сторно невозможно: часть товара из этой накладной уже израсходована или стоимость остатка недостаточна. Сначала оформите корректирующий складской документ."
                    });
                }
            }

            var reversalOperationId = Guid.NewGuid();
            var now = DateTimeOffset.UtcNow;
            foreach (var movement in postedMovements)
            {
                db.StockMovements.Add(new StockMovement
                {
                    RestaurantId = restaurantId,
                    WarehouseId = movement.WarehouseId,
                    ProductId = movement.ProductId,
                    EmployeeId = employeeId,
                    OperationId = reversalOperationId,
                    Type = "RECEIPT_REVERSAL",
                    QuantityDelta = -movement.QuantityDelta,
                    UnitCost = movement.UnitCost,
                    CostDelta = movement.CostDelta.HasValue ? -movement.CostDelta.Value : null,
                    ReferenceType = "STOCK_DOCUMENT_REVERSAL",
                    ReferenceId = document.Id,
                    Note = NormalizeOptional(request.Reason, 500) ?? $"Сторно приходной {document.Number}",
                    CreatedAt = now
                });
            }

            var originalLedgerEntry = await db.LedgerEntries
                .AsNoTracking()
                .Include(x => x.Lines)
                .ThenInclude(x => x.Account)
                .FirstOrDefaultAsync(x =>
                    x.RestaurantId == restaurantId &&
                    x.ReferenceType == "STOCK_RECEIPT" &&
                    x.ReferenceId == document.Id, ct);

            if (originalLedgerEntry is not null)
            {
                var reversalLines = originalLedgerEntry.Lines.Select(line =>
                    new AccountingLedger.LineDraft(
                        line.Account!,
                        Debit: line.Credit,
                        Credit: line.Debit,
                        SupplierId: line.SupplierId,
                        WarehouseId: line.WarehouseId,
                        MoneyAccountId: line.MoneyAccountId));

                await AccountingLedger.PostAsync(
                    db,
                    restaurantId,
                    "STOCK_RECEIPT_REVERSAL",
                    document.Id,
                    now,
                    $"Сторно приходной накладной {document.Number}",
                    employeeId,
                    reversalLines,
                    ct);
            }

            document.Status = StockDocumentStatus.Cancelled;

            StockDocument? correction = null;
            if (request.CreateCorrectionDraft)
            {
                var copyResult = await CreateReceiptCopyAsync(
                    db,
                    restaurantId,
                    employeeId,
                    document,
                    $"Исправление документа {document.Number}",
                    ct);
                if (copyResult.Error is not null) return copyResult.Error;

                correction = copyResult.Document!;
                db.StockDocuments.Add(correction);
            }

            AddAudit(db, user, restaurantId, "STOCK_DOCUMENT_REVERSED", "StockDocument", document.Id, new
            {
                document.Number,
                reversalOperationId,
                reason = NormalizeOptional(request.Reason, 500),
                correctionDocumentId = correction?.Id
            });

            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);

            return Results.Ok(new
            {
                id = document.Id,
                status = EnumText(document.Status),
                correctionDocumentId = correction?.Id
            });
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
            var products = await db.Products
                .AsNoTracking()
                .Where(x =>
                    x.RestaurantId == restaurantId &&
                    x.IsActive &&
                    x.TrackStock &&
                    productIds.Contains(x.Id))
                .Select(x => new { x.Id, x.InventoryAccountCode })
                .ToDictionaryAsync(x => x.Id, ct);
            if (products.Count != productIds.Length)
                return Results.BadRequest(new { message = "Одна или несколько позиций больше недоступны для складского учёта." });
            if (products.Values.Any(x => !AccountingLedger.IsSupportedInventoryAccountCode(x.InventoryAccountCode)))
                return Results.BadRequest(new { message = "У одной или нескольких позиций не настроен корректный счёт складского учёта." });

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
                    UnitCost = line.Quantity == 0m
                        ? 0m
                        : Money(line.InventoryCostAmount / line.Quantity),
                    CostDelta = line.InventoryCostAmount,
                    ReferenceType = "STOCK_DOCUMENT",
                    ReferenceId = document.Id,
                    Note = document.Comment,
                    CreatedAt = document.DocumentDate
                });
            }

            var negativeCostCorrection =
                await WeightedAverageNegativeStockAccounting.ApplyReceiptAsync(
                    db,
                    restaurantId,
                    employeeId,
                    document.WarehouseId.Value,
                    document.Id,
                    document.DocumentDate,
                    document.Lines,
                    ct);

            if (document.TotalAmount > 0m)
            {
                await AccountingLedger.EnsureFoundationAsync(db, restaurantId, ct);
                var payableAccount = await AccountingLedger.EnsureSystemAccountAsync(
                    db, restaurantId, AccountingLedger.SupplierPayableKey,
                    "531", "Malsatan və podratçılara qısamüddətli kreditor borcları",
                    LedgerAccountType.Liability, ct);
                var advanceAccount = await AccountingLedger.EnsureSystemAccountAsync(
                    db, restaurantId, AccountingLedger.SupplierAdvanceKey,
                    "243", "Verilmiş qısamüddətli avanslar", LedgerAccountType.Asset, ct);

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

                var ledgerLines = new List<AccountingLedger.LineDraft>();
                foreach (var accountGroup in document.Lines
                             .GroupBy(line => products[line.ProductId].InventoryAccountCode!))
                {
                    var amount = AccountingLedger.Money(accountGroup.Sum(x => x.InventoryCostAmount));
                    if (amount <= 0m) continue;

                    var inventoryAccount = await AccountingLedger.EnsureInventoryAccountAsync(
                        db, restaurantId, accountGroup.Key, ct);
                    ledgerLines.Add(new AccountingLedger.LineDraft(
                        inventoryAccount,
                        Debit: amount,
                        WarehouseId: warehouse.Id));
                }

                var recoverableVat = TaxPolicy.IsInputVatRecognizedAsRecoverable(
                    document.TaxRegimeSnapshot,
                    document.InputVatCreditStatus)
                    ? AccountingLedger.Money(document.VatAmount)
                    : 0m;

                if (recoverableVat > 0m)
                {
                    var vatRecoverableAccount = await AccountingLedger.EnsureSystemAccountAsync(
                        db,
                        restaurantId,
                        AccountingLedger.VatRecoverableKey,
                        "241-1",
                        "Əvəzləşdirilən əlavə dəyər vergisi",
                        LedgerAccountType.Asset,
                        ct);

                    ledgerLines.Add(new AccountingLedger.LineDraft(
                        vatRecoverableAccount,
                        Debit: recoverableVat,
                        SupplierId: supplier.Id));
                }

                ledgerLines.Add(new AccountingLedger.LineDraft(
                    payableAccount,
                    Credit: document.TotalAmount,
                    SupplierId: supplier.Id));

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
                document.PurchaseDocumentKind,
                document.PurchaseReferenceNumber,
                document.TaxRegimeSnapshot,
                document.VatPriceMode,
                document.InputVatCreditStatus,
                document.EInvoiceNumber,
                document.NetAmount,
                document.VatAmount,
                document.InventoryCostAmount,
                document.TotalAmount,
                lineCount = document.Lines.Count,
                document.PostedAt,
                negativeStockCostCorrection = new
                {
                    negativeCostCorrection.AdjustedProducts,
                    negativeCostCorrection.CogsAdjustment
                }
            });

            // Persist the receipt first so pending realization acts in the same
            // transaction can see the newly available stock and FIFO layers.
            await db.SaveChangesAsync(ct);

            var inventorySettings = await db.Restaurants
                .AsNoTracking()
                .Where(x => x.Id == restaurantId)
                .Select(x => new
                {
                    x.InventoryCostMethod,
                    x.AllowNegativeRealization
                })
                .FirstAsync(ct);

            var pendingActs = await db.StockDocuments
                .Where(x =>
                    x.RestaurantId == restaurantId &&
                    x.Type == StockDocumentType.Realization &&
                    x.Status == StockDocumentStatus.Draft)
                .Include(x => x.Lines)
                .OrderBy(x => x.DocumentDate)
                .ThenBy(x => x.CreatedAt)
                .ToListAsync(ct);

            var retriedActs = 0;
            var autoPostedActs = 0;
            foreach (var act in pendingActs)
            {
                act.CostMethodSnapshot = inventorySettings.InventoryCostMethod.ToString();

                var retry = await RealizationActAccounting.TryPostActAsync(
                    db,
                    act,
                    restaurantId,
                    employeeId,
                    inventorySettings.InventoryCostMethod,
                    inventorySettings.AllowNegativeRealization,
                    ct);

                retriedActs++;
                if (retry.Posted)
                {
                    autoPostedActs++;
                    AddAudit(
                        db,
                        user,
                        restaurantId,
                        "REALIZATION_ACT_AUTO_POSTED_AFTER_RECEIPT",
                        "StockDocument",
                        act.Id,
                        new
                        {
                            act.Number,
                            receiptDocumentId = document.Id,
                            costMethod = EnumText(inventorySettings.InventoryCostMethod),
                            totalCost = retry.TotalCost
                        });
                }

                // Each posted act must become visible before evaluating the next
                // FIFO act, otherwise two acts could consume the same layer.
                await db.SaveChangesAsync(ct);
            }

            await tx.CommitAsync(ct);

            return Results.Ok(new
            {
                id = document.Id,
                status = EnumText(document.Status),
                document.PostedAt,
                document.TotalAmount,
                negativeStockCostCorrection = new
                {
                    negativeCostCorrection.AdjustedProducts,
                    negativeCostCorrection.CogsAdjustment
                },
                realizationRetry = new
                {
                    retriedActs,
                    postedActs = autoPostedActs,
                    pendingActs = retriedActs - autoPostedActs
                }
            });
        }).RequireAuthorization(Permissions.InventoryManage);

        return app;
    }

    private sealed record ReceiptCopyResult(StockDocument? Document, IResult? Error);

    private static async Task<ReceiptCopyResult> CreateReceiptCopyAsync(
        RestaurantDbContext db,
        Guid restaurantId,
        Guid employeeId,
        StockDocument source,
        string commentPrefix,
        CancellationToken ct)
    {
        if (!source.WarehouseId.HasValue || !source.SupplierId.HasValue)
            return new(null, Results.BadRequest(new { message = "В исходном документе не указан склад или поставщик." }));

        var isRetailReceipt = NormalizePurchaseDocumentKind(source.PurchaseDocumentKind) ==
                              TaxPolicy.PurchaseDocumentRetailReceipt;
        var retailVatMode = isRetailReceipt && source.Lines.Any(x => x.VatTaxCode == TaxPolicy.PurchaseVat18)
            ? TaxPolicy.RetailVat18
            : TaxPolicy.RetailVatNotSpecified;

        var comment = string.IsNullOrWhiteSpace(source.Comment)
            ? commentPrefix
            : $"{commentPrefix}. {source.Comment}";

        var request = new UpsertReceiptDocumentRequest(
            null,
            DateTimeOffset.UtcNow,
            source.WarehouseId.Value,
            source.SupplierId.Value,
            source.PurchaseDocumentKind,
            null,
            isRetailReceipt ? retailVatMode : null,
            source.VatPriceMode,
            source.InputVatCreditStatus,
            null,
            NormalizeOptional(comment, 500),
            source.Lines.Select(line => new ReceiptDocumentLineRequest(
                line.ProductId,
                line.Quantity,
                line.UnitPrice,
                line.VatTaxCode)).ToList());

        var validation = await ValidateReceiptRequestAsync(db, restaurantId, request, null, ct);
        if (validation.Error is not null)
            return new(null, validation.Error);

        var copy = new StockDocument
        {
            RestaurantId = restaurantId,
            Type = StockDocumentType.Receipt,
            Status = StockDocumentStatus.Draft,
            Number = CreateNumber("PR", DateTimeOffset.UtcNow),
            DocumentDate = request.DocumentDate ?? DateTimeOffset.UtcNow,
            WarehouseId = request.WarehouseId,
            SupplierId = request.SupplierId,
            PurchaseSource = "LOCAL",
            PurchaseDocumentKind = NormalizePurchaseDocumentKind(request.PurchaseDocumentKind),
            PurchaseReferenceNumber = null,
            TaxRegimeSnapshot = validation.TaxRegime!,
            VatPriceMode = validation.VatPriceMode!,
            InputVatCreditStatus = validation.InputVatCreditStatus!,
            EInvoiceNumber = null,
            Comment = NormalizeOptional(request.Comment, 500),
            CreatedByEmployeeId = employeeId,
            NetAmount = Money(validation.Lines!.Sum(x => x.NetAmount)),
            VatAmount = Money(validation.Lines!.Sum(x => x.VatAmount)),
            InventoryCostAmount = Money(validation.Lines!.Sum(x => x.InventoryCostAmount)),
            TotalAmount = Money(validation.Lines!.Sum(x => x.Amount))
        };

        foreach (var line in validation.Lines!)
        {
            copy.Lines.Add(new StockDocumentLine
            {
                RestaurantId = restaurantId,
                ProductId = line.ProductId,
                Quantity = line.Quantity,
                UnitPrice = line.UnitPrice,
                VatTaxCode = line.VatTaxCode,
                NetAmount = line.NetAmount,
                VatAmount = line.VatAmount,
                InventoryCostAmount = line.InventoryCostAmount,
                Amount = line.Amount
            });
        }

        return new(copy, null);
    }

    private sealed record ValidatedReceiptLine(
        Guid ProductId,
        decimal Quantity,
        decimal UnitPrice,
        string VatTaxCode,
        decimal NetAmount,
        decimal VatAmount,
        decimal InventoryCostAmount,
        decimal Amount);

    private sealed record ReceiptValidation(
        List<ValidatedReceiptLine>? Lines,
        string? TaxRegime,
        string? VatPriceMode,
        string? InputVatCreditStatus,
        string? EInvoiceNumber,
        IResult? Error);

    private static Task<ReceiptValidation> ValidateReceiptRequestAsync(
        RestaurantDbContext db,
        Guid restaurantId,
        UpsertReceiptDocumentRequest request,
        Guid? documentId,
        CancellationToken ct) =>
        ValidateReceiptRequestAsync(
            db,
            restaurantId,
            request,
            documentId,
            null,
            ct);

    private static async Task<ReceiptValidation> ValidateReceiptRequestAsync(
        RestaurantDbContext db,
        Guid restaurantId,
        UpsertReceiptDocumentRequest request,
        Guid? documentId,
        string? taxRegimeOverride,
        CancellationToken ct)
    {
        if (request.WarehouseId == Guid.Empty)
            return new(null, null, null, null, null, Results.BadRequest(new { message = "Выберите склад." }));
        if (request.SupplierId == Guid.Empty)
            return new(null, null, null, null, null, Results.BadRequest(new { message = "Выберите поставщика." }));

        var warehouseExists = await db.Warehouses.AnyAsync(x =>
            x.Id == request.WarehouseId &&
            x.RestaurantId == restaurantId &&
            x.IsActive, ct);
        if (!warehouseExists)
            return new(null, null, null, null, null, Results.BadRequest(new { message = "Активный склад не найден." }));

        var supplierExists = await db.Suppliers.AnyAsync(x =>
            x.Id == request.SupplierId &&
            x.RestaurantId == restaurantId &&
            x.IsActive, ct);
        if (!supplierExists)
            return new(null, null, null, null, null, Results.BadRequest(new { message = "Активный поставщик не найден." }));

        string taxRegime;
        if (!string.IsNullOrWhiteSpace(taxRegimeOverride))
        {
            taxRegime = taxRegimeOverride.Trim().ToUpperInvariant();
        }
        else
        {
            var restaurantTax = await db.Restaurants
                .AsNoTracking()
                .Where(x => x.Id == restaurantId)
                .Select(x => new { x.TaxRegime })
                .FirstAsync(ct);

            taxRegime = restaurantTax.TaxRegime?.Trim().ToUpperInvariant()
                ?? TaxPolicy.UnconfiguredRegime;
        }

        var purchaseDocumentKind = NormalizePurchaseDocumentKind(request.PurchaseDocumentKind);
        if (!TaxPolicy.IsSupportedPurchaseDocumentKind(purchaseDocumentKind))
            return new(null, null, null, null, null,
                Results.BadRequest(new { message = "Выберите корректный тип документа покупки." }));

        var retailVatMode = string.IsNullOrWhiteSpace(request.RetailVatMode)
            ? TaxPolicy.RetailVatNotSpecified
            : request.RetailVatMode.Trim().ToUpperInvariant();
        if (purchaseDocumentKind == TaxPolicy.PurchaseDocumentRetailReceipt &&
            !TaxPolicy.IsSupportedRetailVatMode(retailVatMode))
        {
            return new(null, null, null, null, null,
                Results.BadRequest(new { message = "Выберите, указан ли ƏDV в кассовом чеке." }));
        }

        var vatPriceMode = purchaseDocumentKind == TaxPolicy.PurchaseDocumentRetailReceipt
            ? TaxPolicy.VatPriceIncluded
            : string.IsNullOrWhiteSpace(request.VatPriceMode)
                ? TaxPolicy.VatPriceIncluded
                : request.VatPriceMode.Trim().ToUpperInvariant();

        if (!TaxPolicy.IsSupportedVatPriceMode(vatPriceMode))
            return new(null, null, null, null, null,
                Results.BadRequest(new { message = "Выберите корректный режим цены ƏDV." }));

        var requestedCreditStatus = purchaseDocumentKind == TaxPolicy.PurchaseDocumentRetailReceipt
            ? TaxPolicy.InputVatNonCreditable
            : string.IsNullOrWhiteSpace(request.InputVatCreditStatus)
                ? TaxPolicy.InputVatPending
                : request.InputVatCreditStatus.Trim().ToUpperInvariant();

        if (!TaxPolicy.IsSupportedInputVatCreditStatus(requestedCreditStatus))
            return new(null, null, null, null, null,
                Results.BadRequest(new { message = "Выберите корректный статус входного ƏDV." }));

        var eInvoiceNumber = purchaseDocumentKind == TaxPolicy.PurchaseDocumentSupplierInvoice
            ? NormalizeOptional(request.EInvoiceNumber, 120)
            : null;

        var number = NormalizeOptional(request.Number, 80);
        if (number is not null && await db.StockDocuments.AnyAsync(x =>
            x.RestaurantId == restaurantId &&
            x.Id != documentId &&
            x.Number == number, ct))
        {
            return new(null, null, null, null, null, Results.Conflict(new { message = "Документ с таким номером уже существует." }));
        }

        var source = request.Lines ?? [];
        if (source.Count == 0)
            return new(null, null, null, null, null, Results.BadRequest(new { message = "Добавьте хотя бы одну позицию." }));
        if (source.GroupBy(x => x.ProductId).Any(x => x.Count() > 1))
            return new(null, null, null, null, null, Results.BadRequest(new { message = "Одна позиция не может повторяться в приходной накладной." }));

        var lines = new List<ValidatedReceiptLine>();
        foreach (var sourceLine in source)
        {
            if (sourceLine.ProductId == Guid.Empty)
                return new(null, null, null, null, null, Results.BadRequest(new { message = "Выберите номенклатуру во всех строках." }));
            if (sourceLine.Quantity <= 0 || sourceLine.Quantity > 1_000_000m)
                return new(null, null, null, null, null, Results.BadRequest(new { message = "Количество должно быть больше нуля." }));
            if (sourceLine.UnitPrice < 0 || sourceLine.UnitPrice > 1_000_000_000m)
                return new(null, null, null, null, null, Results.BadRequest(new { message = "Закупочная цена не может быть отрицательной." }));

            var quantity = decimal.Round(sourceLine.Quantity, 3, MidpointRounding.AwayFromZero);
            var unitPrice = Money(sourceLine.UnitPrice);
            var vatTaxCode = purchaseDocumentKind == TaxPolicy.PurchaseDocumentRetailReceipt
                ? retailVatMode == TaxPolicy.RetailVat18
                    ? TaxPolicy.PurchaseVat18
                    : TaxPolicy.PurchaseNoVat
                : string.IsNullOrWhiteSpace(sourceLine.VatTaxCode)
                    ? TaxPolicy.PurchaseNoVat
                    : sourceLine.VatTaxCode.Trim().ToUpperInvariant();

            if (!TaxPolicy.IsSupportedPurchaseVatCode(vatTaxCode))
                return new(null, null, null, null, null,
                    Results.BadRequest(new { message = "Выберите корректный статус ƏDV для всех строк." }));

            var enteredAmount = Money(quantity * unitPrice);
            var effectiveCreditStatus = purchaseDocumentKind == TaxPolicy.PurchaseDocumentRetailReceipt
                ? TaxPolicy.InputVatNonCreditable
                : taxRegime == TaxPolicy.Vat18Regime
                    ? requestedCreditStatus
                    : TaxPolicy.InputVatNonCreditable;

            var calculation = TaxPolicy.CalculatePurchaseVat(
                enteredAmount,
                vatPriceMode,
                vatTaxCode,
                taxRegime,
                effectiveCreditStatus);

            lines.Add(new(
                sourceLine.ProductId,
                quantity,
                unitPrice,
                vatTaxCode,
                calculation.NetAmount,
                calculation.VatAmount,
                calculation.InventoryCostAmount,
                calculation.GrossAmount));
        }

        var productIds = lines.Select(x => x.ProductId).ToArray();
        var validProducts = await db.Products.CountAsync(x =>
            x.RestaurantId == restaurantId &&
            x.IsActive &&
            x.TrackStock &&
            productIds.Contains(x.Id), ct);
        if (validProducts != productIds.Length)
            return new(null, null, null, null, null, Results.BadRequest(new { message = "Одна или несколько позиций не найдены или складской учёт у них отключён." }));

        var hasVat18 = lines.Any(x => x.VatTaxCode == TaxPolicy.PurchaseVat18);
        var inputVatCreditStatus = !hasVat18
            ? TaxPolicy.InputVatNotApplicable
            : purchaseDocumentKind == TaxPolicy.PurchaseDocumentRetailReceipt
                ? TaxPolicy.InputVatNonCreditable
                : taxRegime == TaxPolicy.Vat18Regime
                    ? requestedCreditStatus
                    : TaxPolicy.InputVatNonCreditable;

        return new(
            lines,
            taxRegime,
            vatPriceMode,
            inputVatCreditStatus,
            eInvoiceNumber,
            null);
    }

    private static string NormalizePurchaseDocumentKind(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? TaxPolicy.PurchaseDocumentSupplierInvoice
            : value.Trim().ToUpperInvariant();

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

public sealed record ReverseReceiptDocumentRequest(bool CreateCorrectionDraft, string? Reason);
public sealed record UpsertSupplierRequest(string Name, string Type, string? TaxId, string? Phone);
public sealed record UpdateSupplierRequest(string Name, string Type, string? TaxId, string? Phone, bool IsActive);
public sealed record ReceiptDocumentLineRequest(
    Guid ProductId,
    decimal Quantity,
    decimal UnitPrice,
    string? VatTaxCode);

public sealed record UpsertReceiptDocumentRequest(
    string? Number,
    DateTimeOffset? DocumentDate,
    Guid WarehouseId,
    Guid SupplierId,
    string? PurchaseDocumentKind,
    string? PurchaseReferenceNumber,
    string? RetailVatMode,
    string? VatPriceMode,
    string? InputVatCreditStatus,
    string? EInvoiceNumber,
    string? Comment,
    IReadOnlyList<ReceiptDocumentLineRequest>? Lines);
