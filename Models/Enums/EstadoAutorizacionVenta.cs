namespace TheBuryProject.Models.Enums
{
    /// <summary>
    /// Estado de autorización de una venta.
    /// Los valores NoRequerida (4) y Pendiente (5) están deprecados y no deben usarse.
    /// </summary>
    public enum EstadoAutorizacionVenta
    {
        /// <summary>
        /// La venta no requiere autorización
        /// </summary>
        NoRequiere = 0,

        /// <summary>
        /// La venta está pendiente de autorización
        /// </summary>
        PendienteAutorizacion = 1,

        /// <summary>
        /// La venta fue autorizada
        /// </summary>
        Autorizada = 2,

        /// <summary>
        /// La venta fue rechazada
        /// </summary>
        Rechazada = 3

        // DEPRECADO: NoRequerida = 4 (usar NoRequiere)
        // DEPRECADO: Pendiente = 5 (usar PendienteAutorizacion)
    }
}