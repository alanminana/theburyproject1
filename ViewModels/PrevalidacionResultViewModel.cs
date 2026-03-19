using TheBuryProject.Models.Enums;

namespace TheBuryProject.ViewModels
{
    /// <summary>
    /// Resultado de la prevalidación crediticia para mostrar en UI.
    /// Se evalúa ANTES de guardar la venta para informar al vendedor.
    /// NO persiste ni crea crédito; es solo informativo.
    /// </summary>
    public class PrevalidacionResultViewModel
    {
        /// <summary>
        /// Resultado de la prevalidación: Aprobable, RequiereAutorizacion, NoViable
        /// </summary>
        public ResultadoPrevalidacion Resultado { get; set; }

        /// <summary>
        /// Color del badge para UI (success, warning, danger)
        /// </summary>
        public string ColorBadge => Resultado switch
        {
            ResultadoPrevalidacion.Aprobable => "success",
            ResultadoPrevalidacion.RequiereAutorizacion => "warning",
            ResultadoPrevalidacion.NoViable => "danger",
            _ => "secondary"
        };

        /// <summary>
        /// Ícono para mostrar en UI
        /// </summary>
        public string Icono => Resultado switch
        {
            ResultadoPrevalidacion.Aprobable => "bi-check-circle-fill",
            ResultadoPrevalidacion.RequiereAutorizacion => "bi-exclamation-triangle-fill",
            ResultadoPrevalidacion.NoViable => "bi-x-circle-fill",
            _ => "bi-question-circle"
        };

        /// <summary>
        /// Texto del estado para mostrar al usuario
        /// </summary>
        public string TextoEstado => Resultado switch
        {
            ResultadoPrevalidacion.Aprobable => "Cliente apto para crédito",
            ResultadoPrevalidacion.RequiereAutorizacion => "Requiere autorización",
            ResultadoPrevalidacion.NoViable => "No viable para crédito",
            _ => "Sin evaluar"
        };

        /// <summary>
        /// Lista de motivos/razones del resultado
        /// </summary>
        public List<MotivoPrevalidacion> Motivos { get; set; } = new();

        /// <summary>
        /// Mensaje resumen consolidado
        /// </summary>
        public string MensajeResumen => GenerarMensajeResumen();

        /// <summary>
        /// Cupo disponible del cliente (si aplica)
        /// </summary>
        public decimal CupoDisponible { get; set; }

        /// <summary>
        /// Límite de crédito asignado al cliente
        /// </summary>
        public decimal? LimiteCredito { get; set; }

        /// <summary>
        /// Crédito ya utilizado por el cliente
        /// </summary>
        public decimal CreditoUtilizado { get; set; }

        /// <summary>
        /// Indica si el cliente tiene mora
        /// </summary>
        public bool TieneMora { get; set; }

        /// <summary>
        /// Días de mora máximos (si tiene mora)
        /// </summary>
        public int? DiasMora { get; set; }

        /// <summary>
        /// Monto de mora pendiente (si tiene mora)
        /// </summary>
        public decimal? MontoMora { get; set; }

        /// <summary>
        /// Indica si la documentación está completa
        /// </summary>
        public bool DocumentacionCompleta { get; set; }

        /// <summary>
        /// Lista de documentos faltantes (si hay)
        /// </summary>
        public List<string> DocumentosFaltantes { get; set; } = new();

        /// <summary>
        /// Lista de documentos vencidos (si hay)
        /// </summary>
        public List<string> DocumentosVencidos { get; set; } = new();

        /// <summary>
        /// Timestamp de cuando se ejecutó la prevalidación
        /// </summary>
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// ID del cliente evaluado
        /// </summary>
        public int ClienteId { get; set; }

        /// <summary>
        /// Monto que se intentó validar
        /// </summary>
        public decimal MontoSolicitado { get; set; }

        /// <summary>
        /// Indica si la prevalidación permite guardar la venta
        /// </summary>
        public bool PermiteGuardar => Resultado != ResultadoPrevalidacion.NoViable;

        private string GenerarMensajeResumen()
        {
            if (Resultado == ResultadoPrevalidacion.Aprobable)
            {
                return $"Cliente apto para crédito. Cupo disponible: {CupoDisponible:C0}";
            }

            if (!Motivos.Any())
            {
                return TextoEstado;
            }

            var mensajes = Motivos.Select(m => m.Descripcion);
            return string.Join(". ", mensajes);
        }
    }

