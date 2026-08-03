using System;

namespace TheBuryProject.Services
{
    /// <summary>
    /// Fuente temporal única para las reglas de negocio (Micro-lote 6 — consistencia temporal).
    /// Centraliza el instante actual UTC y la fecha comercial del negocio (Argentina), de modo
    /// que las comparaciones de vencimiento no mezclen UTC con fecha local del proceso.
    /// </summary>
    /// <remarks>
    /// Para comparar vencimientos por día se debe usar <see cref="HoyComercial"/>, nunca
    /// <c>DateTime.Today</c> (depende de la zona del proceso) ni <c>DateTime.UtcNow</c>
    /// (cruza de día a las 21:00 en −03:00, adelantando mora/punitorio un día).
    /// </remarks>
    public interface IRelojComercial
    {
        /// <summary>Instante actual en UTC (para sellos de auditoría / timestamps).</summary>
        DateTime AhoraUtc { get; }

        /// <summary>Instante actual expresado en la hora comercial (zona Argentina).</summary>
        DateTime AhoraComercial { get; }

        /// <summary>
        /// Fecha comercial actual del negocio (día en Argentina). Autoridad para decidir
        /// si una cuota vence hoy, en el futuro o ya venció.
        /// </summary>
        DateOnly HoyComercial { get; }

        /// <summary>Zona horaria comercial resuelta (Argentina), portable entre Windows y Linux.</summary>
        TimeZoneInfo ZonaComercial { get; }

        /// <summary>
        /// Inicio del día comercial (medianoche de <see cref="HoyComercial"/>) como
        /// <see cref="DateTime"/> con <see cref="DateTimeKind.Unspecified"/>. Sirve como cota
        /// para comparar en consultas EF contra <c>Cuota.FechaVencimiento</c> (almacenada a
        /// medianoche): <c>FechaVencimiento &lt; InicioDiaComercial</c> ⇔ venció antes de hoy.
        /// </summary>
        DateTime InicioDiaComercial { get; }
    }
}
