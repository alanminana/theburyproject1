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
}
