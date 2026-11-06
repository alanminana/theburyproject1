namespace TheBuryProject.Services
{
    /// <summary>
    /// H-22: definición ÚNICA de las categorías de deuda vencida, usada por la bandeja de cobranza, la ficha del cliente
    /// y los reportes:
    ///   * Deuda vencida: saldo de las cuotas cuyo vencimiento es anterior a hoy.
    ///   * En gracia: vencida, pero dentro de los días de gracia (vencimiento entre hoy - DiasGracia y ayer, inclusive).
    ///     Todavía no es mora: no genera alerta de cobranza ni cuenta para la bandeja.
    ///   * En mora: vencida y fuera de la gracia (vencimiento anterior a hoy - DiasGracia). Es lo que muestra la bandeja.
    /// Deuda vencida = en gracia + en mora, de modo que las superficies concilian entre sí.
    /// </summary>
    public static class MoraClasificacion
    {
        /// <summary>Fecha de corte: una cuota con vencimiento ANTERIOR a este día está en mora (misma regla que ProcesarMora).</summary>
        public static DateTime FechaLimiteMora(DateTime hoy, int? diasGracia) => hoy.AddDays(-Math.Max(0, diasGracia ?? 0));

        public static bool EstaEnMora(DateTime vencimiento, DateTime hoy, int? diasGracia) =>
            vencimiento < FechaLimiteMora(hoy, diasGracia);

        public static bool EstaVencida(DateTime vencimiento, DateTime hoy) => vencimiento < hoy;

        public static bool EstaEnGracia(DateTime vencimiento, DateTime hoy, int? diasGracia) =>
            EstaVencida(vencimiento, hoy) && !EstaEnMora(vencimiento, hoy, diasGracia);

        /// <summary>Separa los saldos de cuotas ya vencidas en (en gracia, en mora).</summary>
        public static (decimal EnGracia, decimal EnMora) Separar(
            IEnumerable<(DateTime Vencimiento, decimal Saldo)> cuotasVencidas, DateTime hoy, int? diasGracia)
        {
            decimal gracia = 0m, mora = 0m;
            foreach (var (venc, saldo) in cuotasVencidas)
            {
                if (EstaEnMora(venc, hoy, diasGracia)) mora += saldo;
                else if (EstaEnGracia(venc, hoy, diasGracia)) gracia += saldo;
            }
            return (gracia, mora);
        }
    }
}
