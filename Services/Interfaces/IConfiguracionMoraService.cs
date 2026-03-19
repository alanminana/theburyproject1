using TheBuryProject.ViewModels;

namespace TheBuryProject.Services.Interfaces
{
    public interface IConfiguracionMoraService
    {
        Task<ConfiguracionMoraCompletaViewModel?> GetConfiguracionAsync();
        Task<ConfiguracionMoraCompletaViewModel> SaveConfiguracionAsync(ConfiguracionMoraCompletaViewModel viewModel);
        Task<decimal> CalcularInterePunitorioDiarioAsync(decimal capital, int diasAtraso);
        Task<List<AlertaMoraViewModel>> GetAlertasActivasAsync();
    }
}
