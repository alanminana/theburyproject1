using System.ComponentModel.DataAnnotations;
using TheBuryProject.Models.Enums;
using TheBuryProject.Services;

namespace TheBuryProject.ViewModels
{
    public class CuotaViewModel
    {
        public int Id { get; set; }

        [Display(Name = "Crédito")]
        public int CreditoId { get; set; }

        // NUEVAS PROPIEDADES
        public string CreditoNumero { get; set; } = string.Empty;
        public string ClienteNombre { get; set; } = string.Empty;

        [Display(Name = "Cuota Nro.")]
        public int NumeroCuota { get; set; }

        [Display(Name = "Capital")]
        [DisplayFormat(DataFormatString = "{0:C2}")]
        public decimal MontoCapital { get; set; }

        [Display(Name = "Interés")]
        [DisplayFormat(DataFormatString = "{0:C2}")]
        public decimal MontoInteres { get; set; }

        [Display(Name = "Total Cuota")]
        [DisplayFormat(DataFormatString = "{0:C2}")]
        public decimal MontoTotal { get; set; }

        [Display(Name = "Fecha Vencimiento")]
        [DataType(DataType.Date)]
        [DisplayFormat(DataFormatString = "{0:dd/MM/yyyy}")]
        public DateTime FechaVencimiento { get; set; }

        [Display(Name = "Fecha Pago")]
        [DataType(DataType.Date)]
        [DisplayFormat(DataFormatString = "{0:dd/MM/yyyy}")]
        public DateTime? FechaPago { get; set; }

        [Display(Name = "Monto Pagado")]
        [DisplayFormat(DataFormatString = "{0:C2}")]
        public decimal MontoPagado { get; set; }

        [Display(Name = "Punitorio")]
        [DisplayFormat(DataFormatString = "{0:C2}")]
        public decimal MontoPunitorio { get; set; }

        [Display(Name = "Estado")]
        public EstadoCuota Estado { get; set; }

        [Display(Name = "Medio de Pago")]
        public string? MedioPago { get; set; }

        [Display(Name = "Recargo medio de pago")]
        [DisplayFormat(DataFormatString = "{0:C2}")]
        public decimal RecargoMedioPago { get; set; }

        [Display(Name = "Comprobante")]
        public string? ComprobantePago { get; set; }

        [Display(Name = "Observaciones")]
        [DataType(DataType.MultilineText)]
        public string? Observaciones { get; set; }

        // Propiedades calculadas
        public string EstadoTexto => Estado.ToString();

        /// <summary>
        /// PUN-ML7: "en mora hoy", derivado con <see cref="EstadoCuotaResolver.EstaVencidaDerivado"/>
        /// contra la fecha comercial de Argentina (antes: <c>DateTime.UtcNow</c> directo — cruzaba de
        /// día hasta 3hs antes de la medianoche real, y solo consideraba <see cref="EstadoCuota.Pendiente"/>,
        /// dejando afuera una cuota <see cref="EstadoCuota.Parcial"/> vencida, a diferencia del resto
        /// del sistema — ver <c>ClienteScoringCalculator</c>/<c>MoraService</c>/<c>ClienteAptitudService</c>).
        /// Un ViewModel no recibe DI: usa <see cref="RelojComercial.Sistema"/>, el mismo reloj de
        /// producción por defecto que ya usan los servicios cuando no se les inyecta uno de test.
        /// </summary>
        public bool EstaVencida =>
            EstadoCuotaResolver.EstaVencidaDerivado(Estado, FechaVencimiento, RelojComercial.Sistema.HoyComercial);
        public int DiasAtraso =>
            EstadoCuotaResolver.DiasAtrasoDerivado(Estado, FechaVencimiento, RelojComercial.Sistema.HoyComercial);
        public decimal SaldoPendiente => MontoTotal + MontoPunitorio - MontoPagado;

        // Propiedades de alerta visual
        [Display(Name = "Color de Alerta")]
        public string ColorAlerta { get; set; } = "#FF0000"; // Default rojo

        [Display(Name = "Descripción Alerta")]
        public string? DescripcionAlerta { get; set; }

        [Display(Name = "Prioridad")]
        public int NivelPrioridad { get; set; } = 5; // Default alta prioridad
    }
}