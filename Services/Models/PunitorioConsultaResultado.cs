using TheBuryProject.Models.Entities;
using TheBuryProject.Models.Enums;

namespace TheBuryProject.Services.Models
{
    /// <summary>
    /// Resultado de <c>IPunitorioService.CalcularCuotaAsync</c> (PUN-ML5): punitorio calculado al día
    /// para una cuota real, sin persistir nada. Envuelve <see cref="PunitorioCalculoResultado"/> (PUN-ML4)
    /// con el contexto que solo el caller con acceso a la base puede aportar — cuota, crédito,
    /// pendiente ya aplicado — para que controller/Razor/JS nunca necesiten reconstruir la fórmula.
    /// </summary>
    public sealed class PunitorioConsultaResultado
    {
        public required int CuotaId { get; init; }
        public required int CreditoId { get; init; }
        public required DateOnly FechaCalculo { get; init; }
        public required EstadoResultadoPunitorio EstadoCalculo { get; init; }

        /// <summary>Saldo impago de la cuota a <see cref="FechaCalculo"/> (<c>PunitorioCalculoResultado.SaldoFinal</c>).</summary>
        public required decimal SaldoImpago { get; init; }

        /// <summary><c>PunitorioCalculoResultado.PunitorioRedondeado</c>. <c>null</c> cuando <see cref="EstadoCalculo"/> no tiene total autoritativo.</summary>
        public decimal? PunitorioCalculado { get; init; }

        public IReadOnlyList<SegmentoPunitorio> Segmentos { get; init; } = Array.Empty<SegmentoPunitorio>();

        /// <summary>Versiones de <see cref="ConfiguracionPunitorio"/> referenciadas por al menos un segmento, más antigua a más reciente.</summary>
        public IReadOnlyList<ConfiguracionPunitorio> ConfiguracionesUtilizadas { get; init; } = Array.Empty<ConfiguracionPunitorio>();

        /// <summary><c>false</c> únicamente cuando <see cref="EstadoCalculo"/> es <see cref="EstadoResultadoPunitorio.HistorialIncompleto"/>.</summary>
        public required bool HistorialCompleto { get; init; }

        /// <summary><c>PunitorioCalculoResultado.Motivo</c>. <c>null</c> cuando <see cref="EstadoCalculo"/> es <see cref="EstadoResultadoPunitorio.Calculado"/>.</summary>
        public string? MotivoNoCalculo { get; init; }

        /// <summary>
        /// <see cref="PunitorioAplicado.Importe"/> de la aplicación activa de esta cuota (Modelo A: a
        /// lo sumo una), neto de los pagos efectivos ya atribuidos a ella (PUN-ML6). Cero si no hay
        /// aplicación activa.
        /// </summary>
        public required decimal PunitorioAplicadoPendiente { get; init; }

        /// <summary><see cref="SaldoImpago"/> + <see cref="PunitorioCalculado"/>. <c>null</c> cuando <see cref="PunitorioCalculado"/> es <c>null</c>.</summary>
        public decimal? TotalPendienteEstimado { get; init; }
    }
}
