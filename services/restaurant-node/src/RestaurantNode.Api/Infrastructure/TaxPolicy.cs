namespace RestaurantNode.Api.Infrastructure;

public static class TaxPolicy
{
    public const string UnconfiguredRegime = "UNCONFIGURED";
    public const string Vat18Regime = "VAT_18";
    public const string Simplified8Regime = "SIMPLIFIED_8";
    public const string Simplified2Regime = "SIMPLIFIED_2";

    public const string VatPriceIncluded = "INCLUDED";
    public const string VatPriceExcluded = "EXCLUDED";

    public const string PurchaseVat18 = "VAT_18";
    public const string PurchaseVatZero = "VAT_0";
    public const string PurchaseVatExempt = "VAT_EXEMPT";
    public const string PurchaseNoVat = "NO_VAT";

    public const string PurchaseDocumentSupplierInvoice = "SUPPLIER_INVOICE";
    public const string PurchaseDocumentRetailReceipt = "RETAIL_RECEIPT";
    public const string PurchaseDocumentOther = "OTHER";

    public const string RetailVatNotSpecified = "NOT_SPECIFIED";
    public const string RetailVat18 = "VAT_18";

    public const string InputVatNotApplicable = "NOT_APPLICABLE";
    public const string InputVatPending = "PENDING";
    public const string InputVatEligible = "ELIGIBLE";
    public const string InputVatCredited = "CREDITED";
    public const string InputVatNonCreditable = "NON_CREDITABLE";

    public const string Standard = "STANDARD";
    public const string VatExemptOwnAgriculture = "VAT_EXEMPT_OWN_AGRICULTURE";
    public const string VatAgricultureMargin = "VAT_AGRI_MARGIN";
    public const string VatZeroRate = "VAT_ZERO_RATE";
    public const string VatExemptOther = "VAT_EXEMPT_OTHER";

    public const decimal VatRate = 18m;
    public const decimal Simplified8Rate = 8m;
    public const decimal Simplified8IntegratedPosRate = 6m;
    public const decimal Simplified2Rate = 2m;

    public static readonly DateOnly RestaurantPosReliefFrom = new(2026, 1, 1);
    public static readonly DateOnly RestaurantPosReliefTo = new(2028, 12, 31);

    public sealed record TaxRegimeOption(
        string Code,
        string Name,
        string Description);

    public sealed record ProductTaxStatusOption(
        string Code,
        string Name,
        string Description);

    public sealed record VatCalculation(
        decimal NetAmount,
        decimal VatAmount,
        decimal GrossAmount,
        decimal TaxableTurnover);

    public sealed record SimplifiedTaxCalculation(
        decimal Turnover,
        decimal TaxRate,
        decimal TaxAmount);

    public sealed record PurchaseVatCalculation(
        decimal NetAmount,
        decimal VatAmount,
        decimal GrossAmount,
        decimal InventoryCostAmount,
        decimal RecoverableVatAmount);

    public static IReadOnlyList<TaxRegimeOption> RestaurantTaxRegimes { get; } =
    [
        new(
            UnconfiguredRegime,
            "Не настроено",
            "Налоговый профиль ещё не выбран. Автоматические налоговые проводки не создаются."),
        new(
            Vat18Regime,
            "ƏDV — 18%",
            "Ресторан зарегистрирован как плательщик ƏDV. Продажи и входной ƏDV учитываются раздельно."),
        new(
            Simplified8Regime,
            "Sadələşdirilmiş vergi — 8%",
            "Режим общественного питания с расчётом упрощённого налога с оборота."),
        new(
            Simplified2Regime,
            "Sadələşdirilmiş vergi — 2%",
            "Упрощённый налог рассчитывается с облагаемого оборота по ставке 2%.")
    ];

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

    public static bool IsSupportedTaxRegime(string? value) =>
        RestaurantTaxRegimes.Any(x =>
            string.Equals(x.Code, value?.Trim(), StringComparison.OrdinalIgnoreCase));

    public static bool IsSupportedVatPriceMode(string? value) =>
        value?.Trim().ToUpperInvariant() is VatPriceIncluded or VatPriceExcluded;


    public static bool IsSupportedPurchaseVatCode(string? value) =>
        value?.Trim().ToUpperInvariant() is
            PurchaseVat18 or PurchaseVatZero or PurchaseVatExempt or PurchaseNoVat;


    public static bool IsSupportedPurchaseDocumentKind(string? value) =>
        value?.Trim().ToUpperInvariant() is
            PurchaseDocumentSupplierInvoice or PurchaseDocumentRetailReceipt or PurchaseDocumentOther;

    public static bool IsSupportedRetailVatMode(string? value) =>
        value?.Trim().ToUpperInvariant() is RetailVatNotSpecified or RetailVat18;

    public static bool IsSupportedInputVatCreditStatus(string? value) =>
        value?.Trim().ToUpperInvariant() is
            InputVatNotApplicable or InputVatPending or InputVatEligible or InputVatCredited or InputVatNonCreditable;

    public static bool IsInputVatRecognizedAsRecoverable(string taxRegime, string inputVatCreditStatus) =>
        string.Equals(taxRegime, Vat18Regime, StringComparison.OrdinalIgnoreCase) &&
        inputVatCreditStatus.Trim().ToUpperInvariant() is
            InputVatPending or InputVatEligible or InputVatCredited;

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

