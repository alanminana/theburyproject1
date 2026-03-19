using TheBuryProject.Models.Enums;

namespace TheBuryProject.Services.Models
{
    /// <summary>
    /// Define un tramo de cobranza con las acciones automáticas a ejecutar.
    /// Los tramos son rangos de días de atraso con acciones específicas.
    /// </summary>
    public sealed class TramoCobranza
    {
        /// <summary>
        /// Nombre descriptivo del tramo
        /// </summary>
        public string Nombre { get; init; } = string.Empty;

        /// <summary>
        /// Día de atraso desde el cual aplica este tramo (inclusive)
        /// </summary>
        public int DiasDesde { get; init; }

        /// <summary>
        /// Día de atraso hasta el cual aplica este tramo (inclusive, null = sin límite)
        /// </summary>
        public int? DiasHasta { get; init; }

        /// <summary>
        /// Prioridad asignada a alertas en este tramo
        /// </summary>
        public PrioridadAlerta Prioridad { get; init; }

        /// <summary>
        /// Acciones automáticas a ejecutar en este tramo
        /// </summary>
        public IReadOnlyList<AccionAutomatica> Acciones { get; init; } = Array.Empty<AccionAutomatica>();

        /// <summary>
        /// Verifica si un número de días de atraso cae en este tramo
        /// </summary>
        public bool Aplica(int diasAtraso)
        {
            if (diasAtraso < DiasDesde)
                return false;

            if (DiasHasta.HasValue && diasAtraso > DiasHasta.Value)
                return false;

            return true;
        }
    }

    /// <summary>
    /// Acción automática a ejecutar en un tramo
    /// </summary>
    public sealed class AccionAutomatica
    {
        /// <summary>
        /// Tipo de acción a ejecutar
        /// </summary>
        public TipoAccionAutomatica Tipo { get; init; }

        /// <summary>
        /// Día específico del tramo en que se ejecuta (null = al entrar al tramo)
        /// </summary>
        public int? DiaEjecucion { get; init; }

        /// <summary>
        /// Plantilla de notificación a usar (si aplica)
        /// </summary>
        public int? PlantillaId { get; init; }

        /// <summary>
        /// Canal de notificación preferido (si aplica)
        /// </summary>
        public CanalNotificacion? Canal { get; init; }

        /// <summary>
        /// Descripción de la acción
        /// </summary>
        public string? Descripcion { get; init; }
    }

    /// <summary>
    /// Tipos de acciones automáticas en cobranza
    /// </summary>
    public enum TipoAccionAutomatica
    {
        /// <summary>
        /// Generar alerta de cobranza
        /// </summary>
        GenerarAlerta = 1,

        /// <summary>
        /// Enviar notificación al cliente
        /// </summary>
        EnviarNotificacion = 2,

        /// <summary>
        /// Cambiar estado de la cuota
        /// </summary>
        CambiarEstadoCuota = 3,

        /// <summary>
        /// Escalar prioridad de la alerta
        /// </summary>
        EscalarPrioridad = 4,

        /// <summary>
        /// Bloquear al cliente
        /// </summary>
        BloquearCliente = 5,

        /// <summary>
        /// Registrar nota automática
        /// </summary>
        RegistrarNota = 6,

        /// <summary>
        /// Asignar a gestor
        /// </summary>
        AsignarGestor = 7,

        /// <summary>
        /// Marcar promesa como incumplida
        /// </summary>
        MarcarPromesaIncumplida = 8
    }

    /// <summary>
    /// Resultado del procesamiento automático por tramos
    /// </summary>
    public sealed class ResultadoProcesamientoTramos
    {
        public DateTime FechaProcesamiento { get; init; }
        public int AlertasProcesadas { get; init; }
        public int AccionesEjecutadas { get; init; }
        public int AlertasEscaladas { get; init; }
        public int NotificacionesProgramadas { get; init; }
        public int PromesasVencidas { get; init; }
        public int ClientesBloqueados { get; init; }
        public IReadOnlyList<AccionEjecutada> Detalle { get; init; } = Array.Empty<AccionEjecutada>();
        public bool Exitoso { get; init; } = true;
        public string? Error { get; init; }

        public static ResultadoProcesamientoTramos Vacio(DateTime fecha) => new()
        {
            FechaProcesamiento = fecha,
            AlertasProcesadas = 0,
            AccionesEjecutadas = 0,
            Exitoso = true
        };

        public static ResultadoProcesamientoTramos ConError(DateTime fecha, string error) => new()
        {
            FechaProcesamiento = fecha,
            Exitoso = false,
            Error = error
        };
    }

    /// <summary>
    /// Detalle de una acción ejecutada
    /// </summary>
    public sealed class AccionEjecutada
    {
        public int AlertaId { get; set; }
        public int ClienteId { get; set; }
        public TipoAccionAutomatica Tipo { get; set; }
        public string Descripcion { get; set; } = string.Empty;
        public bool Exitoso { get; set; }
        public string? Error { get; set; }
    }

    /// <summary>
    /// DTO para registrar una promesa de pago
    /// </summary>
    public sealed class PromesaPagoDto
    {
        /// <summary>
        /// Id de la alerta de cobranza
        /// </summary>
        public int AlertaCobranzaId { get; init; }

        /// <summary>
        /// Fecha en que el cliente promete pagar
        /// </summary>
        public DateTime FechaPromesa { get; init; }

        /// <summary>
        /// Monto que promete pagar
        /// </summary>
        public decimal MontoPromesa { get; init; }

        /// <summary>
        /// Tipo de contacto en que se registró la promesa
        /// </summary>
        public TipoContacto TipoContacto { get; init; }

        /// <summary>
        /// Observaciones del gestor
        /// </summary>
        public string? Observaciones { get; init; }

        /// <summary>
        /// Teléfono/canal usado para el contacto
        /// </summary>
        public string? MedioContacto { get; init; }
    }

    /// <summary>
    /// Resultado del registro de una promesa de pago
    /// </summary>
    public sealed class ResultadoPromesaPago
    {
        public int AlertaId { get; init; }
        public int HistorialContactoId { get; init; }
        public DateTime FechaPromesa { get; init; }
        public decimal MontoPromesa { get; init; }
        public DateTime FechaLimite { get; init; }
        public bool Exitoso { get; init; }
        public string? Error { get; init; }

        public static ResultadoPromesaPago ConError(string error) => new()
        {
            Exitoso = false,
            Error = error
        };
    }

    /// <summary>
    /// DTO para verificar promesas vencidas
    /// </summary>
    public sealed class PromesaVencidaDto
    {
        public int AlertaId { get; init; }
        public int ClienteId { get; init; }
        public string ClienteNombre { get; init; } = string.Empty;
        public DateTime FechaPromesa { get; init; }
        public decimal MontoPromesa { get; init; }
        public int DiasVencida { get; init; }
        public decimal SaldoActual { get; init; }
    }
}
