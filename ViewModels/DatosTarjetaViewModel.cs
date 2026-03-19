using System.ComponentModel.DataAnnotations;
using TheBuryProject.Models.Enums;

namespace TheBuryProject.ViewModels
{
    public class DatosTarjetaViewModel
    {
        public int Id { get; set; }

        public int VentaId { get; set; }

        public int? ConfiguracionTarjetaId { get; set; }

        [Display(Name = "Tarjeta")]
        [Required(ErrorMessage = "El nombre de la tarjeta es requerido")]
        [StringLength(100)]
        public string NombreTarjeta { get; set; } = string.Empty;

        [Display(Name = "Tipo")]
        [Required(ErrorMessage = "El tipo de tarjeta es requerido")]
        public TipoTarjeta TipoTarjeta { get; set; }

        [Display(Name = "Cantidad de Cuotas")]
        [Range(1, 60)]
        public int? CantidadCuotas { get; set; }

        [Display(Name = "Tipo de Cuota")]
        public TipoCuotaTarjeta? TipoCuota { get; set; }

        [Display(Name = "Tasa de Interés (%)")]
        public decimal? TasaInteres { get; set; }

        [Display(Name = "Monto por Cuota")]
        [DisplayFormat(DataFormatString = "{0:C2}")]
        public decimal? MontoCuota { get; set; }

        [Display(Name = "Monto Total con Interés")]
        [DisplayFormat(DataFormatString = "{0:C2}")]
        public decimal? MontoTotalConInteres { get; set; }

        [Display(Name = "Recargo Aplicado")]
        [DisplayFormat(DataFormatString = "{0:C2}")]
        public decimal? RecargoAplicado { get; set; }

        [Display(Name = "Número de Autorización")]
        [StringLength(50)]
        public string? NumeroAutorizacion { get; set; }

        [Display(Name = "Observaciones")]
        [StringLength(500)]
        public string? Observaciones { get; set; }
    }
}