using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Reflection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.Extensions.Logging.Abstractions;
using TheBuryProject.Controllers;
using TheBuryProject.Filters;
using TheBuryProject.Helpers;
using TheBuryProject.Models.Entities;
using TheBuryProject.Models.Enums;
using TheBuryProject.Services.Interfaces;
using TheBuryProject.Services.Models;
using TheBuryProject.ViewModels;

namespace TheBuryProject.Tests.Unit;

[Trait("Category", "PagosAbm")]
public sealed class ConfiguracionPagoControllerTests
{
    // -------------------------------------------------------------------------
    // PAGOS-ABM-7A: Auditoría de módulo canónico de permisos
    // El seeder define "configuracion" (singular). El controller debe coincidir.
    // -------------------------------------------------------------------------

    [Fact]
    public void ConfiguracionPagoController_AtributoClase_UsaModuloCanonicoSingular()
    {
        var attr = typeof(ConfiguracionPagoController)
            .GetCustomAttribute<PermisoRequeridoAttribute>();

        Assert.NotNull(attr);
        Assert.Equal("configuracion", attr!.Modulo);
        Assert.Equal("view", attr.Accion);
    }

    [Theory]
    [InlineData(nameof(ConfiguracionPagoController.CrearTarjetaGlobal))]
    [InlineData(nameof(ConfiguracionPagoController.EditarTarjetaGlobal))]
    [InlineData(nameof(ConfiguracionPagoController.CambiarEstadoTarjetaGlobal))]
    [InlineData(nameof(ConfiguracionPagoController.CrearPlanGlobal))]
    [InlineData(nameof(ConfiguracionPagoController.EditarPlanGlobal))]
    [InlineData(nameof(ConfiguracionPagoController.CambiarEstadoPlanGlobal))]
    public void ConfiguracionPagoController_AccionesEscritura_UsanModuloCanonicoUpdate(string methodName)
    {
        var method = typeof(ConfiguracionPagoController).GetMethods()
            .First(m => m.Name == methodName && m.GetCustomAttribute<HttpPostAttribute>() != null);

        var attr = method.GetCustomAttribute<PermisoRequeridoAttribute>();

        Assert.NotNull(attr);
        Assert.Equal("configuracion", attr!.Modulo);
        Assert.Equal("update", attr.Accion);
    }

    [Fact]
    public void ConfiguracionPagoController_CreditoPersonalPost_UsaModuloCanonicoUpdate()
    {
        var methods = typeof(ConfiguracionPagoController).GetMethods()
            .Where(m => m.Name == nameof(ConfiguracionPagoController.CreditoPersonal)
                        && m.GetCustomAttribute<HttpPostAttribute>() != null)
            .ToList();

        Assert.Single(methods);
        var attr = methods[0].GetCustomAttribute<PermisoRequeridoAttribute>();
        Assert.NotNull(attr);
        Assert.Equal("configuracion", attr!.Modulo);
        Assert.Equal("update", attr.Accion);
    }

    [Fact]
    public void ConfiguracionPagoController_NoContieneModuloPluralEnAtributos()
    {
        var classAttrs = typeof(ConfiguracionPagoController)
            .GetCustomAttributes<PermisoRequeridoAttribute>()
            .Where(a => a.Modulo == "configuraciones");

        var methodAttrs = typeof(ConfiguracionPagoController)
            .GetMethods()
            .SelectMany(m => m.GetCustomAttributes<PermisoRequeridoAttribute>())
            .Where(a => a.Modulo == "configuraciones");

        Assert.Empty(classAttrs);
        Assert.Empty(methodAttrs);
    }


