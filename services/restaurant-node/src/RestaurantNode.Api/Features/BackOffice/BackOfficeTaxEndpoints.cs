using System.Security.Claims;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using RestaurantNode.Api.Domain;
using RestaurantNode.Api.Infrastructure;
using RestaurantNode.Api.Security;

namespace RestaurantNode.Api.Features.BackOffice;

public static class BackOfficeTaxEndpoints
{
    public static IEndpointRouteBuilder MapBackOfficeTaxEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/backoffice/tax")
            .RequireAuthorization(Permissions.BackOfficeRead);

        group.MapGet("", async (
            ClaimsPrincipal user,
            RestaurantDbContext db,
            CancellationToken ct) =>
        {
            if (!TryGetRestaurantId(user, out var restaurantId))
                return Results.Unauthorized();

            var restaurant = await db.Restaurants
                .AsNoTracking()
                .Where(x => x.Id == restaurantId)
                .Select(x => new
                {
                    x.Id,
                    x.TaxRegime,
                    x.VatPriceMode,
                    x.IntegratedPosTaxReliefEnabled
                })
                .FirstOrDefaultAsync(ct);

            if (restaurant is null)
                return Results.NotFound();

            return Results.Ok(new
            {
                profile = new
                {
                    restaurantId = restaurant.Id,
                    taxRegime = restaurant.TaxRegime,
                    vatPriceMode = restaurant.VatPriceMode,
                    integratedPosTaxReliefEnabled = restaurant.IntegratedPosTaxReliefEnabled
                },
                taxRegimes = TaxPolicy.RestaurantTaxRegimes,
                vatPriceModes = new[]
                {
                    new
                    {
                        code = TaxPolicy.VatPriceIncluded,
                        name = "ƏDV уже включён в цену",
                        description = "Для продажи с включённым ƏDV налог выделяется из конечной суммы по формуле 18/118."
                    },
                    new
                    {
                        code = TaxPolicy.VatPriceExcluded,
                        name = "Цена указана без ƏDV",
                        description = "К цене добавляется ƏDV 18%, и итог к оплате увеличивается на сумму налога."
                    }
                },
                rates = new
                {
                    vat = TaxPolicy.VatRate,
                    simplified8 = TaxPolicy.Simplified8Rate,
                    simplified8IntegratedPos = TaxPolicy.Simplified8IntegratedPosRate,
                    simplified2 = TaxPolicy.Simplified2Rate
                },
                restaurantPosRelief = new
                {
                    from = TaxPolicy.RestaurantPosReliefFrom,
                    to = TaxPolicy.RestaurantPosReliefTo,
                    vatTaxableTurnoverFactor = 0.5m
                }
            });
        });

        group.MapPut("", async (
            UpdateRestaurantTaxProfileRequest request,
            ClaimsPrincipal user,
            RestaurantDbContext db,
            CancellationToken ct) =>
        {
            if (!TryGetRestaurantId(user, out var restaurantId))
                return Results.Unauthorized();

            var taxRegime = request.TaxRegime?.Trim().ToUpperInvariant();
            var vatPriceMode = request.VatPriceMode?.Trim().ToUpperInvariant();

            if (!TaxPolicy.IsSupportedTaxRegime(taxRegime))
                return Results.BadRequest(new { message = "Выберите корректный налоговый режим." });

            if (!TaxPolicy.IsSupportedVatPriceMode(vatPriceMode))
                return Results.BadRequest(new { message = "Выберите, включён ли ƏDV в цены продажи." });

            var restaurant = await db.Restaurants
                .FirstOrDefaultAsync(x => x.Id == restaurantId, ct);
            if (restaurant is null)
                return Results.NotFound();

            restaurant.TaxRegime = taxRegime!;
            restaurant.VatPriceMode = vatPriceMode!;
            restaurant.IntegratedPosTaxReliefEnabled = request.IntegratedPosTaxReliefEnabled;

            Guid? employeeId = Guid.TryParse(
                user.FindFirstValue("employee_id"),
                out var parsedEmployeeId)
                ? parsedEmployeeId
                : null;

            db.AuditEvents.Add(new AuditEvent
            {
                RestaurantId = restaurantId,
                EmployeeId = employeeId,
                EventType = "RESTAURANT_TAX_PROFILE_UPDATED",
                EntityType = "Restaurant",
                EntityId = restaurant.Id,
                PayloadJson = JsonSerializer.Serialize(new
                {
                    restaurant.TaxRegime,
                    restaurant.VatPriceMode,
                    restaurant.IntegratedPosTaxReliefEnabled
                })
            });

            await db.SaveChangesAsync(ct);

            return Results.Ok(new
            {
                restaurantId = restaurant.Id,
                taxRegime = restaurant.TaxRegime,
                vatPriceMode = restaurant.VatPriceMode,
                integratedPosTaxReliefEnabled = restaurant.IntegratedPosTaxReliefEnabled
            });
        }).RequireAuthorization(Permissions.RestaurantManage);

        return app;
    }

    private static bool TryGetRestaurantId(ClaimsPrincipal user, out Guid restaurantId) =>
        Guid.TryParse(user.FindFirstValue("restaurant_id"), out restaurantId);
}

public sealed record UpdateRestaurantTaxProfileRequest(
    string TaxRegime,
    string VatPriceMode,
    bool IntegratedPosTaxReliefEnabled);
