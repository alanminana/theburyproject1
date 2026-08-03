using TheBuryProject.Models.Enums;

namespace TheBuryProject.Services
{
    /// <summary>
    /// PUN-ML7: autoridad ÚNICA para decidir el estado de una <see cref="Models.Entities.Cuota"/> y
    /// para derivar, sin persistir, si está vencida en una fecha comercial dada. Pura, determinista,
    /// sin acceso a DB ni a <see cref="System.DateTime.Now"/> — toda fecha "hoy" la resuelve el
    /// caller vía <see cref="IRelojComercial"/> antes de invocar este tipo.
    ///
    /// Corrección posterior a la primera entrega de PUN-ML7: existía un segundo resolver privado en
    /// <see cref="CreditoService"/> (3 argumentos, sin noción de vencimiento ni de estado terminal)
    /// usado dentro de un cobro. Se eliminó — <see cref="Resolver"/> es ahora el único punto que
    /// decide <see cref="EstadoCuota"/> en todo el sistema (pago individual, pago múltiple, primera
    /// cuota, adelanto, aplicar/anular punitorio). La eliminación no fue cosmética: el resolver de 3
    /// argumentos, al no ver la fecha, devolvía <c>Pendiente</c> para un pago imputado íntegramente a
    /// punitorio (MontoPagado queda en 0) sobre una cuota ya vencida — ocultando mora real detrás de
    /// un estado que sugiere "todavía no vence". Ver <c>CreditoServiceEstadoCuotaTests</c> para la
    /// prueba estructural que impide reintroducir un resolver paralelo.
    /// </summary>
    public static class EstadoCuotaResolver
    {
        /// <summary>
        /// Estados que nunca se reabren por el paso del tiempo ni por una sincronización automática
        /// (Cancelada: cancelación de venta/crédito). Pagada NO es terminal en este sentido: una
        /// aplicación de punitorio posterior a una cuota saldada en capital puede "reabrirla" a
        /// Parcial — comportamiento intencional (contrato PUN-ML7: "capital cero + punitorio
        /// pendiente no está pagada"), documentado en <see cref="PunitorioService.AplicarAsync"/>.
        /// </summary>
        private static bool EsTerminal(EstadoCuota estado) => estado == EstadoCuota.Cancelada;

        /// <summary>
        /// Resuelve el estado persistido de una cuota para un instante dado. Único punto que combina
        /// saldo de capital, punitorio aplicado pendiente y frontera de vencimiento comercial.
        /// </summary>
        /// <param name="estadoActual">Estado persistido antes de esta resolución (protege terminales).</param>
        /// <param name="fechaVencimiento">Vencimiento de la cuota (se compara solo la parte de fecha).</param>
        /// <param name="fechaComercial">"Hoy" según <see cref="IRelojComercial.HoyComercial"/>.</param>
        /// <param name="montoPagado">Capital+interés efectivamente aplicado a la cuota (nunca incluye punitorio).</param>
        /// <param name="montoTotal">Valor original de la cuota (capital+interés, sin punitorio).</param>
        /// <param name="punitorioAplicadoPendiente">Suma de aplicaciones activas no cobradas (0 si no hay).</param>
        public static EstadoCuota Resolver(
            EstadoCuota estadoActual,
            DateTime fechaVencimiento,
            DateOnly fechaComercial,
            decimal montoPagado,
            decimal montoTotal,
            decimal punitorioAplicadoPendiente)
        {
            if (EsTerminal(estadoActual))
                return estadoActual;

            if (montoPagado >= montoTotal && punitorioAplicadoPendiente <= 0m)
                return EstadoCuota.Pagada;

            if (montoPagado > 0m)
                return EstadoCuota.Parcial;

            return EsVencidaPorFecha(fechaVencimiento, fechaComercial)
                ? EstadoCuota.Vencida
                : EstadoCuota.Pendiente;
        }

