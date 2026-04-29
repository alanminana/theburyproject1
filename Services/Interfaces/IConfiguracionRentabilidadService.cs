using TheBuryProject.Models.Entities;

namespace TheBuryProject.Services.Interfaces
{
    public interface IConfiguracionRentabilidadService
    {
        Task<ConfiguracionRentabilidad> GetConfiguracionAsync();
        Task<ConfiguracionRentabilidad> SaveConfiguracionAsync(decimal margenBajoMax, decimal margenAltoMin);
    }
}
