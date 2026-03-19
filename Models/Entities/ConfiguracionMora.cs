using System.ComponentModel.DataAnnotations;
using TheBuryProject.Models.Base;
using TheBuryProject.Models.Enums;

namespace TheBuryProject.Models.Entities
{
    /// <summary>
    /// Configuración del sistema de mora y alertas de cobranza.
    /// Todos los campos nullable siguen el principio: si no está configurado, la regla no se aplica.
    /// </summary>
    public class ConfiguracionMora : AuditableEntity
    {
        #region Cálculo de Mora

        /// <summary>
        /// Tipo de tasa: Diaria o Mensual. Si es null, no se calcula mora.
        /// </summary>
        public TipoTasaMora? TipoTasaMora { get; set; }

        /// <summary>
        /// Tasa base de mora (porcentaje). Si es null o 0, no se calcula mora.
        /// </summary>
        public decimal? TasaMoraBase { get; set; }

        /// <summary>
        /// Base sobre la cual se calcula la mora. Default: Capital
        /// </summary>
        public BaseCalculoMora? BaseCalculoMora { get; set; }

        /// <summary>
        /// Días de gracia después del vencimiento antes de aplicar mora.
        /// Si es 0 o null, la mora se aplica desde el día 1.
        /// </summary>
        public int? DiasGracia { get; set; } = 3;

        /// <summary>
        /// Si está activo el escalonamiento de tasas por antigüedad
        /// </summary>
        public bool EscalonamientoActivo { get; set; } = false;

        /// <summary>
        /// Tasa para días 1-30 de atraso (si escalonamiento activo)
        /// </summary>
        public decimal? TasaPrimerMes { get; set; }

        /// <summary>
        /// Tasa para días 31-60 de atraso (si escalonamiento activo)
        /// </summary>
        public decimal? TasaSegundoMes { get; set; }

        /// <summary>
        /// Tasa para días 61+ de atraso (si escalonamiento activo)
        /// </summary>
        public decimal? TasaTercerMesEnAdelante { get; set; }

        /// <summary>
        /// Si está activo el tope máximo de mora
        /// </summary>
        public bool TopeMaximoMoraActivo { get; set; } = false;

        /// <summary>
        /// Tipo de tope: Porcentaje o MontoFijo
        /// </summary>
        public TipoTopeMora? TipoTopeMora { get; set; }

        /// <summary>
        /// Valor del tope (% o $, según TipoTopeMora)
        /// </summary>
        public decimal? ValorTopeMora { get; set; }

        /// <summary>
        /// Mora mínima a cobrar. Si la mora calculada es menor, se cobra este monto.
        /// </summary>
        public decimal? MoraMinima { get; set; }

        #endregion

        #region Clasificación y Prioridad

        /// <summary>
        /// Días de atraso para clasificar como prioridad Media
        /// </summary>
        public int? DiasParaPrioridadMedia { get; set; }

        /// <summary>
        /// Días de atraso para clasificar como prioridad Alta
        /// </summary>
        public int? DiasParaPrioridadAlta { get; set; }

        /// <summary>
        /// Días de atraso para clasificar como prioridad Crítica
        /// </summary>
        public int? DiasParaPrioridadCritica { get; set; }

        /// <summary>
        /// Monto vencido para clasificar como prioridad Media
        /// </summary>
        public decimal? MontoParaPrioridadMedia { get; set; }

        /// <summary>
        /// Monto vencido para clasificar como prioridad Alta
        /// </summary>
        public decimal? MontoParaPrioridadAlta { get; set; }

        /// <summary>
        /// Monto vencido para clasificar como prioridad Crítica
        /// </summary>
        public decimal? MontoParaPrioridadCritica { get; set; }

        #endregion

        #region Automatización

        /// <summary>
        /// Si el proceso automático de mora está activo
        /// </summary>
        public bool ProcesoAutomaticoActivo { get; set; } = false;

