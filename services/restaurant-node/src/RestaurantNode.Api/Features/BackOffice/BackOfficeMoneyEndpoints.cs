using System.Security.Claims;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using RestaurantNode.Api.Domain;
using RestaurantNode.Api.Infrastructure;
using RestaurantNode.Api.Security;

namespace RestaurantNode.Api.Features.BackOffice;

public static class BackOfficeMoneyEndpoints
{
    public static IEndpointRouteBuilder MapBackOfficeMoneyEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/backoffice/money")
            .RequireAuthorization(Permissions.BackOfficeRead);

        group.MapGet("", async (
            DateTimeOffset? from,
            DateTimeOffset? to,
            ClaimsPrincipal user,
            RestaurantDbContext db,
            CancellationToken ct) =>
        {
            if (!TryClaims(user, out var restaurantId, out _))
                return Results.Unauthorized();

            var now = DateTimeOffset.UtcNow;
            var periodTo = to ?? now.AddMinutes(1);
            var periodFrom = from ?? periodTo.AddDays(-30);

            if (periodTo <= periodFrom)
                return Results.BadRequest(new { message = "Period end must be after period start." });
            if (periodTo - periodFrom > TimeSpan.FromDays(366))
                return Results.BadRequest(new { message = "Money period cannot exceed 366 days." });

            var accounts = await db.MoneyAccounts
                .AsNoTracking()
                .Where(x => x.RestaurantId == restaurantId)
                .OrderByDescending(x => x.IsActive)
                .ThenBy(x => x.Name)
                .ToListAsync(ct);

            var categories = await db.MoneyCategories
                .AsNoTracking()
                .Where(x => x.RestaurantId == restaurantId)
                .OrderBy(x => x.Direction)
                .ThenByDescending(x => x.IsActive)
                .ThenBy(x => x.Name)
                .ToListAsync(ct);

            var accountBalances = await db.MoneyTransactions
                .AsNoTracking()
                .Where(x => x.RestaurantId == restaurantId)
                .GroupBy(x => x.AccountId)
                .Select(g => new
                {
                    AccountId = g.Key,
                    Balance = g.Sum(x => x.Direction == MoneyDirection.Income ? x.Amount : -x.Amount)
                })
                .ToDictionaryAsync(x => x.AccountId, x => x.Balance, ct);

            var periodTransactions = await db.MoneyTransactions
                .AsNoTracking()
                .Where(x =>
                    x.RestaurantId == restaurantId &&
                    x.OccurredAt >= periodFrom &&
                    x.OccurredAt < periodTo)
                .OrderByDescending(x => x.OccurredAt)
                .ThenByDescending(x => x.CreatedAt)
                .Take(500)
                .ToListAsync(ct);

            var accountLookup = accounts.ToDictionary(x => x.Id, x => x.Name);
            var categoryLookup = categories.ToDictionary(x => x.Id, x => x.Name);

            var employeeIds = periodTransactions
                .Select(x => x.EmployeeId)
                .Distinct()
                .ToArray();
            var employeeLookup = await db.Employees
                .AsNoTracking()
                .Where(x => x.RestaurantId == restaurantId && employeeIds.Contains(x.Id))
                .ToDictionaryAsync(x => x.Id, x => x.Name, ct);

            var income = Money(periodTransactions
                .Where(x => x.Direction == MoneyDirection.Income)
                .Sum(x => x.Amount));
            var expense = Money(periodTransactions
                .Where(x => x.Direction == MoneyDirection.Expense)
                .Sum(x => x.Amount));

            return Results.Ok(new
            {
                period = new { from = periodFrom, to = periodTo },
                summary = new
                {
                    income,
                    expense,
                    net = Money(income - expense),
                    totalBalance = Money(accountBalances.Values.Sum())
                },
                accounts = accounts.Select(x => new
                {
                    id = x.Id,
                    name = x.Name,
                    type = EnumText(x.Type),
                    isActive = x.IsActive,
                    balance = Money(accountBalances.GetValueOrDefault(x.Id)),
                    createdAt = x.CreatedAt
                }),
                categories = categories.Select(x => new
                {
                    id = x.Id,
                    name = x.Name,
                    direction = EnumText(x.Direction),
                    isActive = x.IsActive,
                    createdAt = x.CreatedAt
                }),
                transactions = periodTransactions.Select(x => new
                {
                    id = x.Id,
                    accountId = x.AccountId,
                    accountName = accountLookup.GetValueOrDefault(x.AccountId) ?? "Счёт",
                    categoryId = x.CategoryId,
                    categoryName = categoryLookup.GetValueOrDefault(x.CategoryId) ?? "Категория",
                    employeeId = x.EmployeeId,
                    employeeName = employeeLookup.GetValueOrDefault(x.EmployeeId) ?? "Сотрудник",
                    direction = EnumText(x.Direction),
                    amount = x.Amount,
                    x.Note,
                    x.ReferenceType,
                    x.ReferenceId,
                    x.OccurredAt,
                    x.CreatedAt
                })
            });
        });