    public static bool IsRestaurantPosReliefPeriod(DateOnly operationDate) =>
        operationDate >= RestaurantPosReliefFrom &&
        operationDate <= RestaurantPosReliefTo;

    public static VatCalculation CalculateVat(
        decimal amount,
        string vatPriceMode,
        bool eligibleIntegratedPosRelief = false,
        DateOnly? operationDate = null)
    {
        if (amount < 0m)
            throw new ArgumentOutOfRangeException(nameof(amount));

        var mode = vatPriceMode.Trim().ToUpperInvariant();
        if (!IsSupportedVatPriceMode(mode))
            throw new ArgumentException("Unsupported VAT price mode.", nameof(vatPriceMode));

        var date = operationDate ?? DateOnly.FromDateTime(DateTime.UtcNow);
        var reliefApplies =
            eligibleIntegratedPosRelief &&
            IsRestaurantPosReliefPeriod(date);

        if (mode == VatPriceIncluded)
        {
            var gross = Money(amount);
            var fullNet = Money(gross * 100m / 118m);
            var fullVat = Money(gross - fullNet);

            if (!reliefApplies)
                return new VatCalculation(fullNet, fullVat, gross, fullNet);

            var taxableGross = Money(gross * 0.5m);
            var taxableNet = Money(taxableGross * 100m / 118m);
            var vat = Money(taxableGross - taxableNet);
            var nonTaxablePart = Money(gross - taxableGross);
            var net = Money(nonTaxablePart + taxableNet);
            return new VatCalculation(net, vat, gross, taxableNet);
        }

        var baseAmount = Money(amount);
        var taxableBase = reliefApplies ? Money(baseAmount * 0.5m) : baseAmount;
        var vatAmount = Money(taxableBase * VatRate / 100m);
        var grossAmount = Money(baseAmount + vatAmount);
        return new VatCalculation(baseAmount, vatAmount, grossAmount, taxableBase);
    }

    public static PurchaseVatCalculation CalculatePurchaseVat(
        decimal enteredAmount,
        string vatPriceMode,
        string vatCode,
        string taxRegime,
        string inputVatCreditStatus)
    {
        if (enteredAmount < 0m)
            throw new ArgumentOutOfRangeException(nameof(enteredAmount));

        var mode = vatPriceMode.Trim().ToUpperInvariant();
        var code = vatCode.Trim().ToUpperInvariant();
        var regime = taxRegime.Trim().ToUpperInvariant();
        var creditStatus = inputVatCreditStatus.Trim().ToUpperInvariant();

        if (!IsSupportedVatPriceMode(mode))
            throw new ArgumentException("Unsupported VAT price mode.", nameof(vatPriceMode));
        if (!IsSupportedPurchaseVatCode(code))
            throw new ArgumentException("Unsupported purchase VAT code.", nameof(vatCode));
        if (!IsSupportedInputVatCreditStatus(creditStatus))
            throw new ArgumentException("Unsupported input VAT credit status.", nameof(inputVatCreditStatus));

        decimal net;
        decimal vat;
        decimal gross;

        if (code == PurchaseVat18)
        {
            if (mode == VatPriceIncluded)
            {
                gross = Money(enteredAmount);
                net = Money(gross * 100m / 118m);
                vat = Money(gross - net);
            }
            else
            {
                net = Money(enteredAmount);
                vat = Money(net * VatRate / 100m);
                gross = Money(net + vat);
            }
        }
        else
        {
            net = Money(enteredAmount);
            vat = 0m;
            gross = net;
        }

        var recoverableVat = code == PurchaseVat18 &&
                             IsInputVatRecognizedAsRecoverable(regime, creditStatus)
            ? vat
            : 0m;

        var inventoryCost = Money(gross - recoverableVat);

        return new PurchaseVatCalculation(
            net,
            vat,
            gross,
            inventoryCost,
            recoverableVat);
    }

    public static SimplifiedTaxCalculation CalculateSimplifiedTax(
        decimal turnover,
        string taxRegime,
        bool eligibleIntegratedPosRelief = false,
        DateOnly? operationDate = null)
    {
        if (turnover < 0m)
            throw new ArgumentOutOfRangeException(nameof(turnover));

        var regime = taxRegime.Trim().ToUpperInvariant();
        var date = operationDate ?? DateOnly.FromDateTime(DateTime.UtcNow);

        decimal rate = regime switch
        {
            Simplified2Regime => Simplified2Rate,
            Simplified8Regime when eligibleIntegratedPosRelief && IsRestaurantPosReliefPeriod(date)
                => Simplified8IntegratedPosRate,
            Simplified8Regime => Simplified8Rate,
            _ => throw new ArgumentException("Unsupported simplified tax regime.", nameof(taxRegime))
        };

        var normalizedTurnover = Money(turnover);
        return new SimplifiedTaxCalculation(
            normalizedTurnover,
            rate,
            Money(normalizedTurnover * rate / 100m));
    }

    private static decimal Money(decimal value) =>
        Math.Round(value, 4, MidpointRounding.AwayFromZero);
}
