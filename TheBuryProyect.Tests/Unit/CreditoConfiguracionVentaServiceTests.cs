using Microsoft.Extensions.Logging.Abstractions;
using TheBuryProject.Models.DTOs;
using TheBuryProject.Models.Enums;
using TheBuryProject.Services;
using TheBuryProject.Services.Interfaces;
using TheBuryProject.Services.Models;
using TheBuryProject.ViewModels;

namespace TheBuryProject.Tests.Unit;

public sealed class CreditoConfiguracionVentaServiceTests
{
    [Fact]
    public async Task Resolver_RechazaTasaGlobalAusente()
    {
        var service = CrearService(ConfigService(tasaGlobal: null));

        var result = await service.ResolverAsync(Modelo(FuenteConfiguracionCredito.Global, MetodoCalculoCredito.Global), venta: null);

        Assert.False(result.EsValido);
        Assert.Equal(string.Empty, result.ErrorKey);
        Assert.Contains("tasa de inter", result.ErrorMessage!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Resolver_RechazaMetodoCalculoAusente()
    {
        var service = CrearService(ConfigService(tasaGlobal: 5m));

        var result = await service.ResolverAsync(Modelo(FuenteConfiguracionCredito.Global, metodo: null), venta: null);

        Assert.False(result.EsValido);
        Assert.Equal(nameof(ConfiguracionCreditoVentaViewModel.MetodoCalculo), result.ErrorKey);
        Assert.Contains("Debe seleccionar", result.ErrorMessage!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Resolver_RechazaUsarClienteSinConfiguracionPersonalizada()
    {
        var service = CrearService(ConfigService(
            tasaGlobal: 5m,
            parametros: new ParametrosCreditoCliente
            {
                TieneConfiguracionPersonalizada = false,
                TasaMensual = 5m
            }));

        var result = await service.ResolverAsync(Modelo(FuenteConfiguracionCredito.PorCliente, MetodoCalculoCredito.UsarCliente), venta: null);

        Assert.False(result.EsValido);
        Assert.Equal(nameof(ConfiguracionCreditoVentaViewModel.MetodoCalculo), result.ErrorKey);
        Assert.Contains("no tiene configuraci", result.ErrorMessage!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Resolver_RechazaTasaManualInvalida()
    {
        var service = CrearService(ConfigService(tasaGlobal: 5m));
        var modelo = Modelo(FuenteConfiguracionCredito.Manual, MetodoCalculoCredito.Manual);
        modelo.TasaMensual = 0m;

        var result = await service.ResolverAsync(modelo, venta: null);

        Assert.False(result.EsValido);
        Assert.Equal(nameof(modelo.TasaMensual), result.ErrorKey);
        Assert.Contains("mayor a 0", result.ErrorMessage!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Resolver_RechazaCuotasFueraDeRangoEfectivo()
    {
        var service = CrearService(
            ConfigService(tasaGlobal: 5m, rango: (1, 24, "Global", null)),
            new StubCreditoRangoProductoService(new CreditoRangoProductoResultado(1, 6, 24, 6, 7, "Producto", "Limite", null)));
        var modelo = Modelo(FuenteConfiguracionCredito.Global, MetodoCalculoCredito.Global, ventaId: 99);
        modelo.CantidadCuotas = 7;

        var result = await service.ResolverAsync(modelo, VentaConProducto());

        Assert.False(result.EsValido);
        Assert.Equal(nameof(modelo.CantidadCuotas), result.ErrorKey);
        Assert.Contains("entre 1 y 6", result.ErrorMessage!, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(6, result.RangoEfectivo!.Max);
    }

    [Fact]
    public async Task Resolver_ArmaComandoValidoParaFuenteGlobal()
    {
        var service = CrearService(ConfigService(tasaGlobal: 5m, rango: (1, 24, "Global", null)));

        var result = await service.ResolverAsync(Modelo(FuenteConfiguracionCredito.Global, MetodoCalculoCredito.Global), venta: null);

        AssertComandoValido(result);
        Assert.Equal(FuenteConfiguracionCredito.Global, result.Comando!.FuenteConfiguracion);
        Assert.Equal(5m, result.Comando.TasaMensual);
        Assert.Equal("Global", result.Comando.FuenteRestriccionCuotasSnap);
    }

    [Fact]
    public async Task Resolver_ArmaComandoValidoParaFuenteManual()
    {
        var service = CrearService(ConfigService(tasaGlobal: 5m, rango: (1, 120, "Manual", null)));
        var modelo = Modelo(FuenteConfiguracionCredito.Manual, MetodoCalculoCredito.Manual);
        modelo.TasaMensual = 7.25m;
        modelo.GastosAdministrativos = 150m;

        var result = await service.ResolverAsync(modelo, venta: null);

        AssertComandoValido(result);
        Assert.Equal(FuenteConfiguracionCredito.Manual, result.Comando!.FuenteConfiguracion);
        Assert.Equal(7.25m, result.Comando.TasaMensual);
        Assert.Equal(150m, result.Comando.GastosAdministrativos);
    }

    [Fact]
    public async Task Resolver_ArmaComandoValidoParaFuentePorCliente()
    {
        var service = CrearService(ConfigService(
            tasaGlobal: 5m,
            parametros: new ParametrosCreditoCliente
            {
                TieneConfiguracionPersonalizada = true,
                TasaMensual = 6.5m,
                GastosAdministrativos = 250m
            },
            rango: (1, 18, "Cliente", null)));

        var result = await service.ResolverAsync(Modelo(FuenteConfiguracionCredito.PorCliente, MetodoCalculoCredito.UsarCliente), venta: null);

        AssertComandoValido(result);
        Assert.Equal(FuenteConfiguracionCredito.PorCliente, result.Comando!.FuenteConfiguracion);
        Assert.Equal(6.5m, result.Comando.TasaMensual);
        Assert.Equal(0m, result.Comando.GastosAdministrativos);
        Assert.Equal(18, result.Comando.CuotasMaxPermitidas);
    }

    [Fact]
    public async Task Resolver_ConservaSnapshotsDeRestriccionPorProducto()
    {
        var service = CrearService(
            ConfigService(tasaGlobal: 5m, rango: (1, 24, "Global", null)),
            new StubCreditoRangoProductoService(new CreditoRangoProductoResultado(1, 6, 24, 6, 7, "Producto", "Limite", null)));
        var modelo = Modelo(FuenteConfiguracionCredito.Global, MetodoCalculoCredito.Global, ventaId: 99);
        modelo.CantidadCuotas = 6;

        var result = await service.ResolverAsync(modelo, VentaConProducto());

        AssertComandoValido(result);
        Assert.Equal("Producto", result.Comando!.FuenteRestriccionCuotasSnap);
        Assert.Equal(7, result.Comando.ProductoIdRestrictivoSnap);
        Assert.Equal(24, result.Comando.MaxCuotasBaseSnap);
        Assert.Equal(6, result.Comando.CuotasMaxPermitidas);
    }

    private static CreditoConfiguracionVentaService CrearService(
        StubConfiguracionPagoService configuracionPagoService,
        ICreditoRangoProductoService? rangoProductoService = null) =>
        new(
            configuracionPagoService,
            NullLogger<CreditoConfiguracionVentaService>.Instance,
            rangoProductoService);

    private static StubConfiguracionPagoService ConfigService(
        decimal? tasaGlobal,
        ParametrosCreditoCliente? parametros = null,
        (int Min, int Max, string Descripcion, string? PerfilNombre)? rango = null) =>
        new()
        {
            TasaGlobal = tasaGlobal,
            Parametros = parametros ?? new ParametrosCreditoCliente
            {
                Fuente = FuenteConfiguracionCredito.Global,
                TasaMensual = tasaGlobal ?? 0m,
                GastosAdministrativos = 0m
            },
            Rango = rango ?? (1, 120, "Manual", null)
        };

    private static ConfiguracionCreditoVentaViewModel Modelo(
        FuenteConfiguracionCredito fuente,
        MetodoCalculoCredito? metodo,
        int? ventaId = null) =>
        new()
        {
            CreditoId = 10,
            VentaId = ventaId,
            ClienteId = 20,
            Monto = 10_000m,
            Anticipo = 0m,
            CantidadCuotas = 6,
            TasaMensual = fuente == FuenteConfiguracionCredito.Manual ? 5m : null,
            GastosAdministrativos = 0m,
            FechaPrimeraCuota = new DateTime(2026, 6, 7),
            FuenteConfiguracion = fuente,
            MetodoCalculo = metodo
        };

    private static VentaViewModel VentaConProducto() =>
        new()
        {
            Id = 99,
            Total = 10_000m,
            Detalles = new List<VentaDetalleViewModel>
            {
                new() { ProductoId = 7, ProductoNombre = "Producto restrictivo" }
            }
        };

    private static void AssertComandoValido(CreditoConfiguracionVentaResultado result)
    {
        Assert.True(result.EsValido);
        Assert.NotNull(result.Comando);
        Assert.Equal(10, result.Comando!.CreditoId);
        Assert.Equal(10_000m, result.Comando.Monto);
        Assert.Equal(6, result.Comando.CantidadCuotas);
    }

    private sealed class StubCreditoRangoProductoService : ICreditoRangoProductoService
    {
        private readonly CreditoRangoProductoResultado _resultado;

        public StubCreditoRangoProductoService(CreditoRangoProductoResultado resultado)
        {
            _resultado = resultado;
        }

        public Task<CreditoRangoProductoResultado> ResolverAsync(
            VentaViewModel? venta,
            TipoPago tipoPago,
            int minBase,
            int maxBase,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(_resultado);
    }

    private sealed class StubConfiguracionPagoService : IConfiguracionPagoService
    {
        public decimal? TasaGlobal { get; init; }
        public ParametrosCreditoCliente Parametros { get; init; } = new();
        public (int Min, int Max, string Descripcion, string? PerfilNombre) Rango { get; init; } = (1, 120, "Manual", null);

        public Task<decimal?> ObtenerTasaInteresMensualCreditoPersonalAsync() => Task.FromResult(TasaGlobal);
        public Task<ParametrosCreditoCliente> ObtenerParametrosCreditoClienteAsync(int clienteId, decimal tasaGlobal) => Task.FromResult(Parametros);
        public Task<(int Min, int Max, string Descripcion, string? PerfilNombre)> ResolverRangoCuotasAsync(MetodoCalculoCredito metodo, int? perfilId, int? clienteId) => Task.FromResult(Rango);

        public Task<List<ConfiguracionPagoViewModel>> GetAllAsync() => throw new NotImplementedException();
        public Task<ConfiguracionPagoViewModel?> GetByIdAsync(int id) => throw new NotImplementedException();
        public Task<ConfiguracionPagoViewModel?> GetByTipoPagoAsync(TipoPago tipoPago) => throw new NotImplementedException();
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
        public Task<MaxCuotasSinInteresResultado?> ObtenerMaxCuotasSinInteresEfectivoAsync(int tarjetaId, IEnumerable<int> productoIds) => throw new NotImplementedException();
    }
}
