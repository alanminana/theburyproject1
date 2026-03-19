using System.ComponentModel.DataAnnotations;
using TheBuryProject.Models.Enums;

namespace TheBuryProject.ViewModels.Mora
{
    /// <summary>
    /// ViewModel expandido para configuración completa del módulo de mora
    /// </summary>
    public class ConfiguracionMoraExpandidaViewModel
    {
        public int Id { get; set; }
        public byte[]? RowVersion { get; set; }

        #region Cálculo de Mora

        [Display(Name = "Tipo de Tasa")]
        public TipoTasaMora? TipoTasaMora { get; set; }

        [Display(Name = "Tasa Base de Mora (%)")]
        [Range(0, 100, ErrorMessage = "La tasa debe estar entre 0 y 100")]
        public decimal? TasaMoraBase { get; set; }

        [Display(Name = "Base de Cálculo")]
        public BaseCalculoMora? BaseCalculoMora { get; set; }

        [Display(Name = "Días de Gracia")]
        [Range(0, 90, ErrorMessage = "Los días de gracia deben estar entre 0 y 90")]
        public int? DiasGracia { get; set; }

        [Display(Name = "Escalonamiento Activo")]
        public bool EscalonamientoActivo { get; set; }

        [Display(Name = "Tasa Primer Mes (%)")]
        [Range(0, 100, ErrorMessage = "La tasa debe estar entre 0 y 100")]
        public decimal? TasaPrimerMes { get; set; }

        [Display(Name = "Tasa Segundo Mes (%)")]
        [Range(0, 100, ErrorMessage = "La tasa debe estar entre 0 y 100")]
        public decimal? TasaSegundoMes { get; set; }

        [Display(Name = "Tasa Tercer Mes+ (%)")]
        [Range(0, 100, ErrorMessage = "La tasa debe estar entre 0 y 100")]
        public decimal? TasaTercerMesEnAdelante { get; set; }

        [Display(Name = "Tope Máximo Activo")]
        public bool TopeMaximoMoraActivo { get; set; }

        [Display(Name = "Tipo de Tope")]
        public TipoTopeMora? TipoTopeMora { get; set; }

        [Display(Name = "Valor del Tope")]
        [Range(0, double.MaxValue, ErrorMessage = "El valor debe ser positivo")]
        public decimal? ValorTopeMora { get; set; }

        [Display(Name = "Mora Mínima ($)")]
        [Range(0, double.MaxValue, ErrorMessage = "El valor debe ser positivo")]
        public decimal? MoraMinima { get; set; }

        #endregion

        #region Clasificación y Prioridad

        [Display(Name = "Días para Prioridad Media")]
        [Range(1, 365, ErrorMessage = "Los días deben estar entre 1 y 365")]
        public int? DiasParaPrioridadMedia { get; set; }

        [Display(Name = "Días para Prioridad Alta")]
        [Range(1, 365, ErrorMessage = "Los días deben estar entre 1 y 365")]
        public int? DiasParaPrioridadAlta { get; set; }

        [Display(Name = "Días para Prioridad Crítica")]
        [Range(1, 365, ErrorMessage = "Los días deben estar entre 1 y 365")]
        public int? DiasParaPrioridadCritica { get; set; }

        [Display(Name = "Monto para Prioridad Media ($)")]
        [Range(0, double.MaxValue, ErrorMessage = "El monto debe ser positivo")]
        public decimal? MontoParaPrioridadMedia { get; set; }

        [Display(Name = "Monto para Prioridad Alta ($)")]
        [Range(0, double.MaxValue, ErrorMessage = "El monto debe ser positivo")]
        public decimal? MontoParaPrioridadAlta { get; set; }

        [Display(Name = "Monto para Prioridad Crítica ($)")]
        [Range(0, double.MaxValue, ErrorMessage = "El monto debe ser positivo")]
        public decimal? MontoParaPrioridadCritica { get; set; }

        #endregion

        #region Automatización

        [Display(Name = "Proceso Automático Activo")]
        public bool ProcesoAutomaticoActivo { get; set; }

        [Display(Name = "Hora de Ejecución Diaria")]
        [DataType(DataType.Time)]
        public TimeSpan? HoraEjecucionDiaria { get; set; }

        [Display(Name = "Alertas Preventivas Activas")]
        public bool AlertasPreventivasActivas { get; set; }

        [Display(Name = "Días Antes para Alerta Preventiva")]
        [Range(1, 30, ErrorMessage = "Los días deben estar entre 1 y 30")]
        public int? DiasAntesAlertaPreventiva { get; set; }

        [Display(Name = "Cambiar Estado de Cuota Automáticamente")]
        public bool CambiarEstadoCuotaAuto { get; set; }

        [Display(Name = "Actualizar Mora Diariamente")]
        public bool ActualizarMoraAutomaticamente { get; set; }

        [Display(Name = "Última Ejecución")]
        public DateTime? UltimaEjecucion { get; set; }

        #endregion

        #region Notificaciones

        [Display(Name = "Notificaciones Activas")]
        public bool NotificacionesActivas { get; set; }

        [Display(Name = "WhatsApp Activo")]
        public bool WhatsAppActivo { get; set; }

        [Display(Name = "Email Activo")]
        public bool EmailActivo { get; set; }

