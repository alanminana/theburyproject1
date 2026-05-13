using TheBuryProject.ViewModels;

namespace TheBuryProject.Services.Interfaces;

public interface IConfiguracionPagoGlobalAdminService
{
    Task<ConfiguracionPagoGlobalAdminViewModel> ObtenerAdminGlobalAsync();
    Task<PlanPagoGlobalAdminViewModel> CrearPlanGlobalAsync(PlanPagoGlobalCommandViewModel command);
    Task<PlanPagoGlobalAdminViewModel?> ActualizarPlanGlobalAsync(int id, PlanPagoGlobalCommandViewModel command);
    Task<bool> CambiarEstadoPlanGlobalAsync(int id, bool activo);
}
