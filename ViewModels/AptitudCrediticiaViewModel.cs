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
        public DateTime FechaEvaluacion { get; set; } = DateTime.UtcNow;

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
        public AptitudBcraDetalle Bcra { get; set; } = new();
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
    /// Detalle de evaluación de mora.
    ///
    /// PUN-ML10-C: separa explícitamente dos deudas de naturaleza distinta que antes se mezclaban en
    /// un único monto — mora de CAPITAL (cuota vencida con saldo de capital pendiente) y punitorio
    /// APLICADO PENDIENTE (fila <c>PunitorioAplicado</c> vigente, PUN-ML5/6, todavía no cobrada). Un
    /// punitorio calculado pero nunca aplicado, o aplicado y luego pagado/anulado, no es deuda y no
    /// aparece en ninguno de los dos bloques.
    ///
    /// Las propiedades legacy (<see cref="TieneMora"/>, <see cref="DiasMaximoMora"/>,
    /// <see cref="MontoTotalMora"/>, <see cref="CuotasVencidas"/>) se conservan porque
    /// <c>Views/Cliente/Details_tw.cshtml</c> y <c>ValidacionVentaService</c> ya las consumen, pero su
    /// semántica quedó corregida: representan EXCLUSIVAMENTE mora de capital, nunca una mezcla con
    /// punitorio (antes sumaban <c>Cuota.MontoPunitorio</c>, campo legacy congelado desde PUN-ML5/6) —
    /// son ahora un alias 1:1 de <see cref="MontoMoraCapital"/>/<see cref="CuotasConMoraCapital"/>. El
    /// punitorio aplicado pendiente vive únicamente en <see cref="MontoPunitorioAplicadoPendiente"/>/
    /// <see cref="CuotasConPunitorioAplicadoPendiente"/>/<see cref="TienePunitorioAplicadoPendiente"/>.
    /// </summary>
    public class AptitudMoraDetalle
    {
        public bool Evaluada { get; set; }

        /// <summary>Legacy: alias de "hay mora de CAPITAL" (nunca incluye punitorio). Ver <see cref="CuotasConMoraCapital"/>.</summary>
        public bool TieneMora { get; set; }

        /// <summary>Legacy: días de atraso máximos entre las cuotas con mora de CAPITAL. 0 si no hay mora de capital.</summary>
        public int DiasMaximoMora { get; set; }

        /// <summary>Legacy: alias de <see cref="MontoMoraCapital"/> — nunca incluye punitorio aplicado pendiente.</summary>
        public decimal MontoTotalMora { get; set; }

        /// <summary>Legacy: alias de <see cref="CuotasConMoraCapital"/>.</summary>
        public int CuotasVencidas { get; set; }

        /// <summary>
        /// Clasificación de la mora de CAPITAL únicamente, según los umbrales configurados (mismos de
        /// siempre). El punitorio aplicado pendiente NO se mezcla acá — su propia contribución a
        /// RequiereAutorizacion (regla 4: nunca NoApto por sí solo) se resuelve por separado en
        /// <see cref="ClienteAptitudService.DeterminarEstadoFinal"/>, usando
        /// <see cref="TienePunitorioAplicadoPendiente"/> con motivo/detalle propios.
        /// </summary>
        public bool RequiereAutorizacion { get; set; }

        /// <summary>Bloqueo (NoApto). Sólo lo decide la mora de CAPITAL — el punitorio aplicado pendiente nunca es bloqueante por sí solo (PUN-ML10-C regla 4).</summary>
        public bool EsBloqueante { get; set; }
        public string Mensaje { get; set; } = string.Empty;

        /// <summary>PUN-ML10-C: suma de (MontoTotal - MontoPagado) de las cuotas con mora de CAPITAL (saldo de capital pendiente, vencidas, no terminales). Nunca incluye punitorio.</summary>
        public decimal MontoMoraCapital { get; set; }

        /// <summary>PUN-ML10-C: cantidad de cuotas con mora de CAPITAL (predicado canónico <see cref="EstadoCuotaResolver.EstaEnMoraCapitalDerivado"/>).</summary>
        public int CuotasConMoraCapital { get; set; }

        /// <summary>PUN-ML10-C: suma del punitorio APLICADO pendiente de cobro (PUN-ML5/6) de todas las cuotas del cliente. Nunca punitorio calculado-no-aplicado, pagado ni anulado.</summary>
        public decimal MontoPunitorioAplicadoPendiente { get; set; }

        /// <summary>PUN-ML10-C: cantidad de cuotas con punitorio aplicado pendiente &gt; 0.</summary>
        public int CuotasConPunitorioAplicadoPendiente { get; set; }

        /// <summary>PUN-ML10-C: true si <see cref="MontoPunitorioAplicadoPendiente"/> &gt; 0. Deuda separada de la mora de capital — nunca produce NoApto por sí sola.</summary>
        public bool TienePunitorioAplicadoPendiente { get; set; }

        /// <summary>Umbral de días configurado para requerir autorización (ConfiguracionCredito.DiasParaRequerirAutorizacion).</summary>
        public int? DiasParaRequerirAutorizacion { get; set; }

        /// <summary>Umbral de días configurado para pasar a NoApto (ConfiguracionCredito.DiasParaNoApto).</summary>
        public int? DiasParaNoApto { get; set; }

        /// <summary>
        /// Explica en texto el estado de los días de mora frente a los umbrales configurados.
        /// Null si no hay mora activa (para no agregar ruido visual).
        /// </summary>
        public string? MensajeUmbralDias
        {
            get
            {
                if (!TieneMora)
                {
                    return null;
                }

                if (DiasParaNoApto.HasValue && DiasMaximoMora >= DiasParaNoApto.Value)
                {
                    return $"Mora activa: {DiasMaximoMora} días. Supera el umbral de NoApto de {DiasParaNoApto.Value} días.";
                }

                if (DiasParaRequerirAutorizacion.HasValue && DiasMaximoMora >= DiasParaRequerirAutorizacion.Value)
                {
                    return $"Mora activa: {DiasMaximoMora} días. Supera el umbral de autorización de {DiasParaRequerirAutorizacion.Value} días.";
                }

                if (DiasParaRequerirAutorizacion.HasValue)
                {
                    return $"Mora activa: {DiasMaximoMora} días. Todavía no supera el umbral de autorización de {DiasParaRequerirAutorizacion.Value} días.";
                }

                return $"Mora activa: {DiasMaximoMora} días.";
            }
        }
    }

    /// <summary>
    /// Detalle de evaluación de situación BCRA (Central de Deudores).
    /// Sólo actúa como bloqueo/revisión cuando la consulta fue exitosa y hay situación informada.
    /// Situación 0/1 = normal; 2 = requiere revisión; >= 3 = no apto.
    /// </summary>
    public class AptitudBcraDetalle
    {
        /// <summary>True sólo cuando hay consulta válida y situación informada.</summary>
        public bool Evaluada { get; set; }
        public bool ConsultaOk { get; set; }
        public int? Situacion { get; set; }
        public string? Descripcion { get; set; }
        public bool EsBloqueante { get; set; }        // situación >= 3
        public bool RequiereAutorizacion { get; set; } // situación == 2
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