        [Display(Name = "Canal Preferido")]
        public CanalNotificacion? CanalPreferido { get; set; }

        [Display(Name = "Notificar Próximo Vencimiento")]
        public bool NotificarProximoVencimiento { get; set; }

        [Display(Name = "Días Antes para Notificación")]
        [Range(1, 30, ErrorMessage = "Los días deben estar entre 1 y 30")]
        public int? DiasAntesNotificacionPreventiva { get; set; }

        [Display(Name = "Notificar Cuota Vencida")]
        public bool NotificarCuotaVencida { get; set; }

        [Display(Name = "Notificar Mora Acumulada")]
        public bool NotificarMoraAcumulada { get; set; }

        [Display(Name = "Frecuencia Recordatorio (días)")]
        [Range(1, 30, ErrorMessage = "La frecuencia debe estar entre 1 y 30 días")]
        public int? FrecuenciaRecordatorioMora { get; set; }

        [Display(Name = "Máximo Notificaciones Diarias")]
        [Range(1, 10, ErrorMessage = "El máximo debe estar entre 1 y 10")]
        public int? MaximoNotificacionesDiarias { get; set; }

        [Display(Name = "Máximo Notificaciones por Cuota")]
        [Range(1, 20, ErrorMessage = "El máximo debe estar entre 1 y 20")]
        public int? MaximoNotificacionesPorCuota { get; set; }

        [Display(Name = "Hora Inicio Envío")]
        [DataType(DataType.Time)]
        public TimeSpan? HoraInicioEnvio { get; set; }

        [Display(Name = "Hora Fin Envío")]
        [DataType(DataType.Time)]
        public TimeSpan? HoraFinEnvio { get; set; }

        [Display(Name = "Enviar en Fin de Semana")]
        public bool EnviarFinDeSemana { get; set; }

        #endregion

        #region Gestión de Cobranzas

        [Display(Name = "Días Máximos sin Gestión")]
        [Range(1, 90, ErrorMessage = "Los días deben estar entre 1 y 90")]
        public int? DiasMaximosSinGestion { get; set; }

        [Display(Name = "Días para Cumplir Promesa")]
        [Range(1, 30, ErrorMessage = "Los días deben estar entre 1 y 30")]
        public int? DiasParaCumplirPromesa { get; set; }

        [Display(Name = "Máximo Cuotas en Acuerdo")]
        [Range(1, 60, ErrorMessage = "El máximo debe estar entre 1 y 60 cuotas")]
        public int? MaximoCuotasAcuerdo { get; set; }

        [Display(Name = "Porcentaje Mínimo Entrega (%)")]
        [Range(0, 100, ErrorMessage = "El porcentaje debe estar entre 0 y 100")]
        public decimal? PorcentajeMinimoEntrega { get; set; }

        [Display(Name = "Permitir Condonación de Mora")]
        public bool PermitirCondonacionMora { get; set; }

        [Display(Name = "Porcentaje Máximo Condonación (%)")]
        [Range(0, 100, ErrorMessage = "El porcentaje debe estar entre 0 y 100")]
        public decimal? PorcentajeMaximoCondonacion { get; set; }

        #endregion

        #region Tramos de Cobranza

        [Display(Name = "Tramos de Cobranza Activos")]
        public bool TramosCobranzaActivos { get; set; }

        // Tramo 1: Preventivo
        [Display(Name = "Días Inicio Tramo 1")]
        public int? Tramo1DiasInicio { get; set; }

        [Display(Name = "Días Fin Tramo 1")]
        public int? Tramo1DiasFin { get; set; }

        [Display(Name = "Recordatorio Tramo 1 (días)")]
        public int? Tramo1Recordatorio { get; set; }

        [Display(Name = "Llamada Tramo 1 Activa")]
        public bool Tramo1LlamadaActiva { get; set; }

        // Tramo 2: Temprano
        [Display(Name = "Días Inicio Tramo 2")]
        public int? Tramo2DiasInicio { get; set; }

        [Display(Name = "Días Fin Tramo 2")]
        public int? Tramo2DiasFin { get; set; }

        [Display(Name = "Recordatorio Tramo 2 (días)")]
        public int? Tramo2Recordatorio { get; set; }

        [Display(Name = "Llamada Tramo 2 Activa")]
        public bool Tramo2LlamadaActiva { get; set; }

        // Tramo 3: Gestión
        [Display(Name = "Días Inicio Tramo 3")]
        public int? Tramo3DiasInicio { get; set; }

        [Display(Name = "Días Fin Tramo 3")]
        public int? Tramo3DiasFin { get; set; }

        [Display(Name = "Recordatorio Tramo 3 (días)")]
        public int? Tramo3Recordatorio { get; set; }

        [Display(Name = "Llamada Tramo 3 Activa")]
        public bool Tramo3LlamadaActiva { get; set; }

        // Tramo 4: Crítico
        [Display(Name = "Días Inicio Tramo 4")]
        public int? Tramo4DiasInicio { get; set; }

        [Display(Name = "Escalamiento Tramo 4 Activo")]
        public bool Tramo4EscalamientoActivo { get; set; }

        [Display(Name = "Bloqueo Tramo 4 Activo")]
        public bool Tramo4BloqueoActivo { get; set; }

        #endregion
    }
}