        /// <summary>
        /// Hora del día para ejecutar el proceso automático
        /// </summary>
        public TimeSpan? HoraEjecucionDiaria { get; set; } = new TimeSpan(8, 0, 0);

        /// <summary>
        /// Si se generan alertas antes del vencimiento
        /// </summary>
        public bool AlertasPreventivasActivas { get; set; } = false;

        /// <summary>
        /// Días antes del vencimiento para generar alerta preventiva
        /// </summary>
        public int? DiasAntesAlertaPreventiva { get; set; }

        /// <summary>
        /// Si el sistema cambia automáticamente el estado de la cuota a "Vencida"
        /// </summary>
        public bool CambiarEstadoCuotaAuto { get; set; } = false;

        /// <summary>
        /// Si la mora se actualiza automáticamente cada día
        /// </summary>
        public bool ActualizarMoraAutomaticamente { get; set; } = false;

        /// <summary>
        /// Fecha/hora de la última ejecución del proceso
        /// </summary>
        public DateTime? UltimaEjecucion { get; set; }

        #endregion

        #region Notificaciones

        /// <summary>
        /// Master switch: si las notificaciones están activas
        /// </summary>
        public bool NotificacionesActivas { get; set; } = false;

        /// <summary>
        /// Si WhatsApp está habilitado como canal
        /// </summary>
        public bool WhatsAppActivo { get; set; } = false;

        /// <summary>
        /// Si Email está habilitado como canal
        /// </summary>
        public bool EmailActivo { get; set; } = false;

        /// <summary>
        /// Canal preferido de notificación
        /// </summary>
        public CanalNotificacion? CanalPreferido { get; set; }

        /// <summary>
        /// Si se envía notificación antes del vencimiento
        /// </summary>
        public bool NotificarProximoVencimiento { get; set; } = false;

        /// <summary>
        /// Días antes del vencimiento para notificar
        /// </summary>
        public int? DiasAntesNotificacionPreventiva { get; set; }

        /// <summary>
        /// Si se notifica cuando la cuota vence
        /// </summary>
        public bool NotificarCuotaVencida { get; set; } = false;

        /// <summary>
        /// Si se envían recordatorios periódicos de mora
        /// </summary>
        public bool NotificarMoraAcumulada { get; set; } = false;

        /// <summary>
        /// Cada cuántos días enviar recordatorio de mora
        /// </summary>
        public int? FrecuenciaRecordatorioMora { get; set; }

        /// <summary>
        /// Máximo de notificaciones por cliente por día
        /// </summary>
        public int? MaximoNotificacionesDiarias { get; set; }

        /// <summary>
        /// Máximo de notificaciones por cuota en total
        /// </summary>
        public int? MaximoNotificacionesPorCuota { get; set; }

        /// <summary>
        /// Hora desde la cual se pueden enviar notificaciones
        /// </summary>
        public TimeSpan? HoraInicioEnvio { get; set; }

        /// <summary>
        /// Hora hasta la cual se pueden enviar notificaciones
        /// </summary>
        public TimeSpan? HoraFinEnvio { get; set; }

        /// <summary>
        /// Si se envían notificaciones en fin de semana
        /// </summary>
        public bool EnviarFinDeSemana { get; set; } = false;

        #endregion

        #region Gestión de Cobranzas

        /// <summary>
        /// Días máximos sin actividad en un caso antes de escalar
        /// </summary>
        public int? DiasMaximosSinGestion { get; set; }

        /// <summary>
        /// Días máximos para esperar cumplimiento de promesa de pago
        /// </summary>
        public int? DiasParaCumplirPromesa { get; set; }

        /// <summary>
        /// Máximo de cuotas permitidas en un acuerdo de pago
        /// </summary>
        public int? MaximoCuotasAcuerdo { get; set; }

        /// <summary>
        /// Porcentaje mínimo requerido como entrega inicial en acuerdos
        /// </summary>
        public decimal? PorcentajeMinimoEntrega { get; set; }

