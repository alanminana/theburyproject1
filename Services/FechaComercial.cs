namespace TheBuryProject.Services
{
    /// <summary>
    /// P-02 / C15 (contador, 2026-11-19): los informes usan el día comercial argentino. <c>Venta.FechaVenta</c> se guarda SIEMPRE como
    /// instante UTC (nunca como «medianoche local» ni como fecha suelta) y todo lo que la lee para períodos, agrupaciones o pantallas la
    /// convierte con este ayudante. Una venta del 31/10 a las 22:30 de Argentina (01:30 UTC del 01/11) pertenece a octubre.
    /// Usa la zona de <see cref="RelojComercial.Sistema"/> (Argentina, portable entre Windows y Linux).
    /// </summary>
    public static class FechaComercial
    {
        private static TimeZoneInfo Zona => RelojComercial.Sistema.ZonaComercial;

        /// <summary>Instante UTC (como se guarda) → hora comercial argentina, con <see cref="DateTimeKind.Unspecified"/>.</summary>
        public static DateTime AComercial(DateTime utc) =>
            DateTime.SpecifyKind(
                TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), Zona),
                DateTimeKind.Unspecified);

        /// <summary>Hora comercial argentina → instante UTC (Kind Utc).</summary>
        public static DateTime AUtc(DateTime comercial) =>
            TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(comercial, DateTimeKind.Unspecified), Zona);

        /// <summary>
        /// Límite UTC del inicio del día comercial <paramref name="diaComercial"/> (00:00 de Argentina). Sirve de cota inferior inclusiva
        /// y, con el día siguiente, de cota superior exclusiva al filtrar <c>FechaVenta</c> por período.
        /// </summary>
        public static DateTime InicioDiaUtc(DateTime diaComercial) => AUtc(diaComercial.Date);

        /// <summary>Día comercial (medianoche, Kind Unspecified) al que pertenece un instante UTC guardado.</summary>
        public static DateTime DiaComercial(DateTime utc) => AComercial(utc).Date;

        /// <summary>
        /// Instante UTC con el que se guarda la fecha de una venta. Un valor con Kind UTC (o Local) ya es un instante y se conserva; una
        /// fecha de formulario (medianoche sin hora) es el DÍA COMERCIAL elegido y se combina con la hora comercial actual (el mismo día
        /// que hoy ⇒ el instante actual); una fecha con hora sin Kind se interpreta como hora comercial.
        /// </summary>
        public static DateTime InstanteUtcDeVenta(DateTime entrante, DateTime ahoraUtc)
        {
            switch (entrante.Kind)
            {
                case DateTimeKind.Utc:
                    return entrante;
                case DateTimeKind.Local:
                    return entrante.ToUniversalTime();
            }

            if (entrante.TimeOfDay != TimeSpan.Zero)
                return AUtc(entrante);

            var ahoraComercial = AComercial(ahoraUtc);
            return entrante.Date == ahoraComercial.Date
                ? DateTime.SpecifyKind(ahoraUtc, DateTimeKind.Utc)
                : AUtc(entrante.Date + ahoraComercial.TimeOfDay);
        }
    }
}
