using TheBuryProject.ViewModels;

namespace TheBuryProject.Services.Interfaces;

public interface IConfiguracionPagoGlobalAdminService
{
    Task<ConfiguracionPagoGlobalAdminViewModel> ObtenerAdminGlobalAsync();
}
