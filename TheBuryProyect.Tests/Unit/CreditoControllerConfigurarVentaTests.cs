using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using TheBuryProject.Controllers;
using TheBuryProject.Models.DTOs;
using TheBuryProject.Models.Entities;
using TheBuryProject.Models.Enums;
using TheBuryProject.Services.Interfaces;
using TheBuryProject.Services.Models;
using TheBuryProject.ViewModels;
using TheBuryProject.ViewModels.Requests;
using TheBuryProject.ViewModels.Responses;

namespace TheBuryProject.Tests.Unit;

public class CreditoControllerConfigurarVentaTests
{
    [Fact]
    public async Task ConfigurarVentaGet_ConservaMontoDesdeCreditoOSobrescribeConTotalVenta()
    {
        var creditoService = new RecordingCreditoService(CreditoBase(montoAprobado: 12_000m, montoSolicitado: 9_000m));
        var configService = ConfigService(tasaGlobal: 4m);
        var contratoService = new StubContratoVentaCreditoService(existeContrato: true, existePlantilla: true);

        var sinVenta = await CrearController(
            creditoService,
            configService,
            ventaService: new StubVentaService())
            .ConfigurarVenta(id: 10, ventaId: null);

        var modeloSinVenta = AssertViewModel(sinVenta);
        Assert.Equal(12_000m, modeloSinVenta.Monto);
        Assert.False(modeloSinVenta.ContratoGenerado);

        var conVenta = await CrearController(
            creditoService,
            configService,
            ventaService: new StubVentaService(totalVenta: 15_500m),
            contratoService: contratoService)
            .ConfigurarVenta(id: 10, ventaId: 99);

        var modeloConVenta = AssertViewModel(conVenta);
        Assert.Equal(15_500m, modeloConVenta.Monto);
        Assert.True(modeloConVenta.ContratoGenerado);
        Assert.True(modeloConVenta.PlantillaActivaDisponible);
    }

    [Fact]
    public async Task ConfigurarVentaGet_ConservaClienteConfigPersonalizadaYPerfilesActivosTipados()
    {
        var configService = ConfigService(
            tasaGlobal: 6m,
            parametros: new ParametrosCreditoCliente
            {
                Fuente = FuenteConfiguracionCredito.PorCliente,
                TieneTasaPersonalizada = true,
                TasaPersonalizada = 7.5m,
                GastosPersonalizados = 125m,
                CuotasMinimas = 3,
                CuotasMaximas = 18,
                TasaMensual = 7.5m,
                GastosAdministrativos = 125m,
                TieneConfiguracionPersonalizada = true,
                PerfilPreferidoId = 2,
                PerfilPreferidoNombre = "Preferente",
                MontoMinimo = 1_000m,
                MontoMaximo = 90_000m
            },
            perfiles: new List<PerfilCreditoViewModel>
            {
                new() { Id = 2, Nombre = "Preferente", TasaMensual = 7.5m, GastosAdministrativos = 125m, MinCuotas = 3, MaxCuotas = 18 }
            });

        var controller = CrearController(new RecordingCreditoService(CreditoBase()), configService);

        var result = await controller.ConfigurarVenta(id: 10, ventaId: null);

        var model = AssertViewModel(result);
        var clienteConfig = model.ClienteConfigPersonalizada;
        Assert.True(clienteConfig.TieneConfiguracionCliente);
        Assert.True(clienteConfig.TieneTasaPersonalizada);
        Assert.Equal(7.5m, clienteConfig.TasaPersonalizada);
        Assert.Equal(18, clienteConfig.CuotasMaximas);
        Assert.Equal(6m, clienteConfig.TasaGlobal);
        Assert.Equal(2, clienteConfig.PerfilPreferidoId);
        Assert.Equal(120, clienteConfig.MaxCuotasBase);

        var perfil = Assert.Single(model.PerfilesActivos);
        Assert.Equal("Preferente", perfil.Nombre);
        Assert.Equal(7.5m, perfil.TasaMensual);
        Assert.Equal(125m, perfil.GastosAdministrativos);
    }

