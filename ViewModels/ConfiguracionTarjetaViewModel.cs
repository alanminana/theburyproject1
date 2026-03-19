using System.ComponentModel.DataAnnotations;
using TheBuryProject.Models.Enums;

namespace TheBuryProject.ViewModels
{
    public class ConfiguracionTarjetaViewModel
    {
        public int Id { get; set; }

        public int ConfiguracionPagoId { get; set; }

        [Display(Name = "Nombre de Tarjeta")]
        [Required(ErrorMessage = "El nombre de la tarjeta es requerido")]
        [StringLength(100)]
        public string NombreTarjeta { get; set; } = string.Empty;

        [Display(Name = "Tipo")]
        [Required(ErrorMessage = "El tipo de tarjeta es requerido")]
        public TipoTarjeta TipoTarjeta { get; set; }

        [Display(Name = "Activa")]
        public bool Activa { get; set; } = true;

        [Display(Name = "Permite Cuotas")]
        public bool PermiteCuotas { get; set; } = false;

        [Display(Name = "Cantidad Máxima de Cuotas")]
        [Range(1, 60)]
        public int? CantidadMaximaCuotas { get; set; }

        [Display(Name = "Tipo de Cuota")]
        public TipoCuotaTarjeta? TipoCuota { get; set; }

        [Display(Name = "Tasa de Interés Mensual (%)")]
        [Range(0, 100)]
        public decimal? TasaInteresesMensual { get; set; }

        [Display(Name = "Tiene Recargo (Débito)")]
        public bool TieneRecargoDebito { get; set; } = false;

        [Display(Name = "% Recargo Débito")]
        [Range(0, 100)]
        public decimal? PorcentajeRecargoDebito { get; set; }

        [Display(Name = "Observaciones")]
        [StringLength(500)]
        public string? Observaciones { get; set; }
    }
}