        /// <summary>
        /// Frontera de vencimiento comercial: el día del vencimiento NO está vencida; el día
        /// siguiente sí. Único lugar que expresa esta comparación — reutilizado por
        /// <see cref="Resolver"/> y por <see cref="EstaVencidaDerivado"/>, y es la regla que debe
        /// coincidir con la condición SQL de <see cref="CreditoService.ActualizarEstadoCuotasAsync"/>
        /// (ver comentario ahí: para una cuota Pendiente, "FechaVencimiento &lt; InicioDiaComercial"
        /// es la misma frontera expresada en SQL sobre columnas <see cref="DateTime"/> a medianoche).
        /// </summary>
        public static bool EsVencidaPorFecha(DateTime fechaVencimiento, DateOnly fechaComercial) =>
            fechaComercial > DateOnly.FromDateTime(fechaVencimiento);

        /// <summary>
        /// "¿Está en mora hoy?", derivado sin persistir. A diferencia de <see cref="Resolver"/> (que
        /// nunca marca Vencida a una cuota con pago parcial — ver contrato PUN-ML7 y
        /// <c>EstadoCuotaResolverTests</c>), este helper SÍ considera en mora a una cuota
        /// Parcial vencida: es la lectura que ya usan <c>ClienteScoringCalculator</c>,
        /// <c>MoraService</c> y <c>ClienteAptitudService</c> para "atraso"/"mora" (evidencia
        /// preexistente, PUN-ML7 unifica sobre ella en vez de inventar una tercera regla). Pagada y
        /// Cancelada nunca están en mora.
        /// </summary>
        public static bool EstaVencidaDerivado(EstadoCuota estado, DateTime fechaVencimiento, DateOnly fechaComercial) =>
            estado != EstadoCuota.Pagada &&
            estado != EstadoCuota.Cancelada &&
            EsVencidaPorFecha(fechaVencimiento, fechaComercial);

        /// <summary>
        /// "¿Está en mora de CAPITAL hoy?" — más estricto que <see cref="EstaVencidaDerivado"/>: además
        /// de vencida y no terminal, exige saldo de CAPITAL pendiente (<c>montoPagado &lt; montoTotal</c>).
        /// Distingue la cola de cobranza de capital (<c>MoraService</c>) de una cuota que ya saldó
        /// capital pero quedó <see cref="EstadoCuota.Parcial"/> únicamente por un punitorio aplicado
        /// pendiente (ver <see cref="Resolver"/>) — ese seguimiento es responsabilidad de
        /// <c>PunitorioService</c>, no de la mora de capital.
        /// PUN-ML7 (corrección de auditoría, lote 2): antes cada lectura de mora en
        /// <c>MoraService</c> reimplementaba esta condición inline, algunas todavía con
        /// <c>Estado == EstadoCuota.Pendiente</c> (perdía Vencida/Parcial con saldo) y ninguna
        /// excluía capital saldado con punitorio pendiente. Predicado único, reutilizado en vez de
        /// duplicado en cada LINQ.
        /// </summary>
        public static bool EstaEnMoraCapitalDerivado(
            EstadoCuota estado,
            decimal montoPagado,
            decimal montoTotal,
            DateTime fechaVencimiento,
            DateOnly fechaComercial) =>
            estado != EstadoCuota.Pagada &&
            estado != EstadoCuota.Cancelada &&
            montoPagado < montoTotal &&
            EsVencidaPorFecha(fechaVencimiento, fechaComercial);

        /// <summary>Días de atraso derivados; 0 si <see cref="EstaVencidaDerivado"/> es falso.</summary>
        public static int DiasAtrasoDerivado(EstadoCuota estado, DateTime fechaVencimiento, DateOnly fechaComercial) =>
            EstaVencidaDerivado(estado, fechaVencimiento, fechaComercial)
                ? fechaComercial.DayNumber - DateOnly.FromDateTime(fechaVencimiento).DayNumber
                : 0;
    }
}
