using System.ComponentModel.DataAnnotations;
using TheBuryProject.Models.Enums;

namespace TheBuryProject.ViewModels
{
    /// <summary>
    /// Resultado de la evaluación de aptitud crediticia del cliente (semáforo).
    /// </summary>
    public class AptitudCrediticiaViewModel
    {
        /// <summary>
        /// Estado resultante: Apto, NoApto, RequiereAutorizacion
        /// </summary>
        public EstadoCrediticioCliente Estado { get; set; } = EstadoCrediticioCliente.NoEvaluado;

        /// <summary>
        /// Color del semáforo: success, warning, danger, secondary
        /// </summary>
        public string ColorSemaforo => Estado switch
        {
            EstadoCrediticioCliente.Apto => "success",
            EstadoCrediticioCliente.RequiereAutorizacion => "warning",
            EstadoCrediticioCliente.NoApto => "danger",
            _ => "secondary"
        };

        /// <summary>
        /// Icono de Bootstrap para el estado
        /// </summary>
        public string Icono => Estado switch
        {
            EstadoCrediticioCliente.Apto => "bi-check-circle-fill",
            EstadoCrediticioCliente.RequiereAutorizacion => "bi-exclamation-triangle-fill",
            EstadoCrediticioCliente.NoApto => "bi-x-circle-fill",
            _ => "bi-question-circle"
        };

        /// <summary>
        /// Texto descriptivo del estado
        /// </summary>
        public string TextoEstado => Estado switch
        {
            EstadoCrediticioCliente.Apto => "Apto para Crédito",
            EstadoCrediticioCliente.RequiereAutorizacion => "Requiere Autorización",
            EstadoCrediticioCliente.NoApto => "No Apto",
            _ => "Sin Evaluar"
        };

        /// <summary>
        /// Motivo principal del estado (si no es apto)
        /// </summary>
        public string? Motivo { get; set; }

        /// <summary>
        /// Lista de razones detalladas que afectan la aptitud
        /// </summary>
        public List<AptitudDetalleItem> Detalles { get; set; } = new();

        /// <summary>
        /// Fecha de la evaluación
        /// </summary>
        public DateTime FechaEvaluacion { get; set; } = DateTime.Now;

        /// <summary>
        /// Indica si la configuración está completa
        /// </summary>
        public bool ConfiguracionCompleta { get; set; } = true;

        /// <summary>
        /// Mensaje de advertencia si la configuración está incompleta
        /// </summary>
        public string? AdvertenciaConfiguracion { get; set; }

        // Detalles de la evaluación
        public AptitudDocumentacionDetalle Documentacion { get; set; } = new();
        public AptitudCupoDetalle Cupo { get; set; } = new();
        public AptitudMoraDetalle Mora { get; set; } = new();
    }

    /// <summary>
    /// Item de detalle de la evaluación de aptitud
    /// </summary>
    public class AptitudDetalleItem
    {
        public string Categoria { get; set; } = string.Empty;
        public string Descripcion { get; set; } = string.Empty;
        public bool EsBloqueo { get; set; } // true = NoApto, false = RequiereAutorizacion
        public string Icono { get; set; } = "bi-info-circle";
        public string Color { get; set; } = "secondary";
    }

    /// <summary>
    /// Detalle de evaluación de documentación
    /// </summary>
    public class AptitudDocumentacionDetalle
    {
        public bool Evaluada { get; set; }
        public bool Completa { get; set; }
        public List<string> DocumentosFaltantes { get; set; } = new();
        public List<string> DocumentosVencidos { get; set; } = new();
        public bool TieneVencidos { get; set; }
        public string Mensaje { get; set; } = string.Empty;
    }

    /// <summary>
    /// Detalle de evaluación de cupo
    /// </summary>
    public class AptitudCupoDetalle
    {
        public bool Evaluado { get; set; }
        public bool TieneCupoAsignado { get; set; }
        public decimal? LimiteCredito { get; set; }
        public decimal CreditoUtilizado { get; set; }
        public decimal CupoDisponible { get; set; }
        public decimal PorcentajeUtilizado { get; set; }
        public bool CupoSuficiente { get; set; }
        public string Mensaje { get; set; } = string.Empty;
    }

    /// <summary>
    /// Detalle de evaluación de mora
    /// </summary>
    public class AptitudMoraDetalle
    {
        public bool Evaluada { get; set; }
        public bool TieneMora { get; set; }
        public int DiasMaximoMora { get; set; }
        public decimal MontoTotalMora { get; set; }
        public int CuotasVencidas { get; set; }
        public bool RequiereAutorizacion { get; set; }
        public bool EsBloqueante { get; set; }
        public string Mensaje { get; set; } = string.Empty;
    }

    /// <summary>
    /// ViewModel para la configuración de aptitud crediticia
    /// </summary>
    public class ConfiguracionCreditoViewModel
    {
        public int Id { get; set; }

        // Documentación
        public bool ValidarDocumentacion { get; set; } = true;
        public List<TipoDocumentoCliente> TiposDocumentoRequeridos { get; set; } = new();
        public bool ValidarVencimientoDocumentos { get; set; } = true;
        public int DiasGraciaVencimientoDocumento { get; set; } = 0;

        // Límite de crédito
        public bool ValidarLimiteCredito { get; set; } = true;
        public decimal? LimiteCreditoMinimo { get; set; }
        public decimal? LimiteCreditoDefault { get; set; }
        public decimal? PorcentajeCupoMinimoRequerido { get; set; }

        // Mora
        public bool ValidarMora { get; set; } = true;
        public int? DiasParaRequerirAutorizacion { get; set; } = 1;
        public int? DiasParaNoApto { get; set; }
        public decimal? MontoMoraParaRequerirAutorizacion { get; set; }
        public decimal? MontoMoraParaNoApto { get; set; }
        public int? CuotasVencidasParaNoApto { get; set; }

        // General
        public bool RecalculoAutomatico { get; set; } = true;
        public int? DiasValidezEvaluacion { get; set; } = 30;
        public bool AuditoriaActiva { get; set; } = true;
    }
}
