namespace TheBuryProject.Services.Models
{
    /// <summary>
    /// Estado del intento de cobrar la primera cuota al generar el crédito
    /// (spec 2.4: pagar la 1ª cuota el mismo día si vence hoy).
    /// </summary>
    public enum EstadoCobroPrimeraCuota
    {
        /// <summary>No corresponde cobrar (no vence hoy, no está pendiente o sin saldo).</summary>
        NoAplica,

        /// <summary>La primera cuota se cobró correctamente.</summary>
        Cobrada,

        /// <summary>Correspondía cobrar pero el cobro falló.</summary>
        Error
    }

    /// <summary>
    /// Resultado de <c>CobrarPrimeraCuotaAlGenerarAsync</c>. El cobro reutiliza el flujo
    /// de <c>PagarCuotaAsync</c> (aplica el recargo del medio de pago como concepto
    /// separado e impacta en caja); este resultado solo resume qué ocurrió para el mensaje
    /// de la confirmación. Un fallo de cobro no revierte la generación del crédito.
    /// </summary>
    public class CobroPrimeraCuotaResultado
    {
        public EstadoCobroPrimeraCuota Estado { get; set; } = EstadoCobroPrimeraCuota.NoAplica;

        public int? CuotaId { get; set; }

        public int NumeroCuota { get; set; }

        /// <summary>Importe base de la cuota cobrado (sin recargo del medio de pago).</summary>
        public decimal MontoBase { get; set; }

        /// <summary>Recargo del medio de pago aplicado sobre el importe base.</summary>
        public decimal RecargoMedioPago { get; set; }

        /// <summary>Total efectivamente cobrado = base + recargo.</summary>
        public decimal Total => MontoBase + RecargoMedioPago;

        public string? MedioPago { get; set; }

        /// <summary>Detalle para el usuario cuando no se cobró o hubo error.</summary>
        public string? Mensaje { get; set; }

        public static CobroPrimeraCuotaResultado NoAplica(string? mensaje = null) =>
            new() { Estado = EstadoCobroPrimeraCuota.NoAplica, Mensaje = mensaje };
    }
}
