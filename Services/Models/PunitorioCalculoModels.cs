using TheBuryProject.Models.Enums;

namespace TheBuryProject.Services.Models
{
    /// <summary>
    /// Resultado global de <see cref="Interfaces.IPunitorioCalculator.Calcular"/> (PUN-ML4). Distingue
    /// explícitamente por qué no hay un total autoritativo cuando corresponde — nunca se infiere un
    /// total parcial como si fuera completo.
    /// </summary>
    public enum EstadoResultadoPunitorio
    {
        /// <summary>Se determinó un importe autoritativo (posiblemente 0: p. ej. configuración activa al 0%).</summary>
        Calculado = 0,

        /// <summary>Ninguna <see cref="ConfiguracionPunitorioEntrada"/> aplica a todo o parte del período: no hay total autoritativo.</summary>
        SinConfiguracion = 1,

        /// <summary>La única configuración vigente en todo el período está desactivada.</summary>
        ConfiguracionInactiva = 2,

        /// <summary>Un pago no reconstruible impide fijar el saldo más allá de su fecha: no hay total autoritativo.</summary>
        HistorialIncompleto = 3,

        /// <summary>El saldo ya estaba en cero al vencimiento: nunca hubo deuda sobre la que devengar.</summary>
        SinSaldo = 4,

        /// <summary>Los días transcurridos no superan la gracia de la única configuración vigente en todo el período.</summary>
        DentroDeGracia = 5,

        /// <summary>La entrada es inconsistente o no soportada; ver <see cref="PunitorioCalculoResultado.Motivo"/>.</summary>
        EntradaInvalida = 6
    }

    /// <summary>
    /// Por qué empieza o termina un <see cref="SegmentoPunitorio"/>. El mismo tipo sirve para
    /// <see cref="SegmentoPunitorio.MotivoDeInicio"/> y <see cref="SegmentoPunitorio.MotivoDeFin"/>: el fin
    /// de un segmento es, salvo el último, exactamente el mismo evento que el inicio del siguiente.
    /// </summary>
    public enum MotivoLimiteSegmento
    {
        /// <summary>El segmento arranca en la fecha de vencimiento de la cuota (siempre el primero).</summary>
        Vencimiento = 0,

        /// <summary>Un pago cambió el saldo base.</summary>
        Pago = 1,

        /// <summary>
        /// Cambió la configuración vigente. Cubre tanto un cambio de porcentaje/período como una
        /// activación/inactivación: ambas son, estructuralmente, una versión nueva con distinto
        /// <see cref="ConfiguracionPunitorioEntrada.Id"/> — no hay un motivo separado para eso; se audita
        /// cruzando <see cref="SegmentoPunitorio.ConfiguracionPunitorioId"/> contra el historial de
        /// configuraciones que recibió la entrada.
        /// </summary>
        NuevaConfiguracion = 2,

        /// <summary>Se alcanzó la fecha de cálculo solicitada (fin del último segmento, salvo corte previo).</summary>
        FechaCalculo = 3,

        /// <summary>El saldo llegó a cero: no hay más importe sobre el cual devengar.</summary>
        SaldoCancelado = 4,

        /// <summary>Un pago posterior no es reconstruible de forma confiable: no se generan más segmentos.</summary>
        HistorialIncompleto = 5
    }

    /// <summary>
    /// Un pago aplicado a la cuota, tal como lo expone <see cref="Models.Entities.PagoCuota"/> — el
    /// calculador no lee la entidad ni la base: recibe exactamente estos campos por cada fila relevante
    /// (incluidas las no efectivas: el filtro se aplica adentro, no antes de llamar).
    /// </summary>
    public sealed class PagoAplicadoPunitorioEntrada
    {
        public required DateOnly FechaPagoComercial { get; init; }

        /// <summary>
        /// Parte del pago aplicada a la cuota (nunca a punitorio). <c>null</c> cuando
        /// <see cref="HistorialCompleto"/> es <c>false</c> — nunca se infiere.
        /// </summary>
        public decimal? ImporteAplicadoCuota { get; init; }

        /// <summary>Solo <see cref="EstadoPagoCuota.Aplicado"/> reduce saldo. Anulado y Revertido se ignoran por completo.</summary>
        public required EstadoPagoCuota Estado { get; init; }

        public bool HistorialCompleto { get; init; } = true;
    }

