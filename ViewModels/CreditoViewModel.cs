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

        /// <summary>
        /// Anticipo propuesto por una cotización convertida (intención, no autoridad). Precarga el
        /// formulario de Configurar Venta; ver Credito.AnticipoPreseleccionado.
        /// </summary>
        public decimal AnticipoPreseleccionado { get; set; }

        [Display(Name = "Monto por Cuota")]
        public decimal MontoCuota { get; set; }

        [Display(Name = "CFTEA (%)")]
        public decimal CFTEA { get; set; }

        /// <summary>
        /// CFTEA para presentación en Details, recalculado desde los importes reales del
        /// crédito (nunca desde <see cref="CFTEA"/> a secas: en créditos históricos ese
        /// snapshot quedó en 0 por no haberse calculado nunca, indistinguible de un 0%
        /// real). Null cuando no hay datos suficientes para calcularlo. Solo lo llena
        /// <c>CreditoService.GetByIdAsync</c>.
        /// </summary>
        public decimal? CfteaPresentacion { get; set; }

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

        /// <summary>F2: el crédito tiene solicitado el cobro de la 1ª cuota al confirmar la venta.</summary>
        public bool CobrarPrimeraCuotaSolicitada { get; set; }

        /// <summary>Medio de pago elegido para el cobro inmediato de la 1ª cuota (si se solicitó).</summary>
        public string? MedioPagoPrimeraCuota { get; set; }

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

        public List<CreditoProductoAsociadoViewModel> ProductosAsociados { get; set; } = new();

        // ── Trazabilidad de restricciones de cuotas (Fase 9.5b) ──────────────
        [Display(Name = "Mínimo de Cuotas Permitidas")]
        public int? CuotasMinimasPermitidas { get; set; }

        [Display(Name = "Máximo de Cuotas Permitidas")]
        public int? CuotasMaximasPermitidas { get; set; }

        [Display(Name = "Fuente Restricción Cuotas")]
        public string? FuenteRestriccionCuotasSnap { get; set; }

        [Display(Name = "Producto Restrictivo (snapshot)")]
        public int? ProductoIdRestrictivoSnap { get; set; }

        [Display(Name = "Máximo Cuotas Base (sin restricción producto)")]
        public int? MaxCuotasBaseSnap { get; set; }
    }

    public class CreditoProductoAsociadoViewModel
    {
        public int ProductoId { get; set; }

        public string ProductoNombre { get; set; } = string.Empty;

        public string? ProductoCodigo { get; set; }

        public int Cantidad { get; set; }

        public decimal Total { get; set; }
    }
}
