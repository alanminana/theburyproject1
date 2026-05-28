using TheBuryProject.ViewModels;

namespace TheBuryProject.Services.Interfaces;

public interface IConfiguracionPagoGlobalAdminService
{
    Task<ConfiguracionPagoGlobalAdminViewModel> ObtenerAdminGlobalAsync();
    Task<IReadOnlyList<TarjetaGlobalAdminViewModel>> ListarTarjetasGlobalesAsync(int? configuracionPagoId = null);
    Task<TarjetaGlobalAdminViewModel?> ObtenerTarjetaGlobalAsync(int id);
    Task<TarjetaGlobalAdminViewModel> CrearTarjetaGlobalAsync(TarjetaGlobalCommandViewModel command);
    Task<TarjetaGlobalAdminViewModel?> ActualizarTarjetaGlobalAsync(int id, TarjetaGlobalCommandViewModel command);
    Task<bool> CambiarEstadoTarjetaGlobalAsync(int id, bool activa);
    Task<PlanPagoGlobalAdminViewModel> CrearPlanGlobalAsync(PlanPagoGlobalCommandViewModel command);
    Task<PlanPagoGlobalAdminViewModel?> ActualizarPlanGlobalAsync(int id, PlanPagoGlobalCommandViewModel command);
    Task<bool> CambiarEstadoPlanGlobalAsync(int id, bool activo);
}