    [Fact]
    public async Task MediosPago_DevuelveVistaAdminConMediosTarjetasYPlanes()
    {
        var adminService = new FakeConfiguracionPagoGlobalAdminService
        {
            AdminModel = new ConfiguracionPagoGlobalAdminViewModel
            {
                Medios =
                [
                    new MedioPagoGlobalAdminViewModel
                    {
                        Id = 1,
                        TipoPago = TipoPago.TarjetaCredito,
                        Nombre = "Tarjeta credito",
                        Activo = true,
                        Tarjetas =
                        [
                            new TarjetaGlobalAdminViewModel
                            {
                                Id = 10,
                                ConfiguracionPagoId = 1,
                                Nombre = "Visa",
                                TipoTarjeta = TipoTarjeta.Credito,
                                Activa = false
                            }
                        ],
                        Planes =
                        [
                            new PlanPagoGlobalAdminViewModel
                            {
                                Id = 20,
                                ConfiguracionPagoId = 1,
                                TipoPago = TipoPago.TarjetaCredito,
                                CantidadCuotas = 3,
                                Activo = false,
                                AjustePorcentaje = 5m
                            }
                        ]
                    }
                ]
            }
        };
        var controller = CrearController(adminService);

        var result = await controller.MediosPago();

        var view = Assert.IsType<ViewResult>(result);
        Assert.Equal("MediosPago_tw", view.ViewName);
        var model = Assert.IsType<ConfiguracionPagoGlobalAdminViewModel>(view.Model);
        var medio = Assert.Single(model.Medios);
        Assert.Single(medio.Tarjetas);
        Assert.Single(medio.Planes);
    }

    [Fact]
    public async Task CrearPlanGlobal_PostValido_RedireccionaAMediosPago()
    {
        var adminService = new FakeConfiguracionPagoGlobalAdminService();
        var controller = CrearController(adminService);
        var command = new PlanPagoGlobalCommandViewModel
        {
            ConfiguracionPagoId = 1,
            CantidadCuotas = 3,
            AjustePorcentaje = 10m,
            Activo = true
        };

        var result = await controller.CrearPlanGlobal(command);

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal(nameof(ConfiguracionPagoController.MediosPago), redirect.ActionName);
        Assert.True(adminService.CrearPlanInvocado);
        Assert.Equal("Plan global creado correctamente.", controller.TempData["Success"]);
    }

    [Fact]
    public void PlanPagoGlobalCommandViewModel_AjustePorcentaje_UsaDecimalModelBinder()
    {
        var property = typeof(PlanPagoGlobalCommandViewModel).GetProperty(nameof(PlanPagoGlobalCommandViewModel.AjustePorcentaje));

        var attribute = property?.GetCustomAttributes(typeof(ModelBinderAttribute), inherit: false)
            .Cast<ModelBinderAttribute>()
            .SingleOrDefault();

        Assert.NotNull(attribute);
        Assert.Equal(typeof(DecimalModelBinder), attribute!.BinderType);
    }