    [Fact]
    public async Task ConfigurarVentaPost_RechazaTasaGlobalAusente()
    {
        var controller = CrearController(
            new RecordingCreditoService(CreditoBase()),
            ConfigService(tasaGlobal: null));

        var result = await controller.ConfigurarVenta(ModeloPost(FuenteConfiguracionCredito.Global, MetodoCalculoCredito.Global));

        AssertViewWithModelError(result, controller, string.Empty, "tasa de inter");
    }

    [Fact]
    public async Task ConfigurarVentaPost_RechazaMetodoCalculoAusente()
    {
        var controller = CrearController(new RecordingCreditoService(CreditoBase()), ConfigService(tasaGlobal: 5m));
        var modelo = ModeloPost(FuenteConfiguracionCredito.Global, metodo: null);

        var result = await controller.ConfigurarVenta(modelo);

        AssertViewWithModelError(result, controller, nameof(modelo.MetodoCalculo), "Debe seleccionar");
    }

    [Fact]
    public async Task ConfigurarVentaPost_RechazaUsarClienteSinConfiguracionPersonalizada()
    {
        var controller = CrearController(
            new RecordingCreditoService(CreditoBase()),
            ConfigService(tasaGlobal: 5m, parametros: new ParametrosCreditoCliente
            {
                TieneConfiguracionPersonalizada = false,
                TasaMensual = 5m
            }));
        var modelo = ModeloPost(FuenteConfiguracionCredito.PorCliente, MetodoCalculoCredito.UsarCliente);

        var result = await controller.ConfigurarVenta(modelo);

        AssertViewWithModelError(result, controller, nameof(modelo.MetodoCalculo), "no tiene configuraci");
    }

    [Fact]
    public async Task ConfigurarVentaPost_RechazaTasaManualInvalida()
    {
        var controller = CrearController(new RecordingCreditoService(CreditoBase()), ConfigService(tasaGlobal: 5m));
        var modelo = ModeloPost(FuenteConfiguracionCredito.Manual, MetodoCalculoCredito.Manual);
        modelo.TasaMensual = 0m;

        var result = await controller.ConfigurarVenta(modelo);

        AssertViewWithModelError(result, controller, nameof(modelo.TasaMensual), "mayor a 0");
    }

    [Fact]
    public async Task ConfigurarVentaPost_RechazaCuotasFueraDeRangoBase()
    {
        var controller = CrearController(
            new RecordingCreditoService(CreditoBase()),
            ConfigService(tasaGlobal: 5m, rango: (3, 12, "Global", null)));
        var modelo = ModeloPost(FuenteConfiguracionCredito.Global, MetodoCalculoCredito.Global);
        modelo.CantidadCuotas = 13;

        var result = await controller.ConfigurarVenta(modelo);

        AssertViewWithModelError(result, controller, nameof(modelo.CantidadCuotas), "entre 3 y 12");
    }

    [Fact]
    public async Task ConfigurarVentaPost_RechazaCuotasFueraDeRangoPorProducto()
    {
        var controller = CrearController(
            new RecordingCreditoService(CreditoBase()),
            ConfigService(tasaGlobal: 5m, rango: (1, 24, "Global", null)),
            ventaService: new StubVentaService(venta: VentaConProducto()),
            productoCreditoRestriccionService: new StubProductoCreditoRestriccionService(new ProductoCreditoRestriccionResultado
            {
                Permitido = true,
                MaxCuotasCredito = 6,
                ProductoIdsRestrictivos = new[] { 7 }
            }));
        var modelo = ModeloPost(FuenteConfiguracionCredito.Global, MetodoCalculoCredito.Global, ventaId: 99);
        modelo.CantidadCuotas = 7;

        var result = await controller.ConfigurarVenta(modelo);

        AssertViewWithModelError(result, controller, nameof(modelo.CantidadCuotas), "entre 1 y 6");
        Assert.Equal(24, modelo.MaxCuotasBase);
        Assert.Equal(6, modelo.MaxCuotasCreditoProducto);
        Assert.Equal(7, modelo.ProductoIdRestrictivo);
    }

