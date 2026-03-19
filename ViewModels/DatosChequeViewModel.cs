using System.ComponentModel.DataAnnotations;

namespace TheBuryProject.ViewModels
{
    public class DatosChequeViewModel
    {
        public int Id { get; set; }

        public int VentaId { get; set; }

        [Display(Name = "Número de Cheque")]
        [Required(ErrorMessage = "El número de cheque es requerido")]
        [StringLength(50)]
        public string NumeroCheque { get; set; } = string.Empty;

        [Display(Name = "Banco")]
        [Required(ErrorMessage = "El banco es requerido")]
        [StringLength(100)]
        public string Banco { get; set; } = string.Empty;

        [Display(Name = "Titular")]
        [Required(ErrorMessage = "El titular es requerido")]
        [StringLength(200)]
        public string Titular { get; set; } = string.Empty;

        [Display(Name = "CUIT")]
        [StringLength(20)]
        public string? CUIT { get; set; }

        [Display(Name = "Fecha de Emisión")]
        [Required(ErrorMessage = "La fecha de emisión es requerida")]
        [DataType(DataType.Date)]
        public DateTime FechaEmision { get; set; } = DateTime.UtcNow;

        [Display(Name = "Fecha de Vencimiento")]
        [Required(ErrorMessage = "La fecha de vencimiento es requerida")]
        [DataType(DataType.Date)]
        public DateTime FechaVencimiento { get; set; }

        [Display(Name = "Monto")]
        [Required(ErrorMessage = "El monto es requerido")]
        [DisplayFormat(DataFormatString = "{0:C2}")]
        public decimal Monto { get; set; }

        [Display(Name = "Observaciones")]
        [StringLength(500)]
        public string? Observaciones { get; set; }
    }
}