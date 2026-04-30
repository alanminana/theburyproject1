using TheBuryProject.Models.Base;
using TheBuryProject.Models.Enums;

namespace TheBuryProject.Models.Entities
{
    /// <summary>
    /// Registra movimientos de stock (Kardex)
    /// </summary>
    public class MovimientoStock  : AuditableEntity
    {
        public int ProductoId { get; set; }
        public TipoMovimiento Tipo { get; set; }
        public decimal Cantidad { get; set; }
        public decimal StockAnterior { get; set; }
        public decimal StockNuevo { get; set; }
        public decimal CostoUnitarioAlMomento { get; set; }
        public decimal CostoTotalAlMomento { get; set; }
        public string FuenteCosto { get; set; } = "NoInformado";
        public string? Referencia { get; set; }
        public int? OrdenCompraId { get; set; }
        public string? Motivo { get; set; }

        // Navegaci�n
        public virtual Producto Producto { get; set; } = null!;
        public virtual OrdenCompra? OrdenCompra { get; set; }
    }
}