    [Fact]
    public async Task ConfigurarVentaPost_RechazaProductoBloqueanteParaCreditoPersonal()
    {
        var controller = CrearController(
            new RecordingCreditoService(CreditoBase()),
            ConfigService(tasaGlobal: 5m),
            ventaService: new StubVentaService(venta: VentaConProducto()),
            productoCreditoRestriccionService: new StubProductoCreditoRestriccionService(new ProductoCreditoRestriccionResultado
            {
                Permitido = false,
                ProductoIdsBloqueantes = new[] { 7 }
            }));
        var modelo = ModeloPost(FuenteConfiguracionCredito.Global, MetodoCalculoCredito.Global, ventaId: 99);

        var result = await controller.ConfigurarVenta(modelo);

        AssertViewWithModelError(result, controller, nameof(modelo.CantidadCuotas), "bloquea el medio de pago");
    }

    [Fact]
    public async Task ConfigurarVentaPost_ConProductoRestrictivo_ConservaSnapshotsDelComando()
    {
        var creditoService = new RecordingCreditoService(CreditoBase());
        var controller = CrearController(
            creditoService,
            ConfigService(tasaGlobal: 5m, rango: (1, 24, "Global", null)),
            ventaService: new StubVentaService(venta: VentaConProducto()),
            productoCreditoRestriccionService: new StubProductoCreditoRestriccionService(new ProductoCreditoRestriccionResultado
            {
                Permitido = true,
                MaxCuotasCredito = 6,
                ProductoIdsRestrictivos = new[] { 7 }
            }));
        var modelo = ModeloPost(FuenteConfiguracionCredito.Global, MetodoCalculoCredito.Global, ventaId: 99);
        modelo.CantidadCuotas = 6;

        var result = await controller.ConfigurarVenta(modelo);

        Assert.IsType<RedirectToActionResult>(result);
        Assert.NotNull(creditoService.LastCommand);
        Assert.Equal("Producto", creditoService.LastCommand!.FuenteRestriccionCuotasSnap);
        Assert.Equal(7, creditoService.LastCommand.ProductoIdRestrictivoSnap);
        Assert.Equal(24, creditoService.LastCommand.MaxCuotasBaseSnap);
        Assert.Equal(6, creditoService.LastCommand.CuotasMaxPermitidas);
    }

    private static CreditoController CrearController(
        RecordingCreditoService creditoService,
        StubConfiguracionPagoService configuracionPagoService,
        IVentaService? ventaService = null,
        IContratoVentaCreditoService? contratoService = null,
        IProductoCreditoRestriccionService? productoCreditoRestriccionService = null)
    {
        var controller = new CreditoController(
            creditoService: creditoService,
            evaluacionService: null!,
            financialService: null!,
            configuracionPagoService: configuracionPagoService,
            configuracionMoraService: null!,
            ventaService: ventaService ?? new StubVentaService(),
            logger: NullLogger<CreditoController>.Instance,
            creditoDisponibleService: null!,
            currentUser: null!,
            viewBagBuilder: null!,
            contratoVentaCreditoService: contratoService ?? new StubContratoVentaCreditoService(),
            aptitudService: null,
            productoCreditoRestriccionService: productoCreditoRestriccionService);
        controller.TempData = new TempDataDictionary(new DefaultHttpContext(), new InMemoryTempDataProvider());
        return controller;
    }

