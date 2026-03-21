using TheBuryProject.ViewModels;

namespace TheBuryProject.Services.Interfaces
{
    public interface IConfiguracionMoraService
    {
        Task<ConfiguracionMoraCompletaViewModel?> GetConfiguracionAsync();
        Task<ConfiguracionMoraCompletaViewModel> SaveConfiguracionAsync(ConfiguracionMoraCompletaViewModel viewModel);
    }
}