        group.MapPost("/accounts", async (
            UpsertMoneyAccountRequest request,
            ClaimsPrincipal user,
            RestaurantDbContext db,
            CancellationToken ct) =>
        {
            if (!TryClaims(user, out var restaurantId, out _))
                return Results.Unauthorized();

            var name = NormalizeRequired(request.Name, 120);
            if (name is null)
                return Results.BadRequest(new { message = "Название счёта обязательно." });
            if (!TryAccountType(request.Type, out var type))
                return Results.BadRequest(new { message = "Тип счёта должен быть CASH, BANK, CARD или OTHER." });

            if (await db.MoneyAccounts.AnyAsync(
                x => x.RestaurantId == restaurantId && x.Name == name, ct))
                return Results.Conflict(new { message = "Счёт с таким названием уже существует." });

            var account = new MoneyAccount
            {
                RestaurantId = restaurantId,
                Name = name,
                Type = type,
                IsActive = true
            };
            db.MoneyAccounts.Add(account);
            AddAudit(db, user, restaurantId, "MONEY_ACCOUNT_CREATED", "MoneyAccount", account.Id, new
            {
                account.Name,
                type = EnumText(account.Type)
            });
            await db.SaveChangesAsync(ct);

            return Results.Created($"/api/v1/backoffice/money/accounts/{account.Id}", new { id = account.Id });
        }).RequireAuthorization(Permissions.FinanceManage);

        group.MapPut("/accounts/{id:guid}", async (
            Guid id,
            UpdateMoneyAccountRequest request,
            ClaimsPrincipal user,
            RestaurantDbContext db,
            CancellationToken ct) =>
        {
            if (!TryClaims(user, out var restaurantId, out _))
                return Results.Unauthorized();

            var account = await db.MoneyAccounts.FirstOrDefaultAsync(
                x => x.Id == id && x.RestaurantId == restaurantId, ct);
            if (account is null) return Results.NotFound();

            var name = NormalizeRequired(request.Name, 120);
            if (name is null)
                return Results.BadRequest(new { message = "Название счёта обязательно." });
            if (!TryAccountType(request.Type, out var type))
                return Results.BadRequest(new { message = "Тип счёта должен быть CASH, BANK, CARD или OTHER." });

            if (await db.MoneyAccounts.AnyAsync(
                x => x.RestaurantId == restaurantId && x.Id != id && x.Name == name, ct))
                return Results.Conflict(new { message = "Счёт с таким названием уже существует." });

            account.Name = name;
            account.Type = type;
            account.IsActive = request.IsActive;
            AddAudit(db, user, restaurantId, "MONEY_ACCOUNT_UPDATED", "MoneyAccount", account.Id, new
            {
                account.Name,
                type = EnumText(account.Type),
                account.IsActive
            });
            await db.SaveChangesAsync(ct);
            return Results.Ok(new { id = account.Id });
        }).RequireAuthorization(Permissions.FinanceManage);

        group.MapPost("/categories", async (
            UpsertMoneyCategoryRequest request,
            ClaimsPrincipal user,
            RestaurantDbContext db,
            CancellationToken ct) =>
        {
            if (!TryClaims(user, out var restaurantId, out _))
                return Results.Unauthorized();

            var name = NormalizeRequired(request.Name, 120);
            if (name is null)
                return Results.BadRequest(new { message = "Название категории обязательно." });
            if (!TryDirection(request.Direction, out var direction))
                return Results.BadRequest(new { message = "Направление должно быть INCOME или EXPENSE." });

            if (await db.MoneyCategories.AnyAsync(
                x => x.RestaurantId == restaurantId && x.Direction == direction && x.Name == name, ct))
                return Results.Conflict(new { message = "Такая категория уже существует." });

            var category = new MoneyCategory
            {
                RestaurantId = restaurantId,
                Name = name,
                Direction = direction,
                IsActive = true
            };
            db.MoneyCategories.Add(category);
            AddAudit(db, user, restaurantId, "MONEY_CATEGORY_CREATED", "MoneyCategory", category.Id, new
            {
                category.Name,
                direction = EnumText(category.Direction)
            });
            await db.SaveChangesAsync(ct);
            return Results.Created($"/api/v1/backoffice/money/categories/{category.Id}", new { id = category.Id });
        }).RequireAuthorization(Permissions.FinanceManage);

