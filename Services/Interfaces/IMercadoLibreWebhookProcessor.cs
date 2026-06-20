namespace TheBuryProject.Services.Interfaces
{
    /// <summary>
    /// Procesa en background los MercadoLibreWebhookEvents pendientes (Fase J).
    /// El endpoint del webhook solo guarda crudo y responde 200; acá se hace el
    /// trabajo real, idempotente por (topic, resource) y con reintentos acotados.
    /// </summary>
    public interface IMercadoLibreWebhookProcessor
    {
        /// <summary>
        /// Procesa hasta <paramref name="max"/> eventos pendientes.
        /// Devuelve cuántos quedaron marcados como procesados.
        /// </summary>
        Task<int> ProcesarPendientesAsync(int max = 50, CancellationToken ct = default);
    }
}
