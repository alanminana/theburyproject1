using TheBuryProject.Services;

namespace TheBuryProject.Helpers
{
    /// <summary>
    /// P-02 / C15: las marcas de tiempo se guardan como instantes UTC y se MUESTRAN en la hora comercial argentina.
    /// <c>ToLocalTime()</c> dependía de la zona del servidor (el contenedor corre en UTC: una cotización hecha a las 00:43 se veía 03:43),
    /// por lo que no debe usarse para presentar. Estos métodos usan siempre <see cref="RelojComercial.Sistema"/> (Argentina).
    /// Solo para instantes UTC: las fechas comerciales sin hora (vencimientos, fechas de formulario) se muestran tal cual, sin convertir.
    /// </summary>
    public static class FechaComercialExtensions
    {
        /// <summary>Instante guardado en UTC → hora comercial argentina (Kind sin especificar).</summary>
        public static DateTime AHoraComercial(this DateTime utc) => FechaComercial.AComercial(utc);
    }
}