    [Fact]
    public void PlanPagoGlobalCommandViewModel_ValidacionEsAr_AceptaAjustePorcentajeValido()
    {
        var culturaOriginal = CultureInfo.CurrentCulture;
        var uiCulturaOriginal = CultureInfo.CurrentUICulture;

        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("es-AR");
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("es-AR");

            var command = new PlanPagoGlobalCommandViewModel
            {
                ConfiguracionPagoId = 1,
                CantidadCuotas = 1,
                AjustePorcentaje = -5m,
                Activo = true
            };
            var resultados = new List<ValidationResult>();

            var valido = Validator.TryValidateObject(
                command,
                new ValidationContext(command),
                resultados,
                validateAllProperties: true);

            Assert.True(valido);
            Assert.Empty(resultados);
        }
        finally
        {
            CultureInfo.CurrentCulture = culturaOriginal;
            CultureInfo.CurrentUICulture = uiCulturaOriginal;
        }
    }

    private static ConfiguracionPagoController CrearController(FakeConfiguracionPagoGlobalAdminService adminService)
    {
        var httpContext = new DefaultHttpContext();
        return new ConfiguracionPagoController(
            new FakeConfiguracionPagoService(),
            adminService,
            new FakeClienteAptitudService(),
            NullLogger<ConfiguracionPagoController>.Instance)
        {
            ControllerContext = new ControllerContext { HttpContext = httpContext },
            TempData = new TempDataDictionary(httpContext, new TestTempDataProvider())
        };
    }

    private sealed class FakeConfiguracionPagoGlobalAdminService : IConfiguracionPagoGlobalAdminService
    {
        public ConfiguracionPagoGlobalAdminViewModel AdminModel { get; init; } = new();
        public bool CrearPlanInvocado { get; private set; }

        public Task<ConfiguracionPagoGlobalAdminViewModel> ObtenerAdminGlobalAsync() => Task.FromResult(AdminModel);

        public Task<IReadOnlyList<TarjetaGlobalAdminViewModel>> ListarTarjetasGlobalesAsync(int? configuracionPagoId = null) =>
            Task.FromResult<IReadOnlyList<TarjetaGlobalAdminViewModel>>(AdminModel.Medios
                .Where(m => !configuracionPagoId.HasValue || m.Id == configuracionPagoId.Value)
                .SelectMany(m => m.Tarjetas)
                .ToList());

        public Task<TarjetaGlobalAdminViewModel?> ObtenerTarjetaGlobalAsync(int id) =>
            Task.FromResult(AdminModel.Medios.SelectMany(m => m.Tarjetas).FirstOrDefault(t => t.Id == id));

        public Task<TarjetaGlobalAdminViewModel> CrearTarjetaGlobalAsync(TarjetaGlobalCommandViewModel command) =>
            Task.FromResult(new TarjetaGlobalAdminViewModel
            {
                Id = 1,
                ConfiguracionPagoId = command.ConfiguracionPagoId,
                Nombre = command.NombreTarjeta,
                TipoTarjeta = command.TipoTarjeta,
                Activa = command.Activa,
                Observaciones = command.Observaciones
            });

        public Task<TarjetaGlobalAdminViewModel?> ActualizarTarjetaGlobalAsync(int id, TarjetaGlobalCommandViewModel command) =>
            Task.FromResult<TarjetaGlobalAdminViewModel?>(new TarjetaGlobalAdminViewModel
            {
                Id = id,
                ConfiguracionPagoId = command.ConfiguracionPagoId,
                Nombre = command.NombreTarjeta,
                TipoTarjeta = command.TipoTarjeta,
                Activa = command.Activa,
                Observaciones = command.Observaciones
            });

        public Task<bool> CambiarEstadoTarjetaGlobalAsync(int id, bool activa) => Task.FromResult(true);

        public Task<PlanPagoGlobalAdminViewModel> CrearPlanGlobalAsync(PlanPagoGlobalCommandViewModel command)
        {
            CrearPlanInvocado = true;
            return Task.FromResult(new PlanPagoGlobalAdminViewModel
            {
                Id = 1,
                ConfiguracionPagoId = command.ConfiguracionPagoId,
                ConfiguracionTarjetaId = command.ConfiguracionTarjetaId,
                CantidadCuotas = command.CantidadCuotas,
                Activo = command.Activo,
                AjustePorcentaje = command.AjustePorcentaje
            });
        }

        public Task<PlanPagoGlobalAdminViewModel?> ActualizarPlanGlobalAsync(int id, PlanPagoGlobalCommandViewModel command) =>
            Task.FromResult<PlanPagoGlobalAdminViewModel?>(new PlanPagoGlobalAdminViewModel { Id = id });

        public Task<bool> CambiarEstadoPlanGlobalAsync(int id, bool activo) => Task.FromResult(true);
    }

    private sealed class FakeConfiguracionPagoService : IConfiguracionPagoService
    {
        public Task<List<ConfiguracionPagoViewModel>> GetAllAsync() => Task.FromResult(new List<ConfiguracionPagoViewModel>());
        public Task<ConfiguracionPagoViewModel?> GetByIdAsync(int id) => Task.FromResult<ConfiguracionPagoViewModel?>(null);
        public Task<ConfiguracionPagoViewModel?> GetByTipoPagoAsync(TipoPago tipoPago) => Task.FromResult<ConfiguracionPagoViewModel?>(null);
        public Task<decimal?> ObtenerTasaInteresMensualCreditoPersonalAsync() => Task.FromResult<decimal?>(null);
        public Task<ConfiguracionPagoViewModel> CreateAsync(ConfiguracionPagoViewModel viewModel) => Task.FromResult(viewModel);
        public Task<ConfiguracionPagoViewModel?> UpdateAsync(int id, ConfiguracionPagoViewModel viewModel) => Task.FromResult<ConfiguracionPagoViewModel?>(viewModel);
        public Task<bool> DeleteAsync(int id) => Task.FromResult(true);
        public Task<List<ConfiguracionTarjetaViewModel>> GetTarjetasActivasAsync() => Task.FromResult(new List<ConfiguracionTarjetaViewModel>());
        public Task<List<TarjetaActivaVentaResultado>> GetTarjetasActivasParaVentaAsync() => Task.FromResult(new List<TarjetaActivaVentaResultado>());
        public Task<ConfiguracionTarjetaViewModel?> GetTarjetaByIdAsync(int id) => Task.FromResult<ConfiguracionTarjetaViewModel?>(null);
        public Task<bool> ValidarDescuento(TipoPago tipoPago, decimal descuento) => Task.FromResult(true);
        public Task<decimal> CalcularRecargo(TipoPago tipoPago, decimal monto) => Task.FromResult(0m);
        public Task<List<PerfilCreditoViewModel>> GetPerfilesCreditoAsync() => Task.FromResult(new List<PerfilCreditoViewModel>());
        public Task<List<PerfilCreditoViewModel>> GetPerfilesCreditoActivosAsync() => Task.FromResult(new List<PerfilCreditoViewModel>());
        public Task GuardarCreditoPersonalAsync(CreditoPersonalConfigViewModel config) => Task.CompletedTask;
        public Task<ParametrosCreditoCliente> ObtenerParametrosCreditoClienteAsync(int clienteId, decimal tasaGlobal) => Task.FromResult(new ParametrosCreditoCliente());
        public Task<(int Min, int Max, string Descripcion, string? PerfilNombre)> ResolverRangoCuotasAsync(MetodoCalculoCredito metodo, int? perfilId, int? clienteId)
            => Task.FromResult((1, 24, "Global", (string?)null));
        public Task<MaxCuotasSinInteresResultado?> ObtenerMaxCuotasSinInteresEfectivoAsync(int tarjetaId, IEnumerable<int> productoIds)
            => Task.FromResult<MaxCuotasSinInteresResultado?>(null);
    }

    private sealed class FakeClienteAptitudService : IClienteAptitudService
    {
        public Task<AptitudCrediticiaViewModel> EvaluarAptitudAsync(int clienteId, bool guardarResultado = true) => throw new NotImplementedException();
        public Task<AptitudCrediticiaViewModel> EvaluarAptitudSinGuardarAsync(int clienteId) => throw new NotImplementedException();
        public Task<AptitudCrediticiaViewModel?> GetUltimaEvaluacionAsync(int clienteId) => throw new NotImplementedException();
        public Task<(bool EsApto, string? Motivo)> VerificarAptitudParaMontoAsync(int clienteId, decimal monto) => throw new NotImplementedException();
        public Task<AptitudDocumentacionDetalle> EvaluarDocumentacionAsync(int clienteId) => throw new NotImplementedException();
        public Task<AptitudCupoDetalle> EvaluarCupoAsync(int clienteId) => throw new NotImplementedException();
        public Task<AptitudMoraDetalle> EvaluarMoraAsync(int clienteId) => throw new NotImplementedException();
        public Task<ConfiguracionCredito> GetConfiguracionAsync() => throw new NotImplementedException();
        public Task<ConfiguracionCredito> UpdateConfiguracionAsync(ConfiguracionCreditoViewModel viewModel) => throw new NotImplementedException();
        public Task<(bool EstaConfigurando, string? Mensaje)> VerificarConfiguracionAsync() => throw new NotImplementedException();
        public Task<ScoringThresholdsViewModel> GetScoringThresholdsAsync() => Task.FromResult(new ScoringThresholdsViewModel());
        public Task UpdateScoringThresholdsAsync(ScoringThresholdsViewModel model) => Task.CompletedTask;
        public Task<SemaforoFinancieroViewModel> GetSemaforoFinancieroAsync() => Task.FromResult(new SemaforoFinancieroViewModel());
        public Task UpdateSemaforoFinancieroAsync(SemaforoFinancieroViewModel model) => Task.CompletedTask;
        public Task<bool> AsignarLimiteCreditoAsync(int clienteId, decimal limite, string? motivo = null) => throw new NotImplementedException();
        public Task<decimal> GetCupoDisponibleAsync(int clienteId) => throw new NotImplementedException();
        public Task<decimal> GetCreditoUtilizadoAsync(int clienteId) => throw new NotImplementedException();
    }

    private sealed class TestTempDataProvider : ITempDataProvider
    {
        public IDictionary<string, object> LoadTempData(HttpContext context) => new Dictionary<string, object>();
        public void SaveTempData(HttpContext context, IDictionary<string, object> values) { }
    }
}
