using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using TheBuryProject.Models.Base;
using TheBuryProject.Models.Enums;

namespace TheBuryProject.Models.Entities
{
    public class VentaDetalle  : AuditableEntity
    {
        [Required]
        public int VentaId { get; set; }

        [Required]
        public int ProductoId { get; set; }

        // Snapshot histórico de la identidad del producto al momento de la operación (Micro-lote 5).
        // Se capturan server-side desde el Producto de BD; nunca desde el payload del cliente.
        // Nullable sólo por compatibilidad con filas anteriores a la migración (backfill/fallback).
        // Renombrar o eliminar el producto no debe alterar la venta histórica: las pantallas leen
        // estos campos (con fallback a la relación viva sólo para filas legacy) vía VentaDetalleProductoSnapshot.
        [StringLength(200)]
        public string? ProductoNombreAlMomento { get; set; }

        [StringLength(50)]
        public string? ProductoCodigoAlMomento { get; set; }

        [Required]
        public int Cantidad { get; set; }

        [Required]
        [Column(TypeName = "decimal(18,2)")]
        public decimal PrecioUnitario { get; set; }

        // VENTA-CREDITO-ELEGIBILIDAD-DESCUENTO-FIX: porcentaje (0-100) sobre PrecioUnitario*Cantidad,
        // no importe absoluto. Único consumidor autoritativo: VentaService.CalcularSubtotalLineaConDescuento.
        [Column(TypeName = "decimal(18,2)")]
        public decimal Descuento { get; set; } = 0;

        [Required]
        [Column(TypeName = "decimal(18,2)")]
        public decimal Subtotal { get; set; }

        [Column(TypeName = "decimal(5,2)")]
        public decimal PorcentajeIVA { get; set; }

        public int? AlicuotaIVAId { get; set; }

        [StringLength(100)]
        public string? AlicuotaIVANombre { get; set; }

        [Column(TypeName = "decimal(18,2)")]
        public decimal PrecioUnitarioNeto { get; set; }

        [Column(TypeName = "decimal(18,2)")]
        public decimal IVAUnitario { get; set; }

        [Column(TypeName = "decimal(18,2)")]
        public decimal SubtotalNeto { get; set; }

        [Column(TypeName = "decimal(18,2)")]
        public decimal SubtotalIVA { get; set; }

        [Column(TypeName = "decimal(18,2)")]
        public decimal DescuentoGeneralProrrateado { get; set; }

        [Column(TypeName = "decimal(18,2)")]
        public decimal SubtotalFinalNeto { get; set; }

        [Column(TypeName = "decimal(18,2)")]
        public decimal SubtotalFinalIVA { get; set; }

        [Column(TypeName = "decimal(18,2)")]
        public decimal SubtotalFinal { get; set; }

        [Column(TypeName = "decimal(18,2)")]
        public decimal CostoUnitarioAlMomento { get; set; }

        [Column(TypeName = "decimal(18,2)")]
        public decimal CostoTotalAlMomento { get; set; }

        [Column(TypeName = "decimal(5,2)")]
        public decimal ComisionPorcentajeAplicada { get; set; } = 0m;

        [Column(TypeName = "decimal(18,2)")]
        public decimal ComisionMonto { get; set; } = 0m;

        [StringLength(200)]
        public string? Observaciones { get; set; }

        // Forma de pago por ítem (Fase 16.2 — todos nullable para compatibilidad con ventas existentes)
        public TipoPago? TipoPago { get; set; }
        public int? ProductoCondicionPagoPlanId { get; set; }

        [Column(TypeName = "decimal(5,2)")]
        public decimal? PorcentajeAjustePlanAplicado { get; set; }

        [Column(TypeName = "decimal(18,2)")]
        public decimal? MontoAjustePlanAplicado { get; set; }

        // Trazabilidad individual (Fase 8.2.E — nullable para compatibilidad con ventas históricas)
        public int? ProductoUnidadId { get; set; }

        // Navegación
        public virtual Venta Venta { get; set; } = null!;
        public virtual Producto Producto { get; set; } = null!;
        public virtual ProductoCondicionPagoPlan? ProductoCondicionPagoPlan { get; set; }
        public virtual ProductoUnidad? ProductoUnidad { get; set; }
    }
}
