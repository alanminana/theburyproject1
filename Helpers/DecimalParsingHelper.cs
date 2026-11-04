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

    /// <summary>
    /// Lleva un número escrito a formato invariante. Contrato común con wwwroot/js/input-rules.js y
    /// jquery-validate-number-es-ar.js: con ambos separadores el ÚLTIMO es el decimal ("1.234,56" y "1,234.56" valen
    /// 1234,56); un mismo separador repetido en grupos de tres dígitos es de miles ("1.234.567", "1,234,567");
    /// un único separador es decimal (la ambigüedad de "1.500" en dinero la resuelve <see cref="EsMontoAmbiguo"/>).
    /// </summary>
    public static string NormalizeFlexibleDecimal(string raw, bool allowMixedSeparators = false)
    {
        var normalized = raw.Trim();
        var comas = normalized.Count(c => c == ',');
        var puntos = normalized.Count(c => c == '.');

        if (allowMixedSeparators && comas > 0 && puntos > 0)
        {
            var decimalEsComa = normalized.LastIndexOf(',') > normalized.LastIndexOf('.');
            var miles = decimalEsComa ? "." : ",";
            var decimalSep = decimalEsComa ? "," : ".";
            return normalized.Replace(miles, "").Replace(decimalSep, ".");
        }

        // Un solo tipo de separador repetido con grupos de tres dígitos: miles ("1.234.567" / "1,234,567").
        if ((comas > 1 && puntos == 0) || (puntos > 1 && comas == 0))
        {
            var sep = comas > 1 ? ',' : '.';
            if (MilesRepetidosRegex.IsMatch(normalized))
                return normalized.Replace(sep.ToString(), "");
        }

        if (comas > 0)
            return normalized.Replace(",", ".");

        return normalized;
    }

    private static readonly System.Text.RegularExpressions.Regex MilesRepetidosRegex = new(
        @"^-?\d{1,3}(?:([.,])\d{3})(?:\1\d{3})+$",
        System.Text.RegularExpressions.RegexOptions.CultureInvariant);

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
