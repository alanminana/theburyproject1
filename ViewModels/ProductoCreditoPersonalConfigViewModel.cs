using System.ComponentModel.DataAnnotations;

namespace TheBuryProject.ViewModels
{
    /// <summary>
    /// Estado declarado de Crédito Personal para un producto. Es la señal explícita que la UI
    /// envía junto con <see cref="ProductoCreditoPersonalConfigViewModel.AdmiteCreditoPersonal"/>
    /// y <see cref="ProductoCreditoPersonalConfigViewModel.Cuotas"/>; el guardado rechaza
    /// cualquier combinación donde el modo declarado no sea consistente con esos dos datos
    /// (ver <c>ProductoCreditoPersonalConfigService.Validar</c>). No es una columna propia: se
    /// deriva de <c>ProductoCreditoRestriccion.Permitido</c> y de si existen planes propios
    /// activos, así que no requiere migración.
    /// </summary>
    public enum ModoCreditoPersonalProducto
    {
        HeredaGlobal = 0,
        ConfiguracionPropia = 1,
        NoDisponible = 2
    }

    /// <summary>
    /// Configuración de Crédito Personal específica de un producto.
    /// Sobrescribe la configuración global; si no hay planes propios, el producto hereda la global.
    /// </summary>
    public class ProductoCreditoPersonalConfigViewModel
    {
        /// <summary>
        /// Estado declarado (ver <see cref="ModoCreditoPersonalProducto"/>). Debe ser consistente
        /// con <see cref="AdmiteCreditoPersonal"/> y con los planes activos en <see cref="Cuotas"/>;
        /// el guardado rechaza combinaciones contradictorias (p. ej. "Hereda global" con planes
        /// propios activos, o "No disponible" con <see cref="AdmiteCreditoPersonal"/> en true).
        /// </summary>
        public ModoCreditoPersonalProducto Modo { get; set; } = ModoCreditoPersonalProducto.HeredaGlobal;

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
    }
}
