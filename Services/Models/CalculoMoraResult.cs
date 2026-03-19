namespace TheBuryProject.Services.Models
{
    /// <summary>
    /// Resultado del cálculo de mora para una o más cuotas.
    /// DTO inmutable, sin side-effects.
    /// </summary>
    public sealed class CalculoMoraResult
    {
        /// <summary>
        /// Fecha en que se realizó el cálculo (para idempotencia)
        /// </summary>
        public DateTime FechaCalculo { get; init; }

        /// <summary>
        /// Total de mora calculada (suma de todas las cuotas)
        /// </summary>
        public decimal TotalMora { get; init; }

        /// <summary>
        /// Total del capital vencido
        /// </summary>
        public decimal TotalCapitalVencido { get; init; }

        /// <summary>
        /// Total de deuda (capital + interés + mora)
        /// </summary>
        public decimal TotalDeuda { get; init; }

        /// <summary>
        /// Cantidad de cuotas procesadas
        /// </summary>
        public int CuotasProcesadas { get; init; }

        /// <summary>
        /// Cantidad de cuotas con mora > 0
        /// </summary>
        public int CuotasConMora { get; init; }

        /// <summary>
        /// Detalle por cada cuota procesada
        /// </summary>
        public IReadOnlyList<DetalleMoraCuota> Detalles { get; init; } = Array.Empty<DetalleMoraCuota>();

        /// <summary>
        /// Indica si el cálculo fue exitoso
        /// </summary>
        public bool Exitoso { get; init; } = true;

        /// <summary>
        /// Mensaje de error si el cálculo falló
        /// </summary>
        public string? Error { get; init; }

        /// <summary>
        /// Crea un resultado vacío (sin mora)
        /// </summary>
        public static CalculoMoraResult Vacio(DateTime fechaCalculo) => new()
        {
            FechaCalculo = fechaCalculo,
            TotalMora = 0,
            TotalCapitalVencido = 0,
            TotalDeuda = 0,
            CuotasProcesadas = 0,
            CuotasConMora = 0,
            Detalles = Array.Empty<DetalleMoraCuota>()
        };

        /// <summary>
        /// Crea un resultado con error
        /// </summary>
        public static CalculoMoraResult ConError(DateTime fechaCalculo, string error) => new()
        {
            FechaCalculo = fechaCalculo,
            Exitoso = false,
            Error = error
        };
    }

    /// <summary>
    /// Detalle del cálculo de mora para una cuota específica.
    /// DTO inmutable, sin side-effects.
    /// </summary>
    public sealed class DetalleMoraCuota
    {
        /// <summary>
        /// Id de la cuota
        /// </summary>
        public int CuotaId { get; init; }

        /// <summary>
        /// Número de cuota en el crédito
        /// </summary>
        public int NumeroCuota { get; init; }

        /// <summary>
        /// Fecha de vencimiento de la cuota
        /// </summary>
        public DateTime FechaVencimiento { get; init; }

        /// <summary>
        /// Días de atraso (sin días de gracia)
        /// </summary>
        public int DiasAtraso { get; init; }

        /// <summary>
        /// Días de atraso efectivos (descontando días de gracia)
        /// </summary>
        public int DiasAtrasoEfectivos { get; init; }

        /// <summary>
        /// Base sobre la cual se calculó la mora
        /// </summary>
        public decimal BaseCalculo { get; init; }

        /// <summary>
        /// Tasa aplicada (diaria o mensual prorrateada)
        /// </summary>
        public decimal TasaAplicada { get; init; }

        /// <summary>
        /// Mora calculada antes de aplicar topes
        /// </summary>
        public decimal MoraBruta { get; init; }

        /// <summary>
        /// Mora después de aplicar topes y mínimos
        /// </summary>
        public decimal MoraFinal { get; init; }

        /// <summary>
        /// Si se aplicó tope máximo
        /// </summary>
        public bool TopeAplicado { get; init; }

        /// <summary>
        /// Si se aplicó mora mínima
        /// </summary>
        public bool MinimoAplicado { get; init; }

        /// <summary>
        /// Capital de la cuota
        /// </summary>
        public decimal MontoCapital { get; init; }

        /// <summary>
        /// Interés de la cuota
        /// </summary>
        public decimal MontoInteres { get; init; }

        /// <summary>
        /// Monto ya pagado de la cuota
        /// </summary>
        public decimal MontoPagado { get; init; }

        /// <summary>
        /// Saldo pendiente de la cuota (sin mora)
        /// </summary>
        public decimal SaldoPendiente => MontoCapital + MontoInteres - MontoPagado;

        /// <summary>
        /// Total a pagar (saldo + mora)
        /// </summary>
        public decimal TotalAPagar => SaldoPendiente + MoraFinal;
    }
}
