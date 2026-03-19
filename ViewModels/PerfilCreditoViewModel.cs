using System.ComponentModel.DataAnnotations;

namespace TheBuryProject.ViewModels
{
    /// <summary>
    /// ViewModel para Perfiles de Crédito
    /// </summary>
    public class PerfilCreditoViewModel
    {
        public int Id { get; set; }

        [Required(ErrorMessage = "El nombre del perfil es requerido")]
        [StringLength(100, ErrorMessage = "El nombre no puede exceder 100 caracteres")]
        [Display(Name = "Nombre del Perfil")]
        public string Nombre { get; set; } = string.Empty;

        [StringLength(500, ErrorMessage = "La descripción no puede exceder 500 caracteres")]
        [Display(Name = "Descripción")]
        public string? Descripcion { get; set; }

        [Required(ErrorMessage = "La tasa mensual es requerida")]
        [Range(0, 100, ErrorMessage = "La tasa debe estar entre 0% y 100%")]
        [Display(Name = "Tasa Mensual (%)")]
        public decimal TasaMensual { get; set; }

        [Required(ErrorMessage = "Los gastos administrativos son requeridos")]
        [Range(0, 999999.99, ErrorMessage = "Los gastos deben estar entre $0 y $999,999.99")]
        [Display(Name = "Gastos Administrativos ($)")]
        public decimal GastosAdministrativos { get; set; } = 0m;

        [Required(ErrorMessage = "El mínimo de cuotas es requerido")]
        [Range(1, 120, ErrorMessage = "El mínimo debe estar entre 1 y 120")]
        [Display(Name = "Mínimo de Cuotas")]
        public int MinCuotas { get; set; } = 1;

        [Required(ErrorMessage = "El máximo de cuotas es requerido")]
        [Range(1, 120, ErrorMessage = "El máximo debe estar entre 1 y 120")]
        [Display(Name = "Máximo de Cuotas")]
        public int MaxCuotas { get; set; } = 24;

        [Display(Name = "Activo")]
        public bool Activo { get; set; } = true;

        [Display(Name = "Orden")]
        public int Orden { get; set; } = 0;

        [Timestamp]
        public byte[]? RowVersion { get; set; }
    }
}