        group.MapPut("/categories/{id:guid}", async (
            Guid id,
            UpdateMoneyCategoryRequest request,
            ClaimsPrincipal user,
            RestaurantDbContext db,
            CancellationToken ct) =>
        {
            if (!TryClaims(user, out var restaurantId, out _))
                return Results.Unauthorized();

            var category = await db.MoneyCategories.FirstOrDefaultAsync(
                x => x.Id == id && x.RestaurantId == restaurantId, ct);
            if (category is null) return Results.NotFound();

            var name = NormalizeRequired(request.Name, 120);
            if (name is null)
                return Results.BadRequest(new { message = "Название категории обязательно." });
            if (!TryDirection(request.Direction, out var direction))
                return Results.BadRequest(new { message = "Направление должно быть INCOME или EXPENSE." });

            if (await db.MoneyCategories.AnyAsync(
                x => x.RestaurantId == restaurantId &&
                     x.Id != id &&
                     x.Direction == direction &&
                     x.Name == name, ct))
                return Results.Conflict(new { message = "Такая категория уже существует." });

            category.Name = name;
            category.Direction = direction;
            category.IsActive = request.IsActive;
            AddAudit(db, user, restaurantId, "MONEY_CATEGORY_UPDATED", "MoneyCategory", category.Id, new
            {
                category.Name,
                direction = EnumText(category.Direction),
                category.IsActive
            });
            await db.SaveChangesAsync(ct);
            return Results.Ok(new { id = category.Id });
        }).RequireAuthorization(Permissions.FinanceManage);

        group.MapPost("/transactions", async (
            CreateMoneyTransactionRequest request,
            ClaimsPrincipal user,
            RestaurantDbContext db,
            CancellationToken ct) =>
        {
            if (!TryClaims(user, out var restaurantId, out var employeeId))
                return Results.Unauthorized();

            if (!TryDirection(request.Direction, out var direction))
                return Results.BadRequest(new { message = "Направление должно быть INCOME или EXPENSE." });
            if (request.Amount <= 0 || request.Amount > 1_000_000_000m)
                return Results.BadRequest(new { message = "Сумма должна быть больше нуля." });

            var account = await db.MoneyAccounts
                .AsNoTracking()
                .FirstOrDefaultAsync(x =>
                    x.Id == request.AccountId &&
                    x.RestaurantId == restaurantId &&
                    x.IsActive, ct);
            if (account is null)
                return Results.BadRequest(new { message = "Активный денежный счёт не найден." });

            var category = await db.MoneyCategories
                .AsNoTracking()
                .FirstOrDefaultAsync(x =>
                    x.Id == request.CategoryId &&
                    x.RestaurantId == restaurantId &&
                    x.IsActive, ct);
            if (category is null)
                return Results.BadRequest(new { message = "Активная финансовая категория не найдена." });
            if (category.Direction != direction)
                return Results.BadRequest(new { message = "Категория не соответствует типу операции." });

            var occurredAt = request.OccurredAt ?? DateTimeOffset.UtcNow;
            if (occurredAt > DateTimeOffset.UtcNow.AddDays(1))
                return Results.BadRequest(new { message = "Дата операции не может быть в далёком будущем." });

            var transaction = new MoneyTransaction
            {
                RestaurantId = restaurantId,
                AccountId = account.Id,
                CategoryId = category.Id,
                EmployeeId = employeeId,
                Direction = direction,
                Amount = Money(request.Amount),
                Note = NormalizeOptional(request.Note, 500),
                ReferenceType = "MANUAL",
                OccurredAt = occurredAt,
                CreatedAt = DateTimeOffset.UtcNow
            };

            db.MoneyTransactions.Add(transaction);
            AddAudit(db, user, restaurantId, "MONEY_TRANSACTION_CREATED", "MoneyTransaction", transaction.Id, new
            {
                transaction.AccountId,
                accountName = account.Name,
                transaction.CategoryId,
                categoryName = category.Name,
                direction = EnumText(transaction.Direction),
                transaction.Amount,
                transaction.Note,
                transaction.OccurredAt
            });
            await db.SaveChangesAsync(ct);

            return Results.Created($"/api/v1/backoffice/money/transactions/{transaction.Id}", new
            {
                id = transaction.Id,
                transaction.AccountId,
                transaction.CategoryId,
                direction = EnumText(transaction.Direction),
                transaction.Amount,
                transaction.Note,
                transaction.OccurredAt
            });
        }).RequireAuthorization(Permissions.FinanceManage);

        return app;
    }

    private static bool TryAccountType(string? value, out MoneyAccountType type) =>
        Enum.TryParse(value?.Trim(), true, out type) && Enum.IsDefined(type);

    private static bool TryDirection(string? value, out MoneyDirection direction) =>
        Enum.TryParse(value?.Trim(), true, out direction) && Enum.IsDefined(direction);

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

public sealed record UpsertMoneyAccountRequest(string Name, string Type);
public sealed record UpdateMoneyAccountRequest(string Name, string Type, bool IsActive);
public sealed record UpsertMoneyCategoryRequest(string Name, string Direction);
public sealed record UpdateMoneyCategoryRequest(string Name, string Direction, bool IsActive);
public sealed record CreateMoneyTransactionRequest(
    Guid AccountId,
    Guid CategoryId,
    string Direction,
    decimal Amount,
    string? Note,
    DateTimeOffset? OccurredAt);
