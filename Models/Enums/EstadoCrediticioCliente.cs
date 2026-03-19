namespace TheBuryProject.Models.Enums
{
    /// <summary>
    /// Estado de aptitud crediticia del cliente (semáforo).
    /// Determina si el cliente puede solicitar créditos.
    /// </summary>
    public enum EstadoCrediticioCliente
    {
        /// <summary>
        /// Cliente apto para crédito: documentación completa, cupo disponible, sin mora bloqueante
        /// </summary>
        Apto = 1,

        /// <summary>
        /// Cliente no apto: falta documentación, cupo agotado, o mora crítica
        /// </summary>
        NoApto = 2,

        /// <summary>
        /// Requiere autorización de supervisor: cliente con mora o excepción necesaria
        /// </summary>
        RequiereAutorizacion = 3,

        /// <summary>
        /// No evaluado: cliente nuevo sin evaluación de aptitud
        /// </summary>
        NoEvaluado = 0
    }
}
