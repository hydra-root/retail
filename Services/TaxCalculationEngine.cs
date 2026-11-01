using RetailBillingApp.Models;

namespace RetailBillingApp.Services;

public class TaxCalculationResult{
    public decimal TaxableValue { get; set; }
    public decimal CGST { get; set; }
    public decimal SGST { get; set; }
    public decimal IGST { get; set; }
    public decimal Cess { get; set; }
    public decimal TotalTax { get; set; }
    public decimal TotalAmount { get; set; }
}

public class TaxCalculationEngine
{
    public static bool IsValidGSTIN(string? gstin)
    {
        if (string.IsNullOrWhiteSpace(gstin))
            return true; // GSTIN is optional for B2C

        gstin = gstin.Trim().ToUpperInvariant();

        // GSTIN format: 15 characters
        // First 2: State code
        // Next 10: PAN
        // 13th: Entity code
        // 14th: Z (default)
        // 15th: Checksum
        if (gstin.Length != 15)
            return false;

        // Basic pattern check: 2 digits + 10 alphanumeric + 1 alphanumeric + 1 char + 1 digit
        if (!char.IsDigit(gstin[0]) || !char.IsDigit(gstin[1]))
            return false;
        if (gstin[13] != 'Z')
            return false;
        if (!char.IsDigit(gstin[14]))
            return false;

        // INV-01: official GSTIN checksum (mod 36-97) over the first 14 characters.
        const string alphabet = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ";
        var sum = 0;
        for (var i = 0; i < 14; i++)
        {
            var value = alphabet.IndexOf(gstin[i]);
            if (value < 0)
                return false; // character outside the GSTIN alphabet

            var factor = (i % 2) + 1;                 // factors alternate 1, 2, 1, 2, ...
            var product = value * factor;
            sum += product / 36 + product % 36;       // (product / 36) is integer division
        }

        var checksumIndex = (36 - sum % 36) % 36;
        return gstin[14] == alphabet[checksumIndex];
    }

    public static string GetStateCode(string? placeOfSupply)
    {
        // Return state code from place of supply
        // This is a simplified mapping - in production, use a complete state code table
        var stateCodes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "Delhi", "07" },
            { "Maharashtra", "27" },
            { "Karnataka", "29" },
            { "Tamil Nadu", "33" },
            { "Gujarat", "24" },
            { "Haryana", "06" },
            { "Uttar Pradesh", "09" },
            { "West Bengal", "19" },
            { "Rajasthan", "08" },
            { "Madhya Pradesh", "23" }
        };

        if (string.IsNullOrWhiteSpace(placeOfSupply))
            return "07"; // Default

        return stateCodes.TryGetValue(placeOfSupply.Trim(), out var code) ? code : "07";
    }

    public static bool IsInterState(string? supplierStateCode, string? placeOfSupply)
    {
        if (string.IsNullOrWhiteSpace(supplierStateCode) || string.IsNullOrWhiteSpace(placeOfSupply))
            return false;

        var placeCode = GetStateCode(placeOfSupply);
        return supplierStateCode != placeCode;
    }

    public static TaxCalculationResult CalculateItemTax(
        decimal taxableValue,
        decimal taxPercentage,
        decimal cessRate,
        bool isInterState,
        bool taxInclusive = false)
    {
        var result = new TaxCalculationResult();

        if (taxInclusive)
        {
            // Reverse tax calculation: taxable value = total / (1 + taxRate/100)
            var taxRate = taxPercentage / 100m;
            result.TaxableValue = taxableValue / (1 + taxRate);
            var taxAmount = taxableValue - result.TaxableValue;
        }
        else
        {
            result.TaxableValue = taxableValue;
        }

        var cgstRate = isInterState ? 0m : taxPercentage / 2m;
        var sgstRate = isInterState ? 0m : taxPercentage / 2m;
        var igstRate = isInterState ? taxPercentage : 0m;

        result.CGST = Math.Round(result.TaxableValue * cgstRate / 100m, 2);
        result.SGST = Math.Round(result.TaxableValue * sgstRate / 100m, 2);
        result.IGST = Math.Round(result.TaxableValue * igstRate / 100m, 2);
        result.Cess = Math.Round(result.TaxableValue * cessRate / 100m, 2);
        result.TotalTax = result.CGST + result.SGST + result.IGST + result.Cess;
        result.TotalAmount = result.TaxableValue + result.TotalTax;

        return result;
    }

    public static TaxCalculationResult CalculateInvoiceTax(List<InvoiceItem> items, bool isInterState, decimal roundOff = 0)
    {
        var result = new TaxCalculationResult();

        foreach (var item in items)
        {
            var itemResult = CalculateItemTax(
                item.Amount,
                item.TaxPercentage,
                item.CessRate,
                isInterState,
                item.TaxInclusive);

            item.CGST = itemResult.CGST;
            item.SGST = itemResult.SGST;
            item.IGST = itemResult.IGST;
            item.Cess = itemResult.Cess;
            item.TaxAmount = itemResult.TotalTax;
            item.Total = itemResult.TotalAmount;

            result.TaxableValue += itemResult.TaxableValue;
            result.CGST += itemResult.CGST;
            result.SGST += itemResult.SGST;
            result.IGST += itemResult.IGST;
            result.Cess += itemResult.Cess;
            result.TotalTax += itemResult.TotalTax;
            result.TotalAmount += itemResult.TotalAmount;
        }

        result.TotalTax = Math.Round(result.TotalTax, 2);
        result.TotalAmount = Math.Round(result.TotalAmount + roundOff, 2);

        return result;
    }
}
