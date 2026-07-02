using System.ComponentModel.DataAnnotations;
using TheBuryProject.Helpers;
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

        /// <summary>
        /// Resultado de la evaluación de aptitud crediticia (semáforo)
        /// </summary>
        public AptitudCrediticiaViewModel AptitudCrediticia { get; set; } = new();

        /// <summary>
        /// Panel de visibilidad de crédito disponible por puntaje.
        /// </summary>
        public ClienteCreditoDisponiblePanelViewModel CreditoDisponiblePanel { get; set; } = new();

        public string TabActivo { get; set; } = "informacion";

    /// <summary>
    /// Info del garante actual del cliente. Null si no tiene garante asignado.
    /// </summary>
    public GaranteInfoViewModel? GaranteInfo { get; set; }

    /// <summary>
    /// Últimos cambios auditados de PuntajeCliente (solo lectura).
    /// </summary>
    public List<ClientePuntajeHistorialItemViewModel> HistorialPuntaje { get; set; } = new();
    }

    /// <summary>
    /// Item de solo lectura para mostrar un registro de ClientePuntajeHistorial.
    /// </summary>
    public class ClientePuntajeHistorialItemViewModel
    {
        public DateTime Fecha { get; set; }

        public decimal? PuntajeAnterior { get; set; }

        public decimal PuntajeNuevo { get; set; }

        public string Origen { get; set; } = string.Empty;

        public string? RegistradoPor { get; set; }

        public string? Observacion { get; set; }
    }

    public class ClienteCreditoDisponiblePanelViewModel
    {
        public int PuntajeActual { get; set; }

        public CreditoDisponibleResultado Valores { get; set; } = new();

        public List<ClienteNivelCreditoOpcionViewModel> NivelesDisponibles { get; set; } = new();

        public decimal PorcentajeLibre { get; set; }

        public bool TieneErrorConfiguracion { get; set; }

        public string? MensajeError { get; set; }
    }

    public class ClienteNivelCreditoOpcionViewModel
    {
        /// <summary>Puntaje interno de comportamiento (0–5) al que aplica el límite.</summary>
        public int Nivel { get; set; }

        public decimal LimiteMonto { get; set; }

        public bool Activo { get; set; }

        public string Texto => $"Puntaje {Nivel}";
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
