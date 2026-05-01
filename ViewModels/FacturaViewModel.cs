using System.ComponentModel.DataAnnotations;
using TheBuryProject.Models.Enums;
using TheBuryProject.ViewModels;

namespace TheBuryProject.ViewModels
{
    public class FacturaViewModel
    {
        public int Id { get; set; }

        public int VentaId { get; set; }

        [Display(Name = "Número de Factura")]
        [StringLength(50)]
        public string Numero { get; set; } = string.Empty;

        [Display(Name = "Tipo de Factura")]
        [Required]
        public TipoFactura Tipo { get; set; } = TipoFactura.B;

        [Display(Name = "Punto de Venta")]
        [StringLength(50)]
        public string? PuntoVenta { get; set; }

        [Display(Name = "Fecha de Emisión")]
        [Required]
        [DataType(DataType.Date)]
        public DateTime FechaEmision { get; set; } = DateTime.UtcNow;

        [Display(Name = "Subtotal")]
        [DisplayFormat(DataFormatString = "{0:C2}")]
        public decimal Subtotal { get; set; }

        [Display(Name = "IVA")]
        [DisplayFormat(DataFormatString = "{0:C2}")]
        public decimal IVA { get; set; }

        [Display(Name = "Total")]
        [DisplayFormat(DataFormatString = "{0:C2}")]
        public decimal Total { get; set; }

        [Display(Name = "CAE")]
        [StringLength(100)]
        public string? CAE { get; set; }

        [Display(Name = "Vencimiento CAE")]
        [DataType(DataType.Date)]
        public DateTime? FechaVencimientoCAE { get; set; }

        public bool Anulada { get; set; }
        public DateTime? FechaAnulacion { get; set; }
        public string? MotivoAnulacion { get; set; }
        public List<FacturaAlicuotaResumenViewModel> ResumenAlicuotas { get; set; } = new();
    }

    public class FacturaAlicuotaResumenViewModel
    {
        public decimal PorcentajeIVA { get; set; }
        public string AlicuotaIVANombre { get; set; } = string.Empty;
        public decimal BaseImponible { get; set; }
        public decimal IVA { get; set; }
        public decimal Total { get; set; }
    }

    public static class FacturaAlicuotaResumenBuilder
    {
        public static List<FacturaAlicuotaResumenViewModel> Build(IEnumerable<VentaDetalleViewModel>? detalles)
        {
            if (detalles == null)
            {
                return new List<FacturaAlicuotaResumenViewModel>();
            }

            return detalles
                .Select(CrearItem)
                .GroupBy(item => new { item.PorcentajeIVA, item.AlicuotaIVANombre })
                .Select(group => new FacturaAlicuotaResumenViewModel
                {
                    PorcentajeIVA = group.Key.PorcentajeIVA,
                    AlicuotaIVANombre = group.Key.AlicuotaIVANombre,
                    BaseImponible = group.Sum(item => item.BaseImponible),
                    IVA = group.Sum(item => item.IVA),
                    Total = group.Sum(item => item.Total)
                })
                .OrderByDescending(item => item.PorcentajeIVA)
                .ThenBy(item => item.AlicuotaIVANombre)
                .ToList();
        }

        private static FacturaAlicuotaResumenViewModel CrearItem(VentaDetalleViewModel detalle)
        {
            var usaSnapshotsFinales = detalle.SubtotalFinal != 0m
                || detalle.SubtotalFinalNeto != 0m
                || detalle.SubtotalFinalIVA != 0m
                || detalle.DescuentoGeneralProrrateado != 0m;

            if (usaSnapshotsFinales)
            {
                return CrearItem(detalle, detalle.SubtotalFinalNeto, detalle.SubtotalFinalIVA, detalle.SubtotalFinal);
            }

            var usaSnapshotsIva = detalle.SubtotalNeto != 0m
                || detalle.SubtotalIVA != 0m;

            if (usaSnapshotsIva)
            {
                return CrearItem(detalle, detalle.SubtotalNeto, detalle.SubtotalIVA, detalle.Subtotal);
            }

            // Legacy seguro: sin snapshots de IVA no se inventa base/IVA; se expone el importe disponible.
            return CrearItem(detalle, detalle.Subtotal, 0m, detalle.Subtotal);
        }

        private static FacturaAlicuotaResumenViewModel CrearItem(
            VentaDetalleViewModel detalle,
            decimal baseImponible,
            decimal iva,
            decimal total)
        {
            var porcentaje = detalle.PorcentajeIVA;
            return new FacturaAlicuotaResumenViewModel
            {
                PorcentajeIVA = porcentaje,
                AlicuotaIVANombre = string.IsNullOrWhiteSpace(detalle.AlicuotaIVANombre)
                    ? porcentaje > 0m ? $"IVA {porcentaje:0.##}%" : "Sin IVA"
                    : detalle.AlicuotaIVANombre,
                BaseImponible = baseImponible,
                IVA = iva,
                Total = total
            };
        }
    }
}
