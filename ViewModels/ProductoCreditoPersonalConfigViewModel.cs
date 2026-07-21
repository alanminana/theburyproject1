using System.ComponentModel.DataAnnotations;

namespace TheBuryProject.ViewModels
{
    /// <summary>
    /// Configuración de Crédito Personal específica de un producto.
    /// Sobrescribe la configuración global; si no hay planes propios, el producto hereda la global.
    /// </summary>
    public class ProductoCreditoPersonalConfigViewModel
    {
        [Display(Name = "Admite crédito personal")]
        public bool AdmiteCreditoPersonal { get; set; } = true;

        [Display(Name = "Máx. cuotas crédito personal")]
        [Range(1, 120, ErrorMessage = "El máximo de cuotas debe estar entre 1 y 120")]
        public int? MaxCuotasCredito { get; set; }

        /// <summary>
        /// Planes de cuotas propios del producto. Solo se persisten las filas activas
        /// o las que ya existían (para conservar historial de tasas desactivadas).
        /// </summary>
        public List<CuotaCreditoPersonalViewModel> Cuotas { get; set; } = new();

        public bool TienePlanesPropios => Cuotas.Any(c => c.Id > 0 || c.Activo);
    }
}