    /// <summary>Una versión de <see cref="Models.Entities.ConfiguracionPunitorio"/> tal como la ve el calculador.</summary>
    public sealed class ConfiguracionPunitorioEntrada
    {
        public required int Id { get; init; }
        public required DateOnly VigenteDesde { get; init; }
        public required decimal Porcentaje { get; init; }
        public required int PeriodoDias { get; init; }
        public required int DiasGracia { get; init; }
        public bool ProrrateoDiario { get; init; } = true;
        public bool Activa { get; init; } = true;

        /// <summary>
        /// Sin efecto en el cálculo. La autoridad temporal es siempre <see cref="VigenteDesde"/>: este
        /// flag ya fue consumido por PUN-ML3 para autorizar la creación de la versión (permitir una
        /// vigencia pasada al crearla), no para reescribir el cálculo de días anteriores a
        /// <see cref="VigenteDesde"/> ni para sustituir versiones históricas previas. Se expone solo
        /// porque forma parte del registro versionado que recibe el calculador.
        /// </summary>
        public bool AplicacionRetroactiva { get; init; }
    }

    /// <summary>Entrada de <see cref="Interfaces.IPunitorioCalculator.Calcular"/>.</summary>
    public sealed class PunitorioCalculoEntrada
    {
        public required decimal MontoOriginalCuota { get; init; }
        public required DateOnly FechaVencimiento { get; init; }
        public required DateOnly FechaCalculo { get; init; }
        public IReadOnlyList<PagoAplicadoPunitorioEntrada> PagosAplicados { get; init; } = Array.Empty<PagoAplicadoPunitorioEntrada>();
        public IReadOnlyList<ConfiguracionPunitorioEntrada> Configuraciones { get; init; } = Array.Empty<ConfiguracionPunitorioEntrada>();
    }

    /// <summary>
    /// Un tramo de devengo continuo: mismo saldo base y misma configuración vigente durante
    /// [<see cref="Desde"/>, <see cref="Hasta"/>). Los campos de configuración e <see cref="ImporteExacto"/>
    /// quedan en <c>null</c> únicamente cuando el tramo es un hueco sin ninguna configuración aplicable
    /// (no se inventa una tasa). Cuando hay configuración, <see cref="ImporteExacto"/> puede ser 0 por
    /// gracia, por configuración inactiva o por 0% — comparar
    /// <see cref="PunitorioCalculoResultado.DiasTranscurridos"/> contra <see cref="DiasGracia"/> y cruzar
    /// <see cref="ConfiguracionPunitorioId"/> contra el historial recibido para distinguir cuál.
    /// </summary>
    public sealed class SegmentoPunitorio
    {
        public required DateOnly Desde { get; init; }
        public required DateOnly Hasta { get; init; }
        public required int Dias { get; init; }
        public required decimal SaldoBase { get; init; }
        public int? ConfiguracionPunitorioId { get; init; }
        public decimal? Porcentaje { get; init; }
        public int? PeriodoDias { get; init; }
        public int? DiasGracia { get; init; }
        public decimal? ImporteExacto { get; init; }
        public required MotivoLimiteSegmento MotivoDeInicio { get; init; }
        public required MotivoLimiteSegmento MotivoDeFin { get; init; }
    }

    /// <summary>Resultado de <see cref="Interfaces.IPunitorioCalculator.Calcular"/>.</summary>
    public sealed class PunitorioCalculoResultado
    {
        public required EstadoResultadoPunitorio EstadoResultado { get; init; }
        public required decimal SaldoInicial { get; init; }
        public required decimal SaldoFinal { get; init; }
        public required DateOnly FechaVencimiento { get; init; }
        public required DateOnly FechaCalculo { get; init; }
        public required int DiasTranscurridos { get; init; }

        /// <summary>Suma exacta sin redondear de los segmentos calculables. <c>null</c> cuando no hay total autoritativo (ver <see cref="EstadoResultado"/>).</summary>
        public decimal? PunitorioExacto { get; init; }

        /// <summary><see cref="PunitorioExacto"/> redondeado una sola vez a 2 decimales (AwayFromZero). <c>null</c> en las mismas condiciones.</summary>
        public decimal? PunitorioRedondeado { get; init; }

        public IReadOnlyList<SegmentoPunitorio> Segmentos { get; init; } = Array.Empty<SegmentoPunitorio>();

        /// <summary>Explicación legible del estado, en particular por qué no hay total cuando corresponde.</summary>
        public string? Motivo { get; init; }
    }
}
