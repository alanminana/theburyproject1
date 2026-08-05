using TheBuryProject.Models.Enums;

namespace TheBuryProject.Services.Models
{
    /// <summary>
    /// Estado legible del cálculo informativo dentro del detalle de una cuota. Proyecta el estado
    /// del calculador y separa explícitamente una configuración activa al 0% de un cálculo positivo.
    /// </summary>
    public enum EstadoCalculoPunitorioDetalle
    {
        Calculado = 0,
        TasaCero = 1,
        SinConfiguracion = 2,
        ConfiguracionInactiva = 3,
        DentroDeGracia = 4,
        HistorialIncompleto = 5,
        SinSaldo = 6,
        EntradaInvalida = 7
    }

    /// <summary>
    /// Contrato read-only autoritativo de consulta e historial de punitorios de una cuota
    /// (PUN-ML9-B1). No expone entidades EF ni tokens de concurrencia mutables.
    /// </summary>
    public sealed class PunitorioCuotaDetalleResultado
    {
        public required int CuotaId { get; init; }
        public required int CreditoId { get; init; }
        public required int NumeroCuota { get; init; }
        public required DateOnly FechaVencimiento { get; init; }
        public required DateOnly FechaCalculoComercial { get; init; }
        public required EstadoCuota EstadoCuota { get; init; }
        public required decimal MontoTotalCuota { get; init; }
        public required decimal MontoPagadoCapital { get; init; }
        public required decimal CapitalPendiente { get; init; }

        /// <summary>
        /// <c>true</c> únicamente cuando todas las filas de <c>PagoCuota</c> de la cuota tienen una
        /// composición capital/punitorio reconstruible. El estado del cálculo informa por separado
        /// si una ambigüedad afecta efectivamente el total calculado al día.
        /// </summary>
        public required bool HistorialCompleto { get; init; }

        public string? MotivoHistorialIncompleto { get; init; }
        public required string CuotaRowVersionBase64 { get; init; }

        /// <summary>Token de la aplicación activa; <c>null</c> cuando no existe una.</summary>
        public string? PunitorioAplicadoRowVersionBase64 { get; init; }

        public required PunitorioCalculoActualDetalle CalculoActual { get; init; }
        public PunitorioAplicacionDetalle? AplicacionActiva { get; init; }
        public IReadOnlyList<PunitorioAplicacionDetalle> AplicacionesHistoricas { get; init; } =
            Array.Empty<PunitorioAplicacionDetalle>();
        public IReadOnlyList<PagoCuotaDetalle> Pagos { get; init; } = Array.Empty<PagoCuotaDetalle>();
    }

    /// <summary>Cálculo informativo al día; no representa deuda aplicada.</summary>
    public sealed class PunitorioCalculoActualDetalle
    {
        public required EstadoCalculoPunitorioDetalle Estado { get; init; }
        public required decimal SaldoCapitalSegunLedger { get; init; }
        public decimal? ImporteCalculado { get; init; }
        public string? MotivoNoCalculo { get; init; }
        /// <summary>
        /// Cero si no hay aplicación activa, el pendiente derivado si el ledger es completo, o
        /// <c>null</c> si existe una aplicación activa cuya composición pagada no es reconstruible.
        /// </summary>
        public decimal? PunitorioAplicadoPendienteReal { get; init; }
        public IReadOnlyList<SegmentoPunitorio> Segmentos { get; init; } = Array.Empty<SegmentoPunitorio>();
        public IReadOnlyList<PunitorioConfiguracionUsadaDetalle> ConfiguracionesUtilizadas { get; init; } =
            Array.Empty<PunitorioConfiguracionUsadaDetalle>();
    }

    /// <summary>Proyección tipada de una configuración usada por el cálculo, sin exponer EF.</summary>
    public sealed class PunitorioConfiguracionUsadaDetalle
    {
        public required int Id { get; init; }
        public required DateOnly VigenteDesde { get; init; }
        public required decimal Porcentaje { get; init; }
        public required int PeriodoDias { get; init; }
        public required int DiasGracia { get; init; }
        public required bool Activa { get; init; }
    }

    /// <summary>
    /// Proyección de una aplicación y su progreso derivado del ledger efectivo vinculado. Los
    /// campos del snapshot son nulos si el snapshot histórico no puede leerse con el contrato
    /// tipado existente; nunca se reconstruyen por inferencia.
    /// </summary>
    public sealed class PunitorioAplicacionDetalle
    {
        public required int PunitorioAplicadoId { get; init; }
        public required EstadoPunitorioAplicado Estado { get; init; }
        public decimal? ImporteTeorico { get; init; }
        public decimal? ImportePreviamenteAplicado { get; init; }
        public required decimal ImporteNuevoAplicado { get; init; }
        public required decimal ImporteAplicado { get; init; }
        public decimal? ImportePagado { get; init; }
        public decimal? ImportePendiente { get; init; }
        public required DateOnly FechaCalculo { get; init; }
        public required DateTime FechaAplicacion { get; init; }
        public required string MotivoAplicacion { get; init; }
        public required string UsuarioAplicacion { get; init; }
        public DateTime? FechaAnulacion { get; init; }
        public string? MotivoAnulacion { get; init; }
        public string? UsuarioAnulacion { get; init; }
        public required bool EsActiva { get; init; }
        public required string RowVersionBase64 { get; init; }
    }

    /// <summary>Proyección de una fila canónica de <c>PagoCuota</c>.</summary>
    public sealed class PagoCuotaDetalle
    {
        public required int PagoCuotaId { get; init; }
        public required DateOnly FechaPagoComercial { get; init; }
        public required decimal ImporteTotal { get; init; }
        public decimal? ImporteAplicadoPunitorio { get; init; }
        public decimal? ImporteAplicadoCuota { get; init; }
        public string? MedioPago { get; init; }
        public required EstadoPagoCuota Estado { get; init; }
        public required OrigenPagoCuota Origen { get; init; }
        public int? MovimientoCajaId { get; init; }
        public int? PunitorioAplicadoId { get; init; }
        public required bool EsReversion { get; init; }
        public int? ReversionDePagoCuotaId { get; init; }
        public required bool HistorialCompleto { get; init; }
        public string? MotivoHistorialIncompleto { get; init; }
    }
}
