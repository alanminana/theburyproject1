using System.ComponentModel.DataAnnotations;
using TheBuryProject.Models.Enums;

namespace TheBuryProject.ViewModels
{
    public class CreditoFilterViewModel
    {
        [Display(Name = "Número de Crédito")]
        public string? Numero { get; set; }

        [Display(Name = "Cliente (DNI o Nombre)")]
        public string? Cliente { get; set; }

        [Display(Name = "Estado")]
        public EstadoCredito? Estado { get; set; }

        [Display(Name = "Fecha Desde")]
        [DataType(DataType.Date)]
        public DateTime? FechaDesde { get; set; }

        [Display(Name = "Fecha Hasta")]
        [DataType(DataType.Date)]
        public DateTime? FechaHasta { get; set; }

        [Display(Name = "Monto Mínimo")]
        public decimal? MontoMinimo { get; set; }

        [Display(Name = "Monto Máximo")]
        public decimal? MontoMaximo { get; set; }

        [Display(Name = "Solo con cuotas vencidas")]
        public bool SoloCuotasVencidas { get; set; }

        [Display(Name = "Página")]
        public int PageNumber { get; set; } = 1;

        [Display(Name = "Registros por página")]
        public int PageSize { get; set; } = 20;
    }
}