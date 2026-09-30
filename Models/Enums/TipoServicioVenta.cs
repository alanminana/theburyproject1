namespace TheBuryProject.Models.Enums
{
    /// <summary>
    /// Servicios opcionales de una venta con precio fijo global (tabla ServiciosVentaPrecios):
    /// tipos de envío (a nivel venta) y tipos de armado (a nivel línea/producto).
    /// El valor numérico es estable: se persiste tal cual.
    /// </summary>
    public enum TipoServicioVenta
    {
        EnvioCiudad = 1,
        EnvioRural = 2,
        Armado1 = 11,
        Armado2 = 12,
        Armado3 = 13,
        Armado4 = 14,
        Armado5 = 15,
        Armado6 = 16
    }

    public static class TipoServicioVentaExtensions
    {
        public static readonly IReadOnlyList<TipoServicioVenta> Todos = new[]
        {
            TipoServicioVenta.EnvioCiudad, TipoServicioVenta.EnvioRural,
            TipoServicioVenta.Armado1, TipoServicioVenta.Armado2, TipoServicioVenta.Armado3,
            TipoServicioVenta.Armado4, TipoServicioVenta.Armado5, TipoServicioVenta.Armado6
        };

        public static bool EsEnvio(this TipoServicioVenta tipo) =>
            tipo is TipoServicioVenta.EnvioCiudad or TipoServicioVenta.EnvioRural;

        public static bool EsArmado(this TipoServicioVenta tipo) => !tipo.EsEnvio() && Enum.IsDefined(tipo);

        /// <summary>Los armados N.º 5 y N.º 6 se realizan en el domicilio del cliente.</summary>
        public static bool EsDomiciliario(this TipoServicioVenta tipo) =>
            tipo is TipoServicioVenta.Armado5 or TipoServicioVenta.Armado6;

        public static string NombreVisible(this TipoServicioVenta tipo) => tipo switch
        {
            TipoServicioVenta.EnvioCiudad => "Envío Ciudad",
            TipoServicioVenta.EnvioRural => "Envío Rural",
            TipoServicioVenta.Armado1 => "Armado N.º 1",
            TipoServicioVenta.Armado2 => "Armado N.º 2",
            TipoServicioVenta.Armado3 => "Armado N.º 3",
            TipoServicioVenta.Armado4 => "Armado N.º 4",
            TipoServicioVenta.Armado5 => "Armado N.º 5 — Domiciliario",
            TipoServicioVenta.Armado6 => "Armado N.º 6 — Domiciliario",
            _ => tipo.ToString()
        };
    }
}
