using TheBuryProject.ViewModels;

namespace TheBuryProject.Services.Interfaces
{
    /// <summary>
    /// Servicio centralizado para gestión de mora, alertas y cobranzas
    /// </summary>
    public interface IMoraAlertasService
    {
        // Procesamiento de mora
        Task ProcesarMoraAsync();
        Task ActualizarMorasCuotasAsync();

        // Generación de alertas
        Task GenerarAlertasCobranzaAsync();
        Task<List<AlertaCobranzaViewModel>> ObtenerAlertasActivasAsync();
        Task<List<AlertaCobranzaViewModel>> ObtenerAlertasPorClienteAsync(int clienteId);

        // Resolución de alertas
        Task<bool> ResolverAlertaAsync(int alertaId, string? observaciones = null, byte[]? rowVersion = null);
        Task<bool> MarcarAlertaComoLeidaAsync(int alertaId, byte[]? rowVersion = null);

        // ✅ CORREGIDO: Cambiar nombres de métodos a síncronos (sin Async en la firma)
        decimal CalcularMora(int cuotaId);
        decimal CalcularMontoTotalCobrable(int cuotaId);

        // Notificaciones
        Task NotificarClienteAlertasAsync(int clienteId);
    }
}