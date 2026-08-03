namespace TheBuryProject.Models.Enums
{
    /// <summary>
    /// Cómo se originó una fila del ledger de pagos por cuota (<see cref="Entities.PagoCuota"/>).
    /// </summary>
    public enum OrigenPagoCuota
    {
        /// <summary>
        /// Escrita por el flujo de cobro vigente en el momento del pago (no reconstruida).
        /// </summary>
        RegistradoPorSistema = 1,

        /// <summary>
        /// Reconstruida desde un <see cref="Entities.MovimientoCaja"/> histórico inequívoco
        /// (cuota, importe y fecha identificables), con composición capital/punitorio confiable.
        /// </summary>
        BackfillMovimientoCaja = 2,

        /// <summary>
        /// Reconstruida desde un <see cref="Entities.MovimientoCaja"/> histórico inequívoco en
        /// cuota, importe y fecha, pero sin poder distinguir cuánto correspondió a capital/cuota
        /// y cuánto a punitorio (ver <see cref="Entities.PagoCuota.HistorialCompleto"/>).
        /// </summary>
        BackfillIncompleto = 3,

        /// <summary>
        /// Fila generada por una reversión de un pago anterior. Sin productor todavía: la
        /// reversión de cobros no existe como funcionalidad (PUN-ML1, diagnóstico §2).
        /// </summary>
        Reversion = 4
    }

    /// <summary>
    /// Estado de una fila del ledger de pagos por cuota. Las filas nunca se borran físicamente.
    /// </summary>
    public enum EstadoPagoCuota
    {
        /// <summary>
        /// Pago vigente, cuenta para los cálculos de saldo/tramos.
        /// </summary>
        Aplicado = 1,

        /// <summary>
        /// Anulado por un usuario autorizado. Se conserva con motivo y fecha de anulación.
        /// </summary>
        Anulado = 2,

        /// <summary>
        /// Revertido por un pago de signo contrario encadenado (<see cref="Entities.PagoCuota.PagoCuotaOrigenId"/>).
        /// </summary>
        Revertido = 3
    }
}
