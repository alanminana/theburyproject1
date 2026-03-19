using System.ComponentModel.DataAnnotations;
using TheBuryProject.Models.Base;
using TheBuryProject.Models.Enums;

namespace TheBuryProject.Models.Entities
{
    /// <summary>
    /// Configuración del sistema de aptitud crediticia.
    /// Todos los campos nullable siguen el principio: si no está configurado, la regla no se aplica (deshabilitada).
    /// </summary>
    public class ConfiguracionCredito : AuditableEntity
    {
        #region Documentación Requerida

        /// <summary>
        /// Si la validación de documentación está activa.
        /// Si es false, no se valida documentación para aptitud.
        /// </summary>
        public bool ValidarDocumentacion { get; set; } = true;

        /// <summary>
        /// Lista de tipos de documento requeridos (JSON serializado).
        /// Si es null o vacío, se usan los defaults: DNI, ReciboSueldo, Servicio.
        /// </summary>
        [StringLength(500)]
        public string? TiposDocumentoRequeridos { get; set; }

        /// <summary>
        /// Si se valida el vencimiento de documentos.
        /// Si es false, documentos vencidos son aceptados.
        /// </summary>
        public bool ValidarVencimientoDocumentos { get; set; } = true;

        /// <summary>
        /// Días de gracia después del vencimiento de un documento.
        /// Si es null o 0, no hay gracia (vencido = no válido).
        /// </summary>
        public int? DiasGraciaVencimientoDocumento { get; set; } = 0;

        #endregion

        #region Límite de Crédito (Cupo)

        /// <summary>
        /// Si se valida el límite de crédito del cliente.
        /// Si es false, no se valida cupo para aptitud.
        /// </summary>
        public bool ValidarLimiteCredito { get; set; } = true;

        /// <summary>
        /// Límite de crédito mínimo que debe tener un cliente.
        /// Si es null, no hay mínimo (0 es válido).
        /// </summary>
        public decimal? LimiteCreditoMinimo { get; set; } = 1000m;

        /// <summary>
        /// Límite de crédito por defecto para clientes nuevos.
        /// Si es null, los clientes nuevos no tienen límite asignado.
        /// </summary>
        public decimal? LimiteCreditoDefault { get; set; }

        /// <summary>
        /// Porcentaje mínimo de cupo disponible para ser apto.
        /// Ej: 10 = debe tener al menos 10% del cupo disponible.
        /// Si es null, no se valida porcentaje mínimo.
        /// </summary>
        public decimal? PorcentajeCupoMinimoRequerido { get; set; }

        #endregion

        #region Validación de Mora

        /// <summary>
        /// Si se valida la mora del cliente para aptitud.
        /// Si es false, clientes en mora son aptos.
        /// </summary>
        public bool ValidarMora { get; set; } = true;

        /// <summary>
        /// Días de mora para marcar como "Requiere Autorización".
        /// Si es null, cualquier mora requiere autorización.
        /// </summary>
        public int? DiasParaRequerirAutorizacion { get; set; } = 1;

        /// <summary>
        /// Días de mora para marcar como "No Apto".
        /// Si es null, la mora no bloquea completamente (solo requiere autorización).
        /// </summary>
        public int? DiasParaNoApto { get; set; }

        /// <summary>
        /// Monto de mora para marcar como "Requiere Autorización".
        /// Si es null, no se valida monto.
        /// </summary>
        public decimal? MontoMoraParaRequerirAutorizacion { get; set; }

        /// <summary>
        /// Monto de mora para marcar como "No Apto".
        /// Si es null, la mora no bloquea por monto.
        /// </summary>
        public decimal? MontoMoraParaNoApto { get; set; }

        /// <summary>
        /// Cantidad de cuotas vencidas para marcar como "No Apto".
        /// Si es null, no se valida cantidad de cuotas.
        /// </summary>
        public int? CuotasVencidasParaNoApto { get; set; }

        #endregion

        #region Comportamiento General

        /// <summary>
        /// Si el sistema recalcula automáticamente la aptitud cuando cambian datos del cliente.
        /// </summary>
        public bool RecalculoAutomatico { get; set; } = true;

        /// <summary>
        /// Días de validez de una evaluación antes de requerir nueva evaluación.
        /// Si es null, la evaluación no expira.
        /// </summary>
        public int? DiasValidezEvaluacion { get; set; } = 30;

        /// <summary>
        /// Si se genera auditoría de cambios de estado crediticio.
        /// </summary>
        public bool AuditoriaActiva { get; set; } = true;

        /// <summary>
        /// Si se envían notificaciones cuando cambia el estado crediticio.
        /// </summary>
        public bool NotificacionesCambioEstado { get; set; } = false;

        #endregion

        #region Metadata

        /// <summary>
        /// Descripción para mostrar cuando la configuración está deshabilitada.
        /// </summary>
        [StringLength(500)]
        public string? MensajeConfiguracionDeshabilitada { get; set; } = "La validación de aptitud crediticia no está configurada. Configure los parámetros en Administración.";

        /// <summary>
        /// Última fecha de modificación de la configuración.
        /// </summary>
        public DateTime? FechaUltimaModificacion { get; set; }

        /// <summary>
        /// Usuario que realizó la última modificación.
        /// </summary>
        [StringLength(100)]
        public string? ModificadoPor { get; set; }

        #endregion
    }
}
