using System.ComponentModel.DataAnnotations;
using TheBuryProject.Models.Base;
using TheBuryProject.Models.Entities;

namespace TheBuryProject.Models.Entities
{
    /// <summary>
    /// Reclamo/devolucion/garantia de Mercado Libre asociado a una orden.
    /// No dispara stock ni caja al crearse: siempre requiere decision manual.
    /// </summary>
    public class MercadoLibreClaim : AuditableEntity
    {
        [StringLength(80)]
        public string? MercadoLibreClaimId { get; set; }

        public int MercadoLibreOrderId { get; set; }

        public MercadoLibreClaimTipo Tipo { get; set; } = MercadoLibreClaimTipo.Reclamo;

        public MercadoLibreClaimEstado Estado { get; set; } = MercadoLibreClaimEstado.PendienteRevision;

        [StringLength(500)]
        public string? Motivo { get; set; }

        [StringLength(500)]
        public string? ResolucionManual { get; set; }

        public MercadoLibreClaimAccionStock AccionStock { get; set; } =
            MercadoLibreClaimAccionStock.NoReingresar;

        public MercadoLibreClaimAccionEconomica AccionEconomica { get; set; } =
            MercadoLibreClaimAccionEconomica.SinImpacto;

        public int? MovimientoStockId { get; set; }

        public int? MovimientoCajaId { get; set; }

        [StringLength(1000)]
        public string? Observaciones { get; set; }

        public string? RawJson { get; set; }

        public bool EsSimuladoLocal { get; set; }

        public DateTime FechaCreacionUtc { get; set; } = DateTime.UtcNow;

        public DateTime? FechaResolucionUtc { get; set; }

        [StringLength(100)]
        public string? UsuarioResolucion { get; set; }

        public virtual MercadoLibreOrder Order { get; set; } = null!;
        public virtual MovimientoStock? MovimientoStock { get; set; }
        public virtual MovimientoCaja? MovimientoCaja { get; set; }
    }
}
