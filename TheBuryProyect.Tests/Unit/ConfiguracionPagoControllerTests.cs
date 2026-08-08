using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Reflection;
using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.Extensions.Logging.Abstractions;
using TheBuryProject.Controllers;
using TheBuryProject.Filters;
using TheBuryProject.Helpers;
using TheBuryProject.Models.DTOs;
using TheBuryProject.Models.Entities;
using TheBuryProject.Models.Enums;
using TheBuryProject.Services;
using TheBuryProject.Services.Exceptions;
using TheBuryProject.Services.Interfaces;
using TheBuryProject.Services.Models;
using TheBuryProject.ViewModels;
using TheBuryProject.ViewModels.Punitorio;

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
    public async Task CreditoPersonal_Get_CargaLimitesCanonicosPorPuntaje()
    {
        var controller = CrearController(new FakeConfiguracionPagoGlobalAdminService());

        var result = await controller.CreditoPersonal();

        var view = Assert.IsType<ViewResult>(result);
        Assert.Equal("CreditoPersonal_tw", view.ViewName);
        var model = Assert.IsType<CreditoPersonalConfigViewModel>(view.Model);
        Assert.Equal(Enumerable.Range(0, 6), model.LimitesPorPuntaje.Select(l => l.Puntaje));
        Assert.Equal(200_000m, model.LimitesPorPuntaje.Single(l => l.Puntaje == 0).LimiteMonto);
        Assert.Empty(model.MontosPorPuntaje);
        Assert.Null(model.ScoringThresholds);
    }

    // -------------------------------------------------------------------------
    // ML4 — Fase 6/9: validación backend autoritativa. El controller rechaza un plan activo
    // sin porcentaje explícito ANTES de llamar al service (gate temprano, evita guardar
    // parcialmente Defaults/Perfiles cuando la tabla de cuotas es inválida) — independiente de
    // que el service (mockeado acá) también valide lo mismo por su cuenta.
    // -------------------------------------------------------------------------

    [Fact]
    public async Task CreditoPersonal_Post_PlanActivoSinPorcentaje_RechazaYNoRedirige()
    {
        var controller = CrearController(new FakeConfiguracionPagoGlobalAdminService());
        var config = new CreditoPersonalConfigViewModel
        {
            DefaultsGlobales = new DefaultsGlobalesViewModel { MinCuotas = 1, MaxCuotas = 24 },
            CuotasCreditoPersonal =
            [
                new CuotaCreditoPersonalViewModel { CantidadCuotas = 6, TasaMensual = null, Activo = true }
            ]
        };

        var result = await controller.CreditoPersonal(
            config, null, null, null, null, null, null, true, null, null, null, true, null, null);

        var view = Assert.IsType<ViewResult>(result);
        Assert.Equal("CreditoPersonal_tw", view.ViewName);
        Assert.False(controller.ModelState.IsValid);
        Assert.Contains(
            controller.ModelState[nameof(CreditoPersonalConfigViewModel.CuotasCreditoPersonal)]!.Errors,
            e => e.ErrorMessage.Contains("recargo total explicito", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task CreditoPersonal_Post_PlanActivoConCeroPorciento_NoRechaza()
    {
        var controller = CrearController(new FakeConfiguracionPagoGlobalAdminService());
        var config = new CreditoPersonalConfigViewModel
        {
            DefaultsGlobales = new DefaultsGlobalesViewModel { MinCuotas = 1, MaxCuotas = 24 },
            CuotasCreditoPersonal =
            [
                new CuotaCreditoPersonalViewModel { CantidadCuotas = 6, TasaMensual = 0m, Activo = true }
            ]
        };

        var result = await controller.CreditoPersonal(
            config, null, null, null, null, null, null, true, null, null, null, true, null, null);

        Assert.IsType<RedirectToActionResult>(result);
    }

    // -------------------------------------------------------------------------
    // Micro-lote 5: recargo TOTAL en la sección #s2 de Crédito Personal.
    // -------------------------------------------------------------------------

    [Fact]
    public void DefaultsGlobalesViewModel_TasaMensualNegativa_FallaValidacion()
    {
        var vm = new DefaultsGlobalesViewModel { TasaMensual = -5m, GastosAdministrativos = 0m, MinCuotas = 1, MaxCuotas = 1 };
        var resultados = new List<ValidationResult>();

        var valido = Validator.TryValidateObject(vm, new ValidationContext(vm), resultados, validateAllProperties: true);

        Assert.False(valido);
        Assert.Contains(resultados, r => r.MemberNames.Contains(nameof(DefaultsGlobalesViewModel.TasaMensual)));
    }

    [Fact]
    public void PreviewRecargoCreditoPersonal_CoincideConSimularPlanCredito()
    {
        var controller = CrearController(new FakeConfiguracionPagoGlobalAdminService());
        var financial = new FinancialCalculationService();

        var resultado = Assert.IsType<JsonResult>(controller.PreviewRecargoCreditoPersonal(6, 10m));
        var esperado = financial.SimularPlanCredito(100_000m, 0m, 6, 10m, 0m, DateTime.Today.AddMonths(1));

        Assert.Equal(esperado.MontoFinanciado, GetJsonProp<decimal>(resultado.Value!, "saldoAFinanciar"));
        Assert.Equal(esperado.InteresTotal, GetJsonProp<decimal>(resultado.Value!, "recargoTotal"));
        Assert.Equal(esperado.TotalAPagar, GetJsonProp<decimal>(resultado.Value!, "totalFinanciado"));
        Assert.Equal(esperado.CuotaEstimada, GetJsonProp<decimal>(resultado.Value!, "cuotaEstimada"));
        Assert.Equal(esperado.Cuotas.Select(c => c.Total), GetCuotasVector(resultado.Value!));
    }

    [Fact]
    public void PreviewRecargoCreditoPersonal_RecargoCero_DevuelveCeroNoRechaza()
    {
        var controller = CrearController(new FakeConfiguracionPagoGlobalAdminService());

        var resultado = Assert.IsType<JsonResult>(controller.PreviewRecargoCreditoPersonal(3, 0m));

        Assert.Equal(0m, GetJsonProp<decimal>(resultado.Value!, "recargoTotal"));
    }

    [Theory]
    [InlineData(0, 10)]
    [InlineData(-1, 10)]
    [InlineData(6, -1)]
    public void PreviewRecargoCreditoPersonal_ParametrosInvalidos_Rechaza(int cuotas, decimal porcentaje)
    {
        var controller = CrearController(new FakeConfiguracionPagoGlobalAdminService());

        var resultado = controller.PreviewRecargoCreditoPersonal(cuotas, porcentaje);

        Assert.IsType<BadRequestObjectResult>(resultado);
    }

    // -------------------------------------------------------------------------
    // Corrección post-ML5: el preview debe exponer el vector exacto de cuotas
    // (misma fuente que persiste el guardado real) para que el front nunca muestre
    // "cuota × cantidad" cuando esa multiplicación no cierra contra el total
    // financiado (el residuo de redondeo cae siempre en la última cuota).
    // -------------------------------------------------------------------------

    [Fact]
    public void PreviewRecargoCreditoPersonal_ResiduoNoDivisible_UltimaCuotaAbsorbeElResto()
    {
        var controller = CrearController(new FakeConfiguracionPagoGlobalAdminService());

        // 100.000 / 12 cuotas / 0 % -> 100.000 no divide exacto entre 12: la cuota
        // regular y la última NO pueden ser iguales (caso del reporte del usuario).
        var resultado = Assert.IsType<JsonResult>(controller.PreviewRecargoCreditoPersonal(12, 0m));
        var cuotas = GetCuotasVector(resultado.Value!);

        Assert.Equal(12, cuotas.Count);
        Assert.All(cuotas.Take(11), t => Assert.Equal(8333.33m, t));
        Assert.Equal(8333.37m, cuotas[11]);

        // La multiplicación ingenua de la cuota regular por la cantidad NO coincide
        // con el total financiado: por eso el front no puede usarla para mostrar el
        // desglose, tiene que leer el vector.
        var totalFinanciado = GetJsonProp<decimal>(resultado.Value!, "totalFinanciado");
        Assert.NotEqual(totalFinanciado, cuotas[0] * 12);
        Assert.Equal(totalFinanciado, cuotas.Sum());
    }

    [Fact]
    public void PreviewRecargoCreditoPersonal_ResiduoDivisibleExacto_TodasLasCuotasSonIguales()
    {
        var controller = CrearController(new FakeConfiguracionPagoGlobalAdminService());

        // 100.000 / 12 cuotas / 8 % -> total financiado 108.000, divide exacto por 12.
        var resultado = Assert.IsType<JsonResult>(controller.PreviewRecargoCreditoPersonal(12, 8m));
        var cuotas = GetCuotasVector(resultado.Value!);

        Assert.Equal(12, cuotas.Count);
        Assert.All(cuotas, t => Assert.Equal(9000m, t));
        Assert.Equal(108_000m, GetJsonProp<decimal>(resultado.Value!, "totalFinanciado"));
        Assert.Equal(cuotas[0], cuotas[^1]);
    }

    private static List<decimal> GetCuotasVector(object jsonValue)
    {
        var cuotas = (System.Collections.IEnumerable)GetJsonProp<object>(jsonValue, "cuotas");
        return cuotas.Cast<object>().Select(c => GetJsonProp<decimal>(c, "total")).ToList();
    }

    private static T GetJsonProp<T>(object jsonValue, string propertyName)
    {
        var prop = jsonValue.GetType().GetProperty(propertyName)
            ?? throw new InvalidOperationException($"El JSON de preview no expone la propiedad '{propertyName}'.");
        return (T)prop.GetValue(jsonValue)!;
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

    // -------------------------------------------------------------------------
    // PUN-ML8: UI administrativa de ConfiguracionPunitorio (pestaña "s7" de CreditoPersonal).
    // -------------------------------------------------------------------------

    [Fact]
    public void CrearVersionPunitorio_UsaModuloCanonicoManagepunitorioYViewpunitorio()
    {
        var method = typeof(ConfiguracionPagoController).GetMethod(nameof(ConfiguracionPagoController.CrearVersionPunitorio));
        var attrs = method?.GetCustomAttributes<PermisoRequeridoAttribute>().ToList();

        Assert.NotNull(attrs);
        Assert.Contains(attrs!, a => a.Modulo == "configuracion" && a.Accion == "managepunitorio");
        Assert.Contains(attrs!, a => a.Modulo == "configuracion" && a.Accion == "viewpunitorio");
    }

    [Fact]
    public void PreviewPunitorio_UsaModuloCanonicoViewpunitorio()
    {
        var method = typeof(ConfiguracionPagoController).GetMethod(nameof(ConfiguracionPagoController.PreviewPunitorio));
        var attr = method?.GetCustomAttribute<PermisoRequeridoAttribute>();

        Assert.NotNull(attr);
        Assert.Equal("configuracion", attr!.Modulo);
        Assert.Equal("viewpunitorio", attr.Accion);
    }

    private static CrearConfiguracionPunitorioViewModel FormValido(DateOnly? vigenteDesde = null, string? motivo = "Ajuste de tasa") => new()
    {
        VigenteDesde = vigenteDesde ?? new DateOnly(2026, 6, 1), // futura respecto al reloj fake (2026-01-01)
        Activa = true,
        Porcentaje = 10m,
        PeriodoDias = 20,
        DiasGracia = 5,
        MotivoCambio = motivo
    };

    [Fact]
    public async Task CrearVersionPunitorio_ModelStateInvalido_PreservaFormYActivaTabS7()
    {
        var punitorioService = new FakeConfiguracionPunitorioService();
        var controller = CrearController(new FakeConfiguracionPagoGlobalAdminService(), punitorioService,
            user: UsuarioCon("configuracion.managepunitorio", "configuracion.viewpunitorio"));
        controller.ModelState.AddModelError("Porcentaje", "forzado para el test");

        var form = FormValido();
        var result = await controller.CrearVersionPunitorio(form, null);

        var view = Assert.IsType<ViewResult>(result);
        Assert.Equal("CreditoPersonal_tw", view.ViewName);
        Assert.Equal("s7", controller.ViewData["ActiveSection"]);
        var model = Assert.IsType<CreditoPersonalConfigViewModel>(view.Model);
        Assert.Same(form, model.Punitorios!.CrearForm);
        Assert.Null(punitorioService.UltimoComando); // el servicio nunca se llegó a invocar
    }

    [Fact]
    public async Task CrearVersionPunitorio_FechaRetroactiva_SinMotivo_SeRechaza()
    {
        var punitorioService = new FakeConfiguracionPunitorioService();
        var controller = CrearController(new FakeConfiguracionPagoGlobalAdminService(), punitorioService,
            reloj: new RelojComercialFake(new DateOnly(2026, 1, 1)),
            user: UsuarioCon("configuracion.managepunitorio", "configuracion.viewpunitorio", "configuracion.retroactivepunitorio"));

        var form = FormValido(vigenteDesde: new DateOnly(2025, 6, 1), motivo: null); // anterior al reloj

        var result = await controller.CrearVersionPunitorio(form, null);

        Assert.IsType<ViewResult>(result);
        Assert.False(controller.ModelState.IsValid);
        Assert.Null(punitorioService.UltimoComando);
    }

    [Fact]
    public async Task CrearVersionPunitorio_FechaRetroactiva_SinPermiso_SeRechaza()
    {
        var punitorioService = new FakeConfiguracionPunitorioService();
        var controller = CrearController(new FakeConfiguracionPagoGlobalAdminService(), punitorioService,
            reloj: new RelojComercialFake(new DateOnly(2026, 1, 1)),
            // tiene permiso para gestionar, pero NO el permiso específico de retroactividad
            user: UsuarioCon("configuracion.managepunitorio", "configuracion.viewpunitorio"));

        var form = FormValido(vigenteDesde: new DateOnly(2025, 6, 1), motivo: "Correccion retroactiva");

        var result = await controller.CrearVersionPunitorio(form, null);

        Assert.IsType<ViewResult>(result);
        Assert.False(controller.ModelState.IsValid);
        Assert.Null(punitorioService.UltimoComando);
    }

    [Fact]
    public async Task CrearVersionPunitorio_FechaRetroactiva_ConMotivoYPermiso_ConstruyeComandoAutorizado()
    {
        var punitorioService = new FakeConfiguracionPunitorioService();
        var controller = CrearController(new FakeConfiguracionPagoGlobalAdminService(), punitorioService,
            reloj: new RelojComercialFake(new DateOnly(2026, 1, 1)),
            user: UsuarioCon("configuracion.managepunitorio", "configuracion.viewpunitorio", "configuracion.retroactivepunitorio"));

        var form = FormValido(vigenteDesde: new DateOnly(2025, 6, 1), motivo: "Correccion retroactiva");

        var result = await controller.CrearVersionPunitorio(form, null);

        Assert.IsType<RedirectResult>(result);
        Assert.NotNull(punitorioService.UltimoComando);
        Assert.True(punitorioService.UltimoComando!.AplicacionRetroactiva);
        Assert.True(punitorioService.UltimoComando.AutorizadoParaRetroactivo);
        Assert.True(punitorioService.UltimoComando.ProrrateoDiario);
    }

    [Fact]
    public async Task CrearVersionPunitorio_FechaFutura_NuncaMarcaRetroactivaAunqueTengaElPermiso()
    {
        var punitorioService = new FakeConfiguracionPunitorioService();
        var controller = CrearController(new FakeConfiguracionPagoGlobalAdminService(), punitorioService,
            reloj: new RelojComercialFake(new DateOnly(2026, 1, 1)),
            user: UsuarioCon("configuracion.managepunitorio", "configuracion.viewpunitorio", "configuracion.retroactivepunitorio"));

        var form = FormValido(vigenteDesde: new DateOnly(2026, 6, 1));

        var result = await controller.CrearVersionPunitorio(form, null);

        Assert.IsType<RedirectResult>(result);
        Assert.NotNull(punitorioService.UltimoComando);
        Assert.False(punitorioService.UltimoComando!.AplicacionRetroactiva);
        Assert.False(punitorioService.UltimoComando.AutorizadoParaRetroactivo);
        Assert.True(punitorioService.UltimoComando.ProrrateoDiario);
    }

    [Fact]
    public async Task CrearVersionPunitorio_ProrrateoDiarioSiempreTrue_NoHayCampoQueLoCambie()
    {
        // No existe ningún campo bindable en CrearConfiguracionPunitorioViewModel para esto:
        // este test documenta la garantía, no la deriva de un input.
        Assert.DoesNotContain(
            typeof(CrearConfiguracionPunitorioViewModel).GetProperties(),
            p => p.Name == "ProrrateoDiario" || p.Name == "AplicacionRetroactiva" || p.Name == "AutorizadoParaRetroactivo");
    }

    [Fact]
    public async Task CrearVersionPunitorio_ServicioRechaza_AgregaModelErrorYVuelveAMostrarS7()
    {
        var punitorioService = new FakeConfiguracionPunitorioService
        {
            CrearHandler = _ => throw new ConfiguracionPunitorioRechazadaException(
                MotivoRechazoConfiguracionPunitorio.Conflicto, "Ya existe una version con esa vigencia.")
        };
        var controller = CrearController(new FakeConfiguracionPagoGlobalAdminService(), punitorioService,
            user: UsuarioCon("configuracion.managepunitorio", "configuracion.viewpunitorio"));

        var result = await controller.CrearVersionPunitorio(FormValido(), null);

        var view = Assert.IsType<ViewResult>(result);
        Assert.Equal("CreditoPersonal_tw", view.ViewName);
        Assert.Equal("s7", controller.ViewData["ActiveSection"]);
        Assert.False(controller.ModelState.IsValid);
        Assert.Contains(controller.ModelState[string.Empty]!.Errors, e => e.ErrorMessage.Contains("Ya existe una version"));
    }

    [Fact]
    public async Task CrearVersionPunitorio_Exito_RedirigeConFragmentoS7YMensajeSuccess()
    {
        var punitorioService = new FakeConfiguracionPunitorioService();
        var controller = CrearController(new FakeConfiguracionPagoGlobalAdminService(), punitorioService,
            user: UsuarioCon("configuracion.managepunitorio", "configuracion.viewpunitorio"));

        var result = await controller.CrearVersionPunitorio(FormValido(), null);

        var redirect = Assert.IsType<RedirectResult>(result);
        Assert.EndsWith("#s7", redirect.Url);
        Assert.Equal("Nueva versión de punitorios creada correctamente.", controller.TempData["Success"]);
    }

    [Theory]
    [InlineData(0, 10, 5)]
    [InlineData(-1, 10, 5)]
    [InlineData(10, 0, -1)]
    public void PreviewPunitorio_ParametrosInvalidos_Rechaza(int periodoDias, decimal porcentaje, int diasGracia)
    {
        var controller = CrearController(new FakeConfiguracionPagoGlobalAdminService());

        var result = controller.PreviewPunitorio(porcentaje, periodoDias, diasGracia);

        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public void PreviewPunitorio_DentroDeGracia_DevuelveImporteCero()
    {
        var controller = CrearController(new FakeConfiguracionPagoGlobalAdminService(),
            reloj: new RelojComercialFake(new DateOnly(2026, 1, 1)));

        var resultado = Assert.IsType<JsonResult>(controller.PreviewPunitorio(porcentaje: 10m, periodoDias: 20, diasGracia: 5));
        var preview = Assert.IsType<PunitorioPreviewViewModel>(resultado.Value);

        var puntoEnGracia = preview.Puntos[0]; // "a los G dias"
        Assert.Equal(0m, puntoEnGracia.Importe);
    }

    [Fact]
    public void PreviewPunitorio_PrimerDiaPosGracia_DevuelveImporteMayorACero()
    {
        var controller = CrearController(new FakeConfiguracionPagoGlobalAdminService(),
            reloj: new RelojComercialFake(new DateOnly(2026, 1, 1)));

        var resultado = Assert.IsType<JsonResult>(controller.PreviewPunitorio(porcentaje: 10m, periodoDias: 20, diasGracia: 5));
        var preview = Assert.IsType<PunitorioPreviewViewModel>(resultado.Value);

        var puntoPosGracia = preview.Puntos[1]; // "primer dia posterior a la gracia"
        Assert.True(puntoPosGracia.Importe > 0m);
        Assert.Equal(100_000m, preview.MontoReferencia);
    }

    private static ConfiguracionPagoController CrearController(
        FakeConfiguracionPagoGlobalAdminService adminService,
        IConfiguracionPunitorioService? punitorioService = null,
        IRelojComercial? reloj = null,
        ClaimsPrincipal? user = null)
    {
        var httpContext = new DefaultHttpContext();
        if (user != null)
            httpContext.User = user;

        return new ConfiguracionPagoController(
            new FakeConfiguracionPagoService(),
            adminService,
            new FakeClienteAptitudService(),
            new FakeCreditoDisponibleService(),
            punitorioService ?? new FakeConfiguracionPunitorioService(),
            new PunitorioCalculator(),
            reloj ?? new RelojComercialFake(new DateOnly(2026, 1, 1)),
            NullLogger<ConfiguracionPagoController>.Instance)
        {
            ControllerContext = new ControllerContext { HttpContext = httpContext },
            TempData = new TempDataDictionary(httpContext, new TestTempDataProvider()),
            Url = new StubUrlHelper()
        };
    }

    /// <summary>Mismo stub que <c>VentaControllerCondicionesPagoErrorTests.StubUrlHelper</c> (patrón ya establecido para tests de controller que llaman Url.Action fuera del pipeline HTTP real).</summary>
    private sealed class StubUrlHelper : IUrlHelper
    {
        public ActionContext ActionContext { get; } = new();
        public string? Action(UrlActionContext actionContext) => $"/ConfiguracionPago/{actionContext.Action}";
        public string? Content(string? contentPath) => contentPath;
        public bool IsLocalUrl(string? url) => true;
        public string? Link(string? routeName, object? values) => routeName;
        public string? RouteUrl(UrlRouteContext routeContext) => routeContext.RouteName;
    }

    /// <summary>Principal autenticado con los permisos "Permission" indicados (PUN-ML8), mismo formato que <see cref="Helpers.PermissionAliasHelper.HasPermissionClaim"/> espera.</summary>
    private static ClaimsPrincipal UsuarioCon(params string[] permisos)
    {
        var claims = permisos.Select(p => new Claim("Permission", p)).ToList();
        return new ClaimsPrincipal(new ClaimsIdentity(claims, "TestAuth"));
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

        public Task<bool> EliminarTarjetaGlobalAsync(int id) => Task.FromResult(true);

        public Task<bool> EliminarMedioPagoAsync(int id) => Task.FromResult(true);

        public Task<bool> EditarMedioPagoAsync(int id, MedioPagoGlobalEditViewModel command) => Task.FromResult(true);

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
        public Task<List<MontoPorPuntajeCreditoViewModel>> GetMontosPorPuntajeAsync() => Task.FromResult(new List<MontoPorPuntajeCreditoViewModel>());
        public Task<(bool Ok, List<string> Errores)> GuardarMontosPorPuntajeAsync(List<MontoPorPuntajeCreditoViewModel> items, string usuario) => Task.FromResult((true, new List<string>()));
        public Task<List<CuotaCreditoPersonalViewModel>> GetCuotasCreditoPersonalAsync() => Task.FromResult(new List<CuotaCreditoPersonalViewModel>());
        public Task<List<CuotaCreditoPersonalViewModel>> GetCuotasCreditoPersonalActivasAsync() => Task.FromResult(new List<CuotaCreditoPersonalViewModel>());
        public async Task<PlanesCreditoPersonalResultado> ResolverPlanesCreditoPersonalAsync(IEnumerable<int> productoIds) => PlanesCreditoPersonalStub.DesdeGlobales(await GetCuotasCreditoPersonalActivasAsync());
        public Task<(bool Ok, List<string> Errores)> GuardarCuotasCreditoPersonalAsync(List<CuotaCreditoPersonalViewModel> items, string usuario) => Task.FromResult((true, new List<string>()));
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

    private sealed class FakeCreditoDisponibleService : ICreditoDisponibleService
    {
        public Task<decimal> ObtenerLimitePorPuntajeAsync(int puntaje, CancellationToken cancellationToken = default) => Task.FromResult(0m);
        public Task<decimal> CalcularSaldoVigenteAsync(int clienteId, CancellationToken cancellationToken = default) => Task.FromResult(0m);
        public Task<CreditoDisponibleResultado> CalcularDisponibleAsync(int clienteId, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<(bool Ok, List<string> Errores)> GuardarLimitesPorPuntajeAsync(IReadOnlyList<(int Puntaje, decimal LimiteMonto, bool Activo)> items, string usuario)
            => Task.FromResult((true, new List<string>()));

        public Task<List<PuntajeCreditoLimite>> GetAllLimitesPorPuntajeAsync()
            => Task.FromResult(Enumerable.Range(0, 6)
                .Select(p => new PuntajeCreditoLimite
                {
                    Id = p + 1,
                    Puntaje = p,
                    LimiteMonto = p == 0 ? 200_000m : p * 100_000m,
                    Activo = true
                })
                .ToList());
    }

    private sealed class FakeConfiguracionPunitorioService : IConfiguracionPunitorioService
    {
        public ConfiguracionPunitorioVigente Vigente { get; set; } = ConfiguracionPunitorioVigente.SinConfiguracion;
        public List<ConfiguracionPunitorio> Historial { get; set; } = new();
        public ConfiguracionPunitorioComando? UltimoComando { get; private set; }
        public Func<ConfiguracionPunitorioComando, ConfiguracionPunitorio>? CrearHandler { get; set; }

        public Task<ConfiguracionPunitorioVigente> ObtenerVigenteAsync(DateOnly fechaComercial, CancellationToken cancellationToken = default) =>
            Task.FromResult(Vigente);

        public Task<ConfiguracionPunitorio> CrearNuevaVersionAsync(ConfiguracionPunitorioComando comando, CancellationToken cancellationToken = default)
        {
            UltimoComando = comando;

            if (CrearHandler != null)
                return Task.FromResult(CrearHandler(comando));

            return Task.FromResult(new ConfiguracionPunitorio
            {
                Id = 1,
                Porcentaje = comando.Porcentaje,
                PeriodoDias = comando.PeriodoDias,
                DiasGracia = comando.DiasGracia,
                ProrrateoDiario = comando.ProrrateoDiario,
                AplicacionRetroactiva = comando.AplicacionRetroactiva,
                VigenteDesde = comando.VigenteDesde,
                Activa = comando.Activa,
                MotivoCambio = comando.MotivoCambio
            });
        }

        public Task<IReadOnlyList<ConfiguracionPunitorio>> ListarHistorialAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ConfiguracionPunitorio>>(Historial);
    }

    private sealed class TestTempDataProvider : ITempDataProvider
    {
        public IDictionary<string, object> LoadTempData(HttpContext context) => new Dictionary<string, object>();
        public void SaveTempData(HttpContext context, IDictionary<string, object> values) { }
    }
}
