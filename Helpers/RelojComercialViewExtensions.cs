using TheBuryProject.Services;

namespace TheBuryProject.Helpers
{
    /// <summary>
    /// Fechas «de hoy» para las vistas Razor según el día comercial (Argentina), no según el reloj del servidor.
    /// El contenedor corre en UTC: entre las 21:00 y las 24:00 de Argentina <c>DateTime.Today</c> ya es mañana.
    /// Las vistas reciben el reloj por <c>@inject IRelojComercial Reloj</c> (ver Views/_ViewImports.cshtml).
    /// </summary>
    public static class RelojComercialViewExtensions
    {
        /// <summary>Medianoche del día comercial actual (equivalente de <c>DateTime.Today</c>).</summary>
        public static DateTime HoyComercial(this IRelojComercial reloj) => reloj.InicioDiaComercial;

        /// <summary>Día comercial actual (más <paramref name="dias"/>) como <c>yyyy-MM-dd</c>, para <c>min</c>/<c>value</c> de inputs date.</summary>
        public static string HoyIso(this IRelojComercial reloj, int dias = 0) =>
            reloj.InicioDiaComercial.AddDays(dias).ToString("yyyy-MM-dd");

        /// <summary>Instante comercial actual como <c>yyyy-MM-ddTHH:mm</c>, para <c>min</c> de inputs datetime-local.</summary>
        public static string AhoraIsoLocal(this IRelojComercial reloj) =>
            reloj.AhoraComercial.ToString("yyyy-MM-ddTHH:mm");

        /// <summary>Convierte un instante guardado en UTC (Kind sin especificar) a la hora comercial para mostrarlo.</summary>
        public static DateTime UtcAComercial(this IRelojComercial reloj, DateTime utc) =>
            TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), reloj.ZonaComercial);
    }
}
