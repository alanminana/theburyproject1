namespace TheBuryProject.Models.Enums
{
    /// <summary>
    /// Estado de una cuota de crédito
    /// </summary>
    public enum EstadoCuota
    {
        /// <summary>
        /// Cuota pendiente de pago
        /// </summary>
        Pendiente = 0,

        /// <summary>
        /// Cuota pagada completamente
        /// </summary>
        Pagada = 1,

        /// <summary>
        /// Cuota vencida sin pagar
        /// </summary>
        Vencida = 2,

        /// <summary>
        /// Cuota pagada parcialmente
        /// </summary>
        Parcial = 3,

        /// <summary>
        /// Cuota cancelada (por cancelación del crédito)
        /// </summary>
        Cancelada = 4
    }
}