using System.ComponentModel.DataAnnotations;
using TheBuryProject.Models.Enums;
using TheBuryProject.Services.Models;

namespace TheBuryProject.ViewModels
{
    /// <summary>
    /// ViewModel consolidado para la vista de detalles del cliente con tabs
    /// </summary>
    public class ClienteDetalleViewModel
    {
        public ClienteViewModel Cliente { get; set; } = new();

        public List<DocumentoClienteViewModel> Documentos { get; set; } = new();

        // Nota: se utiliza como "lista de créditos del cliente" en UI; la evaluación filtra por estado.
        public List<CreditoViewModel> CreditosActivos { get; set; } = new();

        public EvaluacionCreditoResult EvaluacionCredito { get; set; } = new();

        /// <summary>
        /// Resultado de la evaluación de aptitud crediticia (semáforo)
        /// </summary>
        public AptitudCrediticiaViewModel AptitudCrediticia { get; set; } = new();

        /// <summary>
        /// Panel de visibilidad de crédito disponible por puntaje.
        /// </summary>
        public ClienteCreditoDisponiblePanelViewModel CreditoDisponiblePanel { get; set; } = new();

        public string TabActivo { get; set; } = "informacion";
    }

    public class ClienteCreditoDisponiblePanelViewModel
    {
        public NivelRiesgoCredito PuntajeActual { get; set; }

        public CreditoDisponibleResultado Valores { get; set; } = new();

        public bool TieneErrorConfiguracion { get; set; }

        public string? MensajeError { get; set; }
    }

    /// <summary>
    /// Resultado de la evaluación para solicitar un crédito
    /// </summary>
    public class EvaluacionCreditoResult
    {
        public bool TieneDocumentosCompletos { get; set; }
        public List<string> DocumentosFaltantes { get; set; } = new();

        public decimal MontoMaximoDisponible { get; set; }
        public decimal IngresosMensuales { get; set; }
        public decimal DeudaActual { get; set; }
        public decimal CapacidadPagoMensual { get; set; }
        public double PorcentajeEndeudamiento { get; set; }

        public int ScoreCrediticio { get; set; }
        public string NivelRiesgo { get; set; } = "Medio";

        public bool CumpleRequisitos { get; set; }
        public List<string> AlertasYRecomendaciones { get; set; } = new();

        public bool RequiereGarante { get; set; }
        public bool TieneGarante { get; set; }
        public string? GaranteNombre { get; set; }

        public bool PuedeAprobarConExcepcion { get; set; }
        public string? MotivoExcepcion { get; set; }
    }

    /// <summary>
    /// ViewModel para solicitar un crédito desde el cliente
    /// </summary>
    public class SolicitudCreditoViewModel
    {
        public int ClienteId { get; set; }

        [Required, Range(1, 100000000, ErrorMessage = "Monto inválido")]
        public decimal MontoSolicitado { get; set; }

        [Required, Range(1, 120, ErrorMessage = "Cantidad de cuotas inválida")]
        public int CantidadCuotas { get; set; }

        [Required, Range(0, 100, ErrorMessage = "Tasa inválida")]
        public decimal TasaInteres { get; set; } = 5.0m;

        public string? Observaciones { get; set; }

        public int? GaranteId { get; set; }

        [StringLength(100)]
        public string? GaranteNombre { get; set; }

        [StringLength(20)]
        public string? GaranteDocumento { get; set; }

        [StringLength(20)]
        public string? GaranteTelefono { get; set; }

        public bool AprobarConExcepcion { get; set; }

        [StringLength(500)]
        public string? MotivoExcepcion { get; set; }

        [StringLength(200)]
        public string? AutorizadoPor { get; set; }
    }
}
