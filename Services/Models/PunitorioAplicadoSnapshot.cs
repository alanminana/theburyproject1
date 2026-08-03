namespace TheBuryProject.Services.Models
{
    /// <summary>
    /// Un tramo del snapshot persistido en <c>PunitorioAplicado.DesgloseSnapshotJson</c> (PUN-ML5).
    /// Espejo serializable de <see cref="SegmentoPunitorio"/> — no se reutiliza ese tipo directamente
    /// para no atar el contrato de persistencia a cambios futuros del calculador (PUN-ML4).
    /// </summary>
    public sealed class PunitorioAplicadoSnapshotSegmento
    {
        public DateOnly Desde { get; init; }
        public DateOnly Hasta { get; init; }
        public int Dias { get; init; }
        public decimal SaldoBase { get; init; }
        public int? ConfiguracionPunitorioId { get; init; }
        public decimal? Porcentaje { get; init; }
        public int? PeriodoDias { get; init; }
        public int? DiasGracia { get; init; }
        public decimal? ImporteExacto { get; init; }
        public string MotivoDeInicio { get; init; } = string.Empty;
        public string MotivoDeFin { get; init; } = string.Empty;
    }

    /// <summary>
    /// Versión de <see cref="Entities.ConfiguracionPunitorio"/> tal como estaba en el momento de
    /// aplicar, capturada por valor: si algún día se reinterpreta el historial (no debería, es
    /// append-only) el snapshot ya aplicado no cambia de significado.
    /// </summary>
    public sealed class PunitorioAplicadoSnapshotConfiguracion
    {
        public int Id { get; init; }
        public DateOnly VigenteDesde { get; init; }
        public decimal Porcentaje { get; init; }
        public int PeriodoDias { get; init; }
        public int DiasGracia { get; init; }
        public bool Activa { get; init; }
    }

    /// <summary>Snapshot completo serializado en <c>PunitorioAplicado.DesgloseSnapshotJson</c>.</summary>
    public sealed class PunitorioAplicadoSnapshot
    {
        public DateOnly FechaVencimiento { get; init; }
        public DateOnly FechaCalculo { get; init; }
        public decimal SaldoInicial { get; init; }
        public decimal SaldoFinal { get; init; }
        public int DiasTranscurridos { get; init; }
        public decimal PunitorioExacto { get; init; }
        public decimal PunitorioRedondeado { get; init; }
        public IReadOnlyList<PunitorioAplicadoSnapshotSegmento> Segmentos { get; init; } = Array.Empty<PunitorioAplicadoSnapshotSegmento>();
        public IReadOnlyList<PunitorioAplicadoSnapshotConfiguracion> ConfiguracionesUtilizadas { get; init; } = Array.Empty<PunitorioAplicadoSnapshotConfiguracion>();

        /// <summary>
        /// Total teórico acumulado que devolvió el calculador para la ventana completa
        /// [FechaVencimiento, FechaCalculo) — equivalente a <see cref="PunitorioRedondeado"/>, repetido
        /// acá explícitamente para que el snapshot de una aplicación sucesiva (PUN-ML6) no dependa de
        /// inferir el teórico a partir de <c>ImporteNuevoAplicado + ImportePreviamenteAplicado</c>.
        /// </summary>
        public decimal PunitorioTeoricoAcumulado { get; init; }

        /// <summary>
        /// Suma de <see cref="Entities.PunitorioAplicado.Importe"/> de aplicaciones previas no
        /// anuladas de la misma cuota (activas o pagadas) al momento de aplicar. Cero en la primera
        /// aplicación de una cuota.
        /// </summary>
        public decimal ImportePreviamenteAplicado { get; init; }

        /// <summary>
        /// <see cref="PunitorioTeoricoAcumulado"/> − <see cref="ImportePreviamenteAplicado"/>: el
        /// diferencial efectivamente persistido en <see cref="Entities.PunitorioAplicado.Importe"/> de
        /// esta fila. Evita doble devengamiento de los días ya cubiertos por aplicaciones anteriores.
        /// </summary>
        public decimal ImporteNuevoAplicado { get; init; }
    }
}
