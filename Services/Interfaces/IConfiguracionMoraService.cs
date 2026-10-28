using TheBuryProject.ViewModels;

namespace TheBuryProject.Services.Interfaces
{
    public interface IConfiguracionMoraService
    {
        Task<ConfiguracionMoraCompletaViewModel?> GetConfiguracionAsync();

        /// <summary>
        /// Asigna ColorAlerta, DescripcionAlerta y NivelPrioridad a cada cuota
        /// según los días de atraso y la configuración de alertas de mora activas.
        /// </summary>
        Task AplicarAlertasMoraAsync(IList<CuotaViewModel> cuotas);
    }
}
