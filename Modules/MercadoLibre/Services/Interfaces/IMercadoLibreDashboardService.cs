using TheBuryProject.Modules.MercadoLibre.ViewModels;

namespace TheBuryProject.Modules.MercadoLibre.Services.Interfaces
{
    /// <summary>
    /// Arma el dashboard operativo de Mercado Libre (Fase 17).
    /// Es de SOLO LECTURA: agrega KPIs, alertas y listas recientes desde la
    /// base local. Nunca llama a la API de ML, ni mueve stock, caja o ventas.
    /// </summary>
    public interface IMercadoLibreDashboardService
    {
        Task<MercadoLibreDashboardViewModel> GetDashboardAsync(CancellationToken ct = default);
    }
}