    /// <summary>
    /// Motivo individual de la prevalidación
    /// </summary>
    public class MotivoPrevalidacion
    {
        /// <summary>
        /// Categoría del motivo
        /// </summary>
        public CategoriaMotivo Categoria { get; set; }

        /// <summary>
        /// Título corto del motivo
        /// </summary>
        public string Titulo { get; set; } = string.Empty;

        /// <summary>
        /// Descripción del motivo para mostrar al usuario
        /// </summary>
        public string Descripcion { get; set; } = string.Empty;

        /// <summary>
        /// Acción sugerida para resolver el problema (si aplica)
        /// </summary>
        public string? AccionSugerida { get; set; }

        /// <summary>
        /// URL para resolver el problema (si aplica)
        /// </summary>
        public string? UrlAccion { get; set; }

        /// <summary>
        /// Indica si este motivo es bloqueante (NoViable) o solo requiere autorización
        /// </summary>
        public bool EsBloqueante { get; set; }
    }

    /// <summary>
    /// Categorías de motivos de prevalidación
    /// </summary>
    public enum CategoriaMotivo
    {
        /// <summary>
        /// Problema relacionado con documentación
        /// </summary>
        Documentacion = 1,

        /// <summary>
        /// Problema relacionado con cupo/límite de crédito
        /// </summary>
        Cupo = 2,

        /// <summary>
        /// Problema relacionado con mora
        /// </summary>
        Mora = 3,

        /// <summary>
        /// Problema relacionado con el estado general del cliente
        /// </summary>
        EstadoCliente = 4,

        /// <summary>
        /// Problema relacionado con configuración del sistema
        /// </summary>
        Configuracion = 5
    }

    /// <summary>
    /// Resultado intermedio de evaluación crediticia.
    /// Se usa internamente para unificar la lógica y luego mapear a PrevalidacionResultViewModel o ValidacionVentaResult.
    /// </summary>
    public class EvaluacionCrediticiaIntermedia
    {
        public int ClienteId { get; set; }
        public decimal MontoSolicitado { get; set; }
        
        /// <summary>
        /// NoViable, RequiereAutorizacion, o Aprobable
        /// </summary>
        public ResultadoPrevalidacion Resultado { get; set; } = ResultadoPrevalidacion.Aprobable;
        
        /// <summary>
        /// Estado de aptitud crediticia del cliente (del semáforo)
        /// </summary>
        public TheBuryProject.Models.Enums.EstadoCrediticioCliente EstadoAptitud { get; set; } = TheBuryProject.Models.Enums.EstadoCrediticioCliente.NoEvaluado;
        
        /// <summary>
        /// Lista unificada de problemas encontrados
        /// </summary>
        public List<ProblemaCredito> Problemas { get; set; } = new();
        
        // Datos de cupo
        public decimal? LimiteCredito { get; set; }
        public decimal CupoDisponible { get; set; }
        public decimal CreditoUtilizado { get; set; }
        
        // Datos de mora
        public bool TieneMora { get; set; }
        public int? DiasMora { get; set; }
        public decimal? MontoMora { get; set; }
        
        // Documentación
        public bool DocumentacionCompleta { get; set; }
        public List<string> DocumentosFaltantes { get; set; } = new();
        public List<string> DocumentosVencidos { get; set; } = new();
        
        public bool EsNoViable => Resultado == ResultadoPrevalidacion.NoViable;
        public bool RequiereAutorizacion => Resultado == ResultadoPrevalidacion.RequiereAutorizacion;
    }

    /// <summary>
    /// Problema de crédito unificado que puede mapearse a MotivoPrevalidacion o RequisitoPendiente/RazonAutorizacion
    /// </summary>
    public class ProblemaCredito
    {
        public CategoriaMotivo Categoria { get; set; }
        public string Titulo { get; set; } = string.Empty;
        public string Descripcion { get; set; } = string.Empty;
        public string? AccionSugerida { get; set; }
        public string? UrlAccion { get; set; }
        public bool EsBloqueante { get; set; }
        public decimal? ValorAsociado { get; set; }
        public decimal? ValorLimite { get; set; }
        
        /// <summary>
        /// Detalle adicional para la razón (ej: descripción de mora)
        /// </summary>
        public string? DetalleAdicional { get; set; }
        
        /// <summary>
        /// Mapea a TipoRequisitoPendiente cuando es bloqueante
        /// </summary>
        public TipoRequisitoPendiente? TipoRequisito { get; set; }
        
        /// <summary>
        /// Mapea a TipoRazonAutorizacion cuando no es bloqueante
        /// </summary>
        public TipoRazonAutorizacion? TipoRazon { get; set; }
    }
}
