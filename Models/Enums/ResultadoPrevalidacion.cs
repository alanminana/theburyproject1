namespace TheBuryProject.Models.Enums
{
    /// <summary>
    /// Resultado de la prevalidación crediticia para una venta.
    /// Determina si la venta puede proceder, requiere autorización, o no es viable.
    /// </summary>
    public enum ResultadoPrevalidacion
    {
        /// <summary>
        /// Cliente apto para crédito. La venta puede proceder sin restricciones.
        /// </summary>
        Aprobable = 0,

        /// <summary>
        /// Cliente puede recibir crédito pero requiere autorización de un supervisor.
        /// Razones: mora no bloqueante, excede cupo con margen, etc.
        /// </summary>
        RequiereAutorizacion = 1,

        /// <summary>
        /// La venta no puede proceder. El cliente no cumple requisitos mínimos.
        /// Razones: sin límite asignado, documentación faltante, mora bloqueante, etc.
        /// </summary>
        NoViable = 2
    }
}