    private static StubConfiguracionPagoService ConfigService(
        decimal? tasaGlobal,
        ParametrosCreditoCliente? parametros = null,
        List<PerfilCreditoViewModel>? perfiles = null,
        (int Min, int Max, string Descripcion, string? PerfilNombre)? rango = null)
    {
        return new StubConfiguracionPagoService
        {
            TasaGlobal = tasaGlobal,
            Parametros = parametros ?? new ParametrosCreditoCliente
            {
                Fuente = FuenteConfiguracionCredito.Global,
                TasaMensual = tasaGlobal ?? 0m,
                GastosAdministrativos = 0m
            },
            Perfiles = perfiles ?? new List<PerfilCreditoViewModel>(),
            Rango = rango ?? (1, 120, "Manual", null)
        };
    }

    private static CreditoViewModel CreditoBase(decimal montoAprobado = 0m, decimal montoSolicitado = 10_000m) =>
        new()
        {
            Id = 10,
            ClienteId = 20,
            ClienteNombre = "Cliente Test",
            Numero = "CR-10",
            Estado = EstadoCredito.PendienteConfiguracion,
            MontoAprobado = montoAprobado,
            MontoSolicitado = montoSolicitado,
            FechaPrimeraCuota = new DateTime(2026, 6, 7)
        };

