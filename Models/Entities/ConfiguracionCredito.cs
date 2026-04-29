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

        #region Scoring

        /// <summary>
        /// Puntaje de riesgo mínimo (escala 0-10) para no ser rechazado por regla crítica.
        /// </summary>
        public decimal PuntajeRiesgoMinimo { get; set; } = 3.0m;

        /// <summary>
        /// Puntaje de riesgo (escala 0-10) a partir del cual el resultado es "Bueno" (banda media).
        /// Debe ser mayor que PuntajeRiesgoMinimo y menor que PuntajeRiesgoExcelente.
        /// </summary>
        public decimal PuntajeRiesgoMedio { get; set; } = 5.0m;

        /// <summary>
        /// Puntaje de riesgo (escala 0-10) a partir del cual el resultado es "Excelente" (banda alta).
        /// Debe ser mayor que PuntajeRiesgoMedio.
        /// </summary>
        public decimal PuntajeRiesgoExcelente { get; set; } = 7.0m;

        /// <summary>
        /// Relación cuota/ingreso máxima aceptable. Ej: 0.35 = 35%.
        /// Debe ser mayor que UmbralCuotaIngresoBajo y menor que UmbralCuotaIngresoAlto.
        /// </summary>
        public decimal RelacionCuotaIngresoMax { get; set; } = 0.35m;

        /// <summary>
        /// Umbral de relación cuota/ingreso por debajo del cual la capacidad de pago es "Excelente".
        /// Debe ser menor que RelacionCuotaIngresoMax.
        /// </summary>
        public decimal UmbralCuotaIngresoBajo { get; set; } = 0.25m;

        /// <summary>
        /// Umbral de relación cuota/ingreso por encima del cual la capacidad de pago es "Insuficiente".
        /// Debe ser mayor que RelacionCuotaIngresoMax.
        /// </summary>
        public decimal UmbralCuotaIngresoAlto { get; set; } = 0.45m;

        /// <summary>
        /// Monto solicitado a partir del cual se requiere garante.
        /// </summary>
        public decimal MontoRequiereGarante { get; set; } = 500_000m;

        /// <summary>
        /// Puntaje mínimo para resultado Aprobado (escala 0-100).
        /// </summary>
        public decimal PuntajeMinimoParaAprobacion { get; set; } = 70m;

        /// <summary>
        /// Puntaje mínimo para resultado RequiereAnalisis; por debajo → Rechazado (escala 0-100).
        /// </summary>
        public decimal PuntajeMinimoParaAnalisis { get; set; } = 50m;

        #endregion

        #region Semáforo financiero

        /// <summary>
        /// Ratio máximo cuota/monto financiado para clasificar la simulación financiera como verde.
        /// </summary>
        public decimal SemaforoFinancieroRatioVerdeMax { get; set; } = 0.08m;

        /// <summary>
        /// Ratio máximo cuota/monto financiado para clasificar la simulación financiera como amarilla.
        /// Por encima de este valor la simulación se clasifica como roja.
        /// </summary>
        public decimal SemaforoFinancieroRatioAmarilloMax { get; set; } = 0.15m;

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
