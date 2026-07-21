using System.ComponentModel.DataAnnotations;
using TheBuryProject.Models.Enums;

namespace TheBuryProject.ViewModels
{
    public class MovimientoStockViewModel
    {
        public int Id { get; set; }

        [Display(Name = "Producto")]
        public int ProductoId { get; set; }

        [Display(Name = "Producto")]
        public string? ProductoNombre { get; set; }

        [Display(Name = "C�digo")]
        public string? ProductoCodigo { get; set; }

        [Display(Name = "Tipo de Movimiento")]
        public TipoMovimiento Tipo { get; set; }

        [Display(Name = "Tipo")]
        public string? TipoNombre { get; set; }

        [Display(Name = "Cantidad")]
        public decimal Cantidad { get; set; }

        [Display(Name = "Stock Anterior")]
        public decimal StockAnterior { get; set; }

        [Display(Name = "Stock Nuevo")]
        public decimal StockNuevo { get; set; }

        [Display(Name = "Costo unitario")]
        public decimal CostoUnitarioAlMomento { get; set; }

        [Display(Name = "Costo total")]
        public decimal CostoTotalAlMomento { get; set; }

        [Display(Name = "Fuente costo")]
        public string FuenteCosto { get; set; } = "NoInformado";

        [Display(Name = "Referencia")]
        public string? Referencia { get; set; }

        [Display(Name = "Orden de Compra")]
        public int? OrdenCompraId { get; set; }

        [Display(Name = "Orden de Compra")]
        public string? OrdenCompraNumero { get; set; }

        [Display(Name = "Motivo")]
        public string? Motivo { get; set; }

        [Display(Name = "Fecha")]
        public DateTime Fecha { get; set; }

        [Display(Name = "Creado")]
        public DateTime CreatedAt { get; set; }

        [Display(Name = "Creado por")]
        public string? CreatedBy { get; set; }

        // ── Referencia contextual (Fase 6) ──────────────────────────────
        // Enriquecida por IMovimientoStockReferenciaResolver a partir de Referencia/OrdenCompraId.

        /// <summary>Tipo de origen del movimiento: "OrdenCompra", "Venta", "Devolucion", "Ajuste", "Otro".</summary>
        public string ReferenciaTipo { get; set; } = "Otro";

        /// <summary>Texto contextual de la referencia (ej. "Compra OC-2026-0001 — Proveedor: Frávega").</summary>
        public string? ReferenciaTexto { get; set; }

        /// <summary>Venta relacionada (salida por venta), si se pudo resolver.</summary>
        public int? VentaId { get; set; }
        public string? VentaNumero { get; set; }

        public int? ClienteId { get; set; }
        public string? ClienteNombre { get; set; }

        public int? ProveedorId { get; set; }
        public string? ProveedorNombre { get; set; }

        /// <summary>Modalidad / medio de pago de la venta que originó la salida.</summary>
        public string? MedioPagoTexto { get; set; }
    }
}