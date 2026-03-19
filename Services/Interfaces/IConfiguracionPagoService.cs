using TheBuryProject.Models.Enums;
using TheBuryProject.ViewModels;

namespace TheBuryProject.Services.Interfaces
{
    public interface IConfiguracionPagoService
    {
        Task<List<ConfiguracionPagoViewModel>> GetAllAsync();
        Task<ConfiguracionPagoViewModel?> GetByIdAsync(int id);
        Task<ConfiguracionPagoViewModel?> GetByTipoPagoAsync(TipoPago tipoPago);
        Task<decimal> ObtenerTasaInteresMensualCreditoPersonalAsync();
        Task<ConfiguracionPagoViewModel> CreateAsync(ConfiguracionPagoViewModel viewModel);
        Task<ConfiguracionPagoViewModel?> UpdateAsync(int id, ConfiguracionPagoViewModel viewModel);
        Task<bool> DeleteAsync(int id);
        Task<List<ConfiguracionTarjetaViewModel>> GetTarjetasActivasAsync();
        Task<ConfiguracionTarjetaViewModel?> GetTarjetaByIdAsync(int id);
        Task<bool> ValidarDescuento(TipoPago tipoPago, decimal descuento);
        Task<decimal> CalcularRecargo(TipoPago tipoPago, decimal monto);
    }
}
