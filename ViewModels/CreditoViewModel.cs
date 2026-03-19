using System.ComponentModel.DataAnnotations;
using TheBuryProject.Models.Enums;

namespace TheBuryProject.ViewModels
{
    public class CreditoViewModel
    {
        public int Id { get; set; }

        public ClienteResumenViewModel Cliente { get; set; } = new();

        public ClienteResumenViewModel? Garante { get; set; }

        [Display(Name = "Cliente")]
        [Required(ErrorMessage = "Debe seleccionar un cliente")]
        public int ClienteId
        {
            get => Cliente.Id;
            set => Cliente.Id = value;
        }

        [Display(Name = "Número de Crédito")]
        public string? Numero { get; set; }

        [Display(Name = "Monto Solicitado")]
        [Required(ErrorMessage = "El monto solicitado es requerido")]
        [Range(1000, 10000000, ErrorMessage = "El monto debe estar entre $1.000 y $10.000.000")]
        public decimal MontoSolicitado { get; set; }

        [Display(Name = "Monto Aprobado")]
        [Range(0, 10000000, ErrorMessage = "El monto debe estar entre $0 y $10.000.000")]
        public decimal MontoAprobado { get; set; }

        [Display(Name = "Tasa de Interés Mensual (%)")]
        [Required(ErrorMessage = "La tasa de interés es requerida")]
        [Range(0, 100, ErrorMessage = "La tasa debe estar entre 0% y 100%")]
        public decimal TasaInteres { get; set; }

        [Display(Name = "Cantidad de Cuotas")]
        // Ya no es requerido - las cuotas se definen al momento de la venta
        public int CantidadCuotas { get; set; }

        [Display(Name = "Monto por Cuota")]
        public decimal MontoCuota { get; set; }

        [Display(Name = "CFTEA (%)")]
        public decimal CFTEA { get; set; }

        [Display(Name = "Total a Pagar")]
        public decimal TotalAPagar { get; set; }

        // Alias para compatibilidad
        public decimal MontoTotal => TotalAPagar;

        [Display(Name = "Saldo Pendiente")]
        public decimal SaldoPendiente { get; set; }

        [Display(Name = "Estado")]
        public EstadoCredito Estado { get; set; }

        [Display(Name = "Fecha de Solicitud")]
        [DataType(DataType.Date)]
        public DateTime FechaSolicitud { get; set; } = DateTime.UtcNow;

        [Display(Name = "Fecha de Aprobación")]
        [DataType(DataType.Date)]
        public DateTime? FechaAprobacion { get; set; }

        [Display(Name = "Fecha de Finalización")]
        [DataType(DataType.Date)]
        public DateTime? FechaFinalizacion { get; set; }

        [Display(Name = "Fecha Primera Cuota")]
        [DataType(DataType.Date)]
        public DateTime? FechaPrimeraCuota { get; set; }

        [Display(Name = "Puntaje de Riesgo Inicial")]
        public decimal PuntajeRiesgoInicial { get; set; }

        [Display(Name = "Garante")]
        public int? GaranteId
        {
            get => Garante?.Id;
            set
            {
                if (value.HasValue)
                {
                    Garante ??= new ClienteResumenViewModel();
                    Garante.Id = value.Value;
                }
                else
                {
                    Garante = null;
                }
            }
        }

        [Display(Name = "Requiere Garante")]
        public bool RequiereGarante { get; set; }

        [Display(Name = "Aprobado Por")]
        public string? AprobadoPor { get; set; }

        [Display(Name = "Observaciones")]
        [DataType(DataType.MultilineText)]
        public string? Observaciones { get; set; }

        // Propiedades de navegación para las vistas
        public string? ClienteNombre
        {
            get => Cliente.NombreCompleto;
            set => Cliente.NombreCompleto = value ?? string.Empty;
        }
        public string? GaranteNombre
        {
            get => Garante?.NombreCompleto;
            set
            {
                if (!string.IsNullOrWhiteSpace(value))
                {
                    Garante ??= new ClienteResumenViewModel();
                    Garante.NombreCompleto = value;
                }
            }
        }

        // Lista de cuotas
        public List<CuotaViewModel>? Cuotas { get; set; }
    }
}