        /// <summary>
        /// Si se permite condonar parte de la mora en acuerdos
        /// </summary>
        public bool PermitirCondonacionMora { get; set; } = false;

        /// <summary>
        /// Porcentaje máximo de mora que se puede condonar
        /// </summary>
        public decimal? PorcentajeMaximoCondonacion { get; set; }

        #endregion

        #region Bloqueos

        /// <summary>
        /// Si el bloqueo automático de clientes está activo
        /// </summary>
        public bool BloqueoAutomaticoActivo { get; set; } = false;

        /// <summary>
        /// Días de mora para bloquear al cliente
        /// </summary>
        public int? DiasParaBloquear { get; set; }

        /// <summary>
        /// Cantidad de cuotas vencidas para bloquear al cliente
        /// </summary>
        public int? CuotasVencidasParaBloquear { get; set; }

        /// <summary>
        /// Monto de mora para bloquear al cliente
        /// </summary>
        public decimal? MontoMoraParaBloquear { get; set; }

        /// <summary>
        /// Tipo de bloqueo a aplicar
        /// </summary>
        public TipoBloqueoCliente? TipoBloqueo { get; set; }

        /// <summary>
        /// Si el cliente se desbloquea automáticamente al pagar
        /// </summary>
        public bool DesbloqueoAutomatico { get; set; } = false;

        #endregion

        #region Score

        /// <summary>
        /// Si la mora impacta el score crediticio del cliente
        /// </summary>
        public bool ImpactarScorePorMora { get; set; } = false;

        /// <summary>
        /// Puntos a restar por cada cuota vencida
        /// </summary>
        public int? PuntosRestarPorCuotaVencida { get; set; }

        /// <summary>
        /// Puntos a restar por cada día de mora
        /// </summary>
        public decimal? PuntosRestarPorDiaMora { get; set; }

        /// <summary>
        /// Máximo de puntos que se pueden restar en total
        /// </summary>
        public int? PuntosMaximosARestar { get; set; }

        /// <summary>
        /// Si el score se recupera parcialmente al pagar
        /// </summary>
        public bool RecuperarScoreAlPagar { get; set; } = false;

        /// <summary>
        /// Porcentaje de puntos perdidos que se recuperan al pagar
        /// </summary>
        public decimal? PorcentajeRecuperacionScore { get; set; }

        #endregion

        #region Compatibilidad con código existente (DEPRECATED - migrar gradualmente)

        /// <summary>
        /// [DEPRECATED] Usar TasaMoraBase. Porcentaje de recargo mensual.
        /// </summary>
        [Obsolete("Usar TasaMoraBase en su lugar")]
        public decimal PorcentajeRecargo { get; set; } = 5.0m;

        /// <summary>
        /// [DEPRECATED] Usar ProcesoAutomaticoActivo. Si el cálculo es automático.
        /// </summary>
        [Obsolete("Usar ProcesoAutomaticoActivo en su lugar")]
        public bool CalculoAutomatico { get; set; } = true;

        /// <summary>
        /// [DEPRECATED] Usar NotificacionesActivas. Si la notificación es automática.
        /// </summary>
        [Obsolete("Usar NotificacionesActivas en su lugar")]
        public bool NotificacionAutomatica { get; set; } = true;

        /// <summary>
        /// [DEPRECATED] Usar ProcesoAutomaticoActivo. Si el job está activo.
        /// </summary>
        [Obsolete("Usar ProcesoAutomaticoActivo en su lugar")]
        public bool JobActivo { get; set; } = true;

        /// <summary>
        /// [DEPRECATED] Usar HoraEjecucionDiaria. Hora de ejecución.
        /// </summary>
        [Obsolete("Usar HoraEjecucionDiaria en su lugar")]
        public TimeSpan HoraEjecucion { get; set; } = new TimeSpan(8, 0, 0);

        #endregion
    }
}