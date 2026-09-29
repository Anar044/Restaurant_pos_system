namespace RestaurantNode.Api.Infrastructure;

public static class TaxPolicy
{
    public const string Standard = "STANDARD";
    public const string VatExemptOwnAgriculture = "VAT_EXEMPT_OWN_AGRICULTURE";
    public const string VatAgricultureMargin = "VAT_AGRI_MARGIN";
    public const string VatZeroRate = "VAT_ZERO_RATE";
    public const string VatExemptOther = "VAT_EXEMPT_OTHER";

    public sealed record ProductTaxStatusOption(
        string Code,
        string Name,
        string Description);

    public static IReadOnlyList<ProductTaxStatusOption> ProductTaxStatuses { get; } =
    [
        new(
            Standard,
            "Обычная продажа",
            "Стандартный налоговый режим позиции определяется налоговым профилем ресторана."),
        new(
            VatExemptOwnAgriculture,
            "Собственная сельхозпродукция — ƏDV-dən azad",
            "Собственная сельхозпродукция, для которой применяется освобождение от ƏDV при выполнении установленных условий."),
        new(
            VatAgricultureMargin,
            "Перепродажа сельхозпродукции — ƏDV с торговой наценки",
            "Отдельный режим для соответствующей перепродажи сельхозпродукции."),
        new(
            VatZeroRate,
            "Нулевая ставка — 0%",
            "Используется только для операций, которые законодательство прямо относит к нулевой ставке."),
        new(
            VatExemptOther,
            "Другое освобождение от ƏDV",
            "Другой предусмотренный законодательством случай освобождения от ƏDV.")
    ];

    public static bool IsSupportedProductTaxStatus(string? value) =>
        ProductTaxStatuses.Any(x =>
            string.Equals(x.Code, value?.Trim(), StringComparison.OrdinalIgnoreCase));

    public static string NormalizeProductTaxStatus(string? value)
    {
        var normalized = value?.Trim().ToUpperInvariant();
        return IsSupportedProductTaxStatus(normalized)
            ? normalized!
            : Standard;
    }
}
