using TheBuryProject.Models.Enums;
using TheBuryProject.Services.Interfaces;
using TheBuryProject.Services.Models;
using TheBuryProject.ViewModels;

namespace TheBuryProject.Tests;

/// <summary>
/// Stub de IConfiguracionPagoService para tests de VentaService que no ejercen
/// la restriccion de cuotas sin interes. Devuelve null en ObtenerMaxCuotasSinInteresEfectivoAsync
/// (sin restriccion) y rangos base actuales para credito personal.
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
        MetodoCalculoCredito metodo, int? perfilId, int? clienteId)
    {
        var rango = metodo == MetodoCalculoCredito.Manual
            ? (Min: 1, Max: 120, Descripcion: "Manual", PerfilNombre: (string?)null)
            : (Min: 1, Max: 24, Descripcion: "Global", PerfilNombre: (string?)null);

        return Task.FromResult(rango);
    }

    public Task<List<MontoPorPuntajeCreditoViewModel>> GetMontosPorPuntajeAsync()
        => Task.FromResult(new List<MontoPorPuntajeCreditoViewModel>());

    public Task<(bool Ok, List<string> Errores)> GuardarMontosPorPuntajeAsync(
        List<MontoPorPuntajeCreditoViewModel> items, string usuario)
        => Task.FromResult((true, new List<string>()));

    // Micro-lote 4: los planes globales activos son la unica fuente de cantidades. En produccion
    // siempre existen; el stub ofrece 1..24 (tasa null = heredar la global) para que confirmar un
    // credito personal no sea rechazado por "sin planes globales". Sobrescribible por test.
    public virtual List<CuotaCreditoPersonalViewModel> CuotasCreditoPersonal { get; set; } =
        Enumerable.Range(1, 24)
            .Select(n => new CuotaCreditoPersonalViewModel { CantidadCuotas = n, TasaMensual = null, Activo = true })
            .ToList();

    public Task<List<CuotaCreditoPersonalViewModel>> GetCuotasCreditoPersonalAsync()
        => Task.FromResult(CuotasCreditoPersonal);

    public Task<List<CuotaCreditoPersonalViewModel>> GetCuotasCreditoPersonalActivasAsync()
        => Task.FromResult(CuotasCreditoPersonal.Where(c => c.Activo).ToList());

    public async Task<PlanesCreditoPersonalResultado> ResolverPlanesCreditoPersonalAsync(IEnumerable<int> productoIds)
        => PlanesCreditoPersonalStub.DesdeGlobales(await GetCuotasCreditoPersonalActivasAsync());

    public Task<(bool Ok, List<string> Errores)> GuardarCuotasCreditoPersonalAsync(
        List<CuotaCreditoPersonalViewModel> items, string usuario)
        => Task.FromResult((true, new List<string>()));
}
