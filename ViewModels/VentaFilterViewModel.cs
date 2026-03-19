using System.ComponentModel.DataAnnotations;
using TheBuryProject.Models.Enums;

namespace TheBuryProject.ViewModels
{
    public class VentaFilterViewModel
    {
        [Display(Name = "Cliente")]
        public int? ClienteId { get; set; }

        [Display(Name = "Número")]
        public string? Numero { get; set; }

        [Display(Name = "Desde")]
        [DataType(DataType.Date)]
        public DateTime? FechaDesde { get; set; }

        [Display(Name = "Hasta")]
        [DataType(DataType.Date)]
        public DateTime? FechaHasta { get; set; }

        [Display(Name = "Estado")]
        public EstadoVenta? Estado { get; set; }

        [Display(Name = "Tipo de Pago")]
        public TipoPago? TipoPago { get; set; }

        [Display(Name = "Estado de Autorización")]
        public EstadoAutorizacionVenta? EstadoAutorizacion { get; set; }
    }
}