using TheBuryProject.Models.Enums;
using TheBuryProject.Services.Interfaces;
using TheBuryProject.Services.Models;
using TheBuryProject.ViewModels;

namespace TheBuryProject.Tests;

/// <summary>
/// Stub de IConfiguracionPagoService para tests de VentaService que no ejercen
/// la restriccion de cuotas sin interes. Devuelve null en ObtenerMaxCuotasSinInteresEfectivoAsync
/// (sin restriccion) y lanza NotImplementedException en todo lo demas.
/// </summary>
internal class StubConfiguracionPagoServiceVenta : IConfiguracionPagoService
{
    public virtual MaxCuotasSinInteresResultado? MaxCuotasResult { get; set; }

    public Task<MaxCuotasSinInteresResultado?> ObtenerMaxCuotasSinInteresEfectivoAsync(
        int tarjetaId, IEnumerable<int> productoIds)
        => Task.FromResult(MaxCuotasResult);

    public Task<List<ConfiguracionPagoViewModel>> GetAllAsync() => throw new NotImplementedException();
    public Task<ConfiguracionPagoViewModel?> GetByIdAsync(int id) => throw new NotImplementedException();
    public Task<ConfiguracionPagoViewModel?> GetByTipoPagoAsync(TipoPago tipoPago) => throw new NotImplementedException();
    public Task<decimal?> ObtenerTasaInteresMensualCreditoPersonalAsync() => throw new NotImplementedException();
    public Task<ConfiguracionPagoViewModel> CreateAsync(ConfiguracionPagoViewModel viewModel) => throw new NotImplementedException();
    public Task<ConfiguracionPagoViewModel?> UpdateAsync(int id, ConfiguracionPagoViewModel viewModel) => throw new NotImplementedException();
    public Task<bool> DeleteAsync(int id) => throw new NotImplementedException();
    public Task GuardarConfiguracionesModalAsync(IReadOnlyList<ConfiguracionPagoViewModel> configuraciones) => throw new NotImplementedException();
    public Task<List<ConfiguracionTarjetaViewModel>> GetTarjetasActivasAsync() => throw new NotImplementedException();
    public Task<List<TarjetaActivaVentaResultado>> GetTarjetasActivasParaVentaAsync() => throw new NotImplementedException();
    public Task<ConfiguracionTarjetaViewModel?> GetTarjetaByIdAsync(int id) => throw new NotImplementedException();
    public Task<bool> ValidarDescuento(TipoPago tipoPago, decimal descuento) => throw new NotImplementedException();
    public Task<decimal> CalcularRecargo(TipoPago tipoPago, decimal monto) => throw new NotImplementedException();
    public Task<List<PerfilCreditoViewModel>> GetPerfilesCreditoAsync() => throw new NotImplementedException();
    public Task<List<PerfilCreditoViewModel>> GetPerfilesCreditoActivosAsync() => throw new NotImplementedException();
    public Task GuardarCreditoPersonalAsync(CreditoPersonalConfigViewModel config) => throw new NotImplementedException();
    public Task<ParametrosCreditoCliente> ObtenerParametrosCreditoClienteAsync(int clienteId, decimal tasaGlobal) => throw new NotImplementedException();
    public Task<(int Min, int Max, string Descripcion, string? PerfilNombre)> ResolverRangoCuotasAsync(
        MetodoCalculoCredito metodo, int? perfilId, int? clienteId) => throw new NotImplementedException();
}
