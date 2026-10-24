using System.Globalization;

namespace TheBuryProject.Helpers;

public static class DecimalParsingHelper
{
    public static bool TryParseFlexibleDecimal(
        string? raw,
        out decimal value,
        NumberStyles numberStyles = NumberStyles.Number,
        bool allowMixedSeparators = false,
        bool emptyAsZero = true)
    {
        value = 0m;

        if (string.IsNullOrWhiteSpace(raw))
            return emptyAsZero;

        var normalized = NormalizeFlexibleDecimal(raw, allowMixedSeparators);
        return decimal.TryParse(normalized, numberStyles, CultureInfo.InvariantCulture, out value);
    }

    public static string NormalizeFlexibleDecimal(string raw, bool allowMixedSeparators = false)
    {
        var normalized = raw.Trim();
        var hasComma = normalized.Contains(',');
        var hasDot = normalized.Contains('.');

        if (allowMixedSeparators && hasComma && hasDot)
            return normalized.Replace(".", "").Replace(",", ".");

        if (hasComma)
            return normalized.Replace(",", ".");

        return normalized;
    }

    private static readonly System.Text.RegularExpressions.Regex MontoAmbiguoRegex = new(
        @"^-?[1-9]\d{0,2}[.,]\d{3}$",
        System.Text.RegularExpressions.RegexOptions.CultureInvariant);

    /// <summary>
    /// Un único separador (punto o coma) seguido de exactamente tres dígitos, con parte entera de 1 a 3 cifras que no
    /// empieza en cero ("1.500", "1,500", "12,345", "-1.500"). En un monto es ambiguo: decimal con tres decimales (que el
    /// dinero no admite) o separador de miles (1500). "0.500", "1.50", "1500", "1.500,00" y "1.500.000" no lo son.
    /// </summary>
    public static bool EsMontoAmbiguo(string? raw) =>
        !string.IsNullOrWhiteSpace(raw) && MontoAmbiguoRegex.IsMatch(raw.Trim());
}