    private static ConfiguracionCreditoVentaViewModel ModeloPost(
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

    private static ConfiguracionCreditoVentaViewModel AssertViewModel(IActionResult result)
    {
        var view = Assert.IsType<ViewResult>(result);
        Assert.Equal("ConfigurarVenta_tw", view.ViewName);
        return Assert.IsType<ConfiguracionCreditoVentaViewModel>(view.Model);
    }

    private static void AssertViewWithModelError(
        IActionResult result,
        CreditoController controller,
        string key,
        string expectedFragment)
    {
        AssertViewModel(result);
        Assert.False(controller.ModelState.IsValid);
        Assert.Contains(controller.ModelState[key]!.Errors, e =>
            e.ErrorMessage.Contains(expectedFragment, StringComparison.OrdinalIgnoreCase));
    }

    private sealed class RecordingCreditoService : ICreditoService
    {
        private readonly CreditoViewModel? _credito;

        public RecordingCreditoService(CreditoViewModel? credito)
        {
            _credito = credito;
        }

        public ConfiguracionCreditoComando? LastCommand { get; private set; }

        public Task<CreditoViewModel?> GetByIdAsync(int id) => Task.FromResult(_credito);
        public Task ConfigurarCreditoAsync(ConfiguracionCreditoComando comando)
        {
            LastCommand = comando;
            return Task.CompletedTask;
        }

        public Task<List<CreditoViewModel>> GetAllAsync(CreditoFilterViewModel? filter = null) => throw new NotImplementedException();
        public Task<List<CreditoViewModel>> GetByClienteIdAsync(int clienteId) => throw new NotImplementedException();
        public Task<CreditoViewModel> CreateAsync(CreditoViewModel viewModel) => throw new NotImplementedException();
        public Task<CreditoViewModel> CreatePendienteConfiguracionAsync(int clienteId, decimal montoTotal) => throw new NotImplementedException();
        public Task<bool> UpdateAsync(CreditoViewModel viewModel) => throw new NotImplementedException();
        public Task<bool> DeleteAsync(int id) => throw new NotImplementedException();
        public Task<SimularCreditoViewModel> SimularCreditoAsync(SimularCreditoViewModel modelo) => throw new NotImplementedException();
        public Task<bool> AprobarCreditoAsync(int creditoId, string aprobadoPor) => throw new NotImplementedException();
        public Task<bool> RechazarCreditoAsync(int creditoId, string motivo) => throw new NotImplementedException();
        public Task<bool> CancelarCreditoAsync(int creditoId, string motivo) => throw new NotImplementedException();
        public Task<(bool Success, string? NumeroCredito, string? ErrorMessage)> SolicitarCreditoAsync(SolicitudCreditoViewModel solicitud, string usuarioSolicitante, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<List<CuotaViewModel>> GetCuotasByCreditoAsync(int creditoId) => throw new NotImplementedException();
        public Task<CuotaViewModel?> GetCuotaByIdAsync(int cuotaId) => throw new NotImplementedException();
        public Task<bool> PagarCuotaAsync(PagarCuotaViewModel pago) => throw new NotImplementedException();
        public Task<PagoMultipleCuotasResult> PagarCuotasAsync(PagoMultipleCuotasRequest request, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<bool> AdelantarCuotaAsync(PagarCuotaViewModel pago) => throw new NotImplementedException();
        public Task<CuotaViewModel?> GetPrimeraCuotaPendienteAsync(int creditoId) => throw new NotImplementedException();
        public Task<CuotaViewModel?> GetUltimaCuotaPendienteAsync(int creditoId) => throw new NotImplementedException();
        public Task<List<CuotaViewModel>> GetCuotasVencidasAsync() => throw new NotImplementedException();
        public Task ActualizarEstadoCuotasAsync() => throw new NotImplementedException();
        public Task<bool> RecalcularSaldoCreditoAsync(int creditoId) => throw new NotImplementedException();
    }

    private sealed class StubConfiguracionPagoService : IConfiguracionPagoService
    {
        public decimal? TasaGlobal { get; init; }
        public ParametrosCreditoCliente Parametros { get; init; } = new();
        public List<PerfilCreditoViewModel> Perfiles { get; init; } = new();
        public (int Min, int Max, string Descripcion, string? PerfilNombre) Rango { get; init; } = (1, 120, "Manual", null);

        public Task<decimal?> ObtenerTasaInteresMensualCreditoPersonalAsync() => Task.FromResult(TasaGlobal);
        public Task<List<PerfilCreditoViewModel>> GetPerfilesCreditoActivosAsync() => Task.FromResult(Perfiles);
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
        public Task GuardarCreditoPersonalAsync(CreditoPersonalConfigViewModel config) => throw new NotImplementedException();
        public Task<MaxCuotasSinInteresResultado?> ObtenerMaxCuotasSinInteresEfectivoAsync(int tarjetaId, IEnumerable<int> productoIds) => throw new NotImplementedException();
    }

    private sealed class StubVentaService : IVentaService
    {
        private readonly decimal? _totalVenta;
        private readonly VentaViewModel? _venta;

        public StubVentaService(decimal? totalVenta = null, VentaViewModel? venta = null)
        {
            _totalVenta = totalVenta;
            _venta = venta;
        }

        public Task<decimal?> GetTotalVentaAsync(int ventaId) => Task.FromResult(_totalVenta);
        public Task<VentaViewModel?> GetByIdAsync(int id) => Task.FromResult(_venta);

        public Task<List<VentaViewModel>> GetAllAsync(VentaFilterViewModel? filter = null) => throw new NotImplementedException();
        public Task<VentaViewModel> CreateAsync(VentaViewModel viewModel) => throw new NotImplementedException();
        public Task<VentaViewModel?> UpdateAsync(int id, VentaViewModel viewModel) => throw new NotImplementedException();
        public Task<bool> DeleteAsync(int id) => throw new NotImplementedException();
        public Task<bool> ConfirmarVentaAsync(int id) => throw new NotImplementedException();
        public Task<bool> ConfirmarVentaCreditoAsync(int id) => throw new NotImplementedException();
        public Task<bool> CancelarVentaAsync(int id, string motivo) => throw new NotImplementedException();
        public Task AsociarCreditoAVentaAsync(int ventaId, int creditoId) => throw new NotImplementedException();
        public Task<bool> FacturarVentaAsync(int id, FacturaViewModel facturaViewModel) => throw new NotImplementedException();
        public Task<int?> AnularFacturaAsync(int facturaId, string motivo) => throw new NotImplementedException();
        public Task<bool> ValidarStockAsync(int ventaId) => throw new NotImplementedException();
        public Task<bool> SolicitarAutorizacionAsync(int id, string usuarioSolicita, string motivo) => throw new NotImplementedException();
        public Task<bool> AutorizarVentaAsync(int id, string usuarioAutoriza, string motivo) => throw new NotImplementedException();
        public Task<bool> RechazarVentaAsync(int id, string usuarioAutoriza, string motivo) => throw new NotImplementedException();
        public Task<bool> RegistrarExcepcionDocumentalAsync(int id, string usuarioAutoriza, string motivo) => throw new NotImplementedException();
        public Task<bool> RequiereAutorizacionAsync(VentaViewModel viewModel) => throw new NotImplementedException();
        public Task<bool> GuardarDatosTarjetaAsync(int ventaId, DatosTarjetaViewModel datosTarjeta) => throw new NotImplementedException();
        public Task<bool> GuardarDatosChequeAsync(int ventaId, DatosChequeViewModel datosCheque) => throw new NotImplementedException();
        public Task<DatosTarjetaViewModel> CalcularCuotasTarjetaAsync(int tarjetaId, decimal monto, int cuotas) => throw new NotImplementedException();
        public Task<DatosCreditoPersonallViewModel> CalcularCreditoPersonallAsync(int creditoId, decimal montoAFinanciar, int cuotas, DateTime fechaPrimeraCuota) => throw new NotImplementedException();
        public Task<DatosCreditoPersonallViewModel?> ObtenerDatosCreditoVentaAsync(int ventaId) => throw new NotImplementedException();
        public Task<bool> ValidarDisponibilidadCreditoAsync(int creditoId, decimal monto) => throw new NotImplementedException();
        public CalculoTotalesVentaResponse CalcularTotalesPreview(List<DetalleCalculoVentaRequest> detalles, decimal descuentoGeneral, bool descuentoEsPorcentaje) => throw new NotImplementedException();
        public Task<CalculoTotalesVentaResponse> CalcularTotalesPreviewAsync(List<DetalleCalculoVentaRequest> detalles, decimal descuentoGeneral, bool descuentoEsPorcentaje) => throw new NotImplementedException();
    }

    private sealed class StubContratoVentaCreditoService : IContratoVentaCreditoService
    {
        private readonly bool _existeContrato;
        private readonly bool _existePlantilla;

        public StubContratoVentaCreditoService(bool existeContrato = false, bool existePlantilla = false)
        {
            _existeContrato = existeContrato;
            _existePlantilla = existePlantilla;
        }

        public Task<bool> ExisteContratoGeneradoAsync(int ventaId) => Task.FromResult(_existeContrato);
        public Task<bool> ExistePlantillaActivaAsync() => Task.FromResult(_existePlantilla);

        public Task<ContratoVentaCreditoValidacionResult> ValidarDatosParaGenerarAsync(int ventaId) => throw new NotImplementedException();
        public Task<ContratoVentaCredito> GenerarAsync(int ventaId, string usuario) => throw new NotImplementedException();
        public Task<ContratoVentaCredito> GenerarPdfAsync(int ventaId, string usuario) => throw new NotImplementedException();
        public Task<ContratoVentaCreditoPdfArchivo?> ObtenerPdfAsync(int ventaId) => throw new NotImplementedException();
        public Task<ContratoVentaCredito?> ObtenerContratoPorVentaAsync(int ventaId) => throw new NotImplementedException();
        public Task<ContratoVentaCredito?> ObtenerContratoPorCreditoAsync(int creditoId) => throw new NotImplementedException();
    }

    private sealed class StubProductoCreditoRestriccionService : IProductoCreditoRestriccionService
    {
        private readonly ProductoCreditoRestriccionResultado _resultado;

        public StubProductoCreditoRestriccionService(ProductoCreditoRestriccionResultado resultado)
        {
            _resultado = resultado;
        }

        public Task<ProductoCreditoRestriccionResultado> ResolverAsync(
            IEnumerable<int> productoIds,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(_resultado);
    }

    private sealed class InMemoryTempDataProvider : ITempDataProvider
    {
        public IDictionary<string, object> LoadTempData(HttpContext context) => new Dictionary<string, object>();
        public void SaveTempData(HttpContext context, IDictionary<string, object> values)
        {
        }
    }
}
