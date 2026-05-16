using System.Reflection;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using TheBuryProject.Controllers;
using TheBuryProject.Filters;
using TheBuryProject.Services.Interfaces;
using TheBuryProject.Services.Models;

namespace TheBuryProject.Tests.Unit;

public sealed class CotizacionApiControllerTests
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    [Fact]
    public async Task Simular_RequestNull_DevuelveBadRequest()
    {
        var controller = CreateController();

        var result = await controller.Simular(null);

        var badRequest = Assert.IsType<BadRequestObjectResult>(result);
        var json = ToJson(badRequest.Value);
        Assert.Equal("El request de cotizacion es obligatorio.", json.RootElement.GetProperty("error").GetString());
    }

    [Fact]
    public async Task Simular_RequestValido_LlamaCalculatorYDevuelveOk()
    {
        var calculator = new StubCotizacionPagoCalculator
        {
            Resultado = ResultadoExitoso()
        };
        var request = RequestValido();
        var controller = CreateController(calculator);

        var result = await controller.Simular(request);

        var ok = Assert.IsType<OkObjectResult>(result);
        var resultado = Assert.IsType<CotizacionSimulacionResultado>(ok.Value);
        Assert.True(resultado.Exitoso);
        Assert.Same(request, calculator.UltimoRequest);
        Assert.Equal(1, calculator.CallCount);
    }

    [Fact]
    public async Task Simular_ResultadoConAdvertencias_DevuelveOkConAdvertencias()
    {
        var controller = CreateController(new StubCotizacionPagoCalculator
        {
            Resultado = ResultadoExitoso(advertencia: "MercadoPago pendiente de mapeo en configuracion de pagos.")
        });

        var result = await controller.Simular(RequestValido());

        var ok = Assert.IsType<OkObjectResult>(result);
        var json = ToJson(ok.Value);
        Assert.True(json.RootElement.GetProperty("exitoso").GetBoolean());
        Assert.Contains(
            "MercadoPago pendiente",
            json.RootElement.GetProperty("advertencias")[0].GetString(),
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Simular_ResultadoNoExitosoFuncional_DevuelveOkConResultado()
    {
        var controller = CreateController(new StubCotizacionPagoCalculator
        {
            Resultado = new CotizacionSimulacionResultado
            {
                Exitoso = false,
                Errores = { "El producto 99 no tiene precio vigente para venta." }
            }
        });

        var result = await controller.Simular(RequestValido());

        var ok = Assert.IsType<OkObjectResult>(result);
        var resultado = Assert.IsType<CotizacionSimulacionResultado>(ok.Value);
        Assert.False(resultado.Exitoso);
        Assert.Contains(resultado.Errores, e => e.Contains("precio vigente", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Simular_ResultadoCreditoPersonal_SerializaCamposDePlanReadonly()
    {
        var controller = CreateController(new StubCotizacionPagoCalculator
        {
            Resultado = ResultadoExitosoConCreditoPersonal()
        });

        var result = await controller.Simular(RequestValido());

        var ok = Assert.IsType<OkObjectResult>(result);
        var json = ToJson(ok.Value);
        var credito = json.RootElement.GetProperty("opcionesPago")
            .EnumerateArray()
            .Single(o => o.GetProperty("medioPago").GetInt32() == (int)CotizacionMedioPagoTipo.CreditoPersonal);
        var plan = credito.GetProperty("planes")[0];

        Assert.Equal(6, plan.GetProperty("cantidadCuotas").GetInt32());
        Assert.Equal(5m, plan.GetProperty("tasaMensual").GetDecimal());
        Assert.Equal(40_000m, plan.GetProperty("costoFinancieroTotal").GetDecimal());
        Assert.Equal("CreditoPersonalReadOnly", plan.GetProperty("tipoCalculo").GetString());
    }

    [Fact]
    public void Simular_NoTocaVentaNiCajaNiStock()
    {
        var constructor = Assert.Single(typeof(CotizacionApiController).GetConstructors());
        var parameterTypes = constructor.GetParameters().Select(p => p.ParameterType).ToArray();

        Assert.Contains(typeof(ICotizacionPagoCalculator), parameterTypes);
        Assert.DoesNotContain(parameterTypes, t => t.Name.Contains("Venta", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(parameterTypes, t => t.Name.Contains("Caja", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(parameterTypes, t => t.Name.Contains("Stock", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Simular_RutaEndpoint_PostApiCotizacionSimular()
    {
        var controllerRoute = typeof(CotizacionApiController).GetCustomAttribute<RouteAttribute>();
        var method = typeof(CotizacionApiController).GetMethod(nameof(CotizacionApiController.Simular));
        var post = method?.GetCustomAttribute<HttpPostAttribute>();

        Assert.Equal("api/cotizacion", controllerRoute?.Template);
        Assert.Equal("simular", post?.Template);
    }

    [Fact]
    public void CotizacionApiController_RequiereAutorizacionYPermisoVentasView()
    {
        Assert.Contains(
            typeof(CotizacionApiController).GetCustomAttributes<AuthorizeAttribute>(),
            a => a.GetType() == typeof(AuthorizeAttribute));

        var permiso = typeof(CotizacionApiController).GetCustomAttribute<PermisoRequeridoAttribute>();
        Assert.NotNull(permiso);
        Assert.Equal("cotizaciones", permiso.Modulo);
        Assert.Equal("view", permiso.Accion);
    }

    private static CotizacionApiController CreateController(StubCotizacionPagoCalculator? calculator = null) =>
        new(calculator ?? new StubCotizacionPagoCalculator(), new StubCotizacionService(), new StubCotizacionConversionService(), NullLogger<CotizacionApiController>.Instance);

    private static CotizacionSimulacionRequest RequestValido() =>
        new()
        {
            Productos =
            {
                new CotizacionProductoRequest
                {
                    ProductoId = 1,
                    Cantidad = 1
                }
            }
        };

    private static CotizacionSimulacionResultado ResultadoExitoso(string? advertencia = null)
    {
        var resultado = new CotizacionSimulacionResultado
        {
            Exitoso = true,
            TotalBase = 100_000m,
            Productos =
            {
                new CotizacionProductoResultado
                {
                    ProductoId = 1,
                    Codigo = "P-1",
                    Nombre = "Producto",
                    Cantidad = 1,
                    PrecioUnitario = 100_000m,
                    Subtotal = 100_000m
                }
            }
        };

        if (!string.IsNullOrWhiteSpace(advertencia))
            resultado.Advertencias.Add(advertencia);

        return resultado;
    }

    private static CotizacionSimulacionResultado ResultadoExitosoConCreditoPersonal()
    {
        var resultado = ResultadoExitoso();
        resultado.OpcionesPago.Add(new CotizacionMedioPagoResultado
        {
            MedioPago = CotizacionMedioPagoTipo.CreditoPersonal,
            NombreMedioPago = "Credito personal",
            Disponible = true,
            Estado = CotizacionOpcionPagoEstado.Disponible,
            Planes =
            {
                new CotizacionPlanPagoResultado
                {
                    Plan = "6 cuotas",
                    CantidadCuotas = 6,
                    TasaMensual = 5m,
                    CostoFinancieroTotal = 40_000m,
                    TipoCalculo = "CreditoPersonalReadOnly",
                    Total = 240_000m,
                    ValorCuota = 40_000m
                }
            }
        });
        return resultado;
    }

    private static JsonDocument ToJson(object? value)
    {
        var json = JsonSerializer.Serialize(value, JsonOptions);
        return JsonDocument.Parse(json);
    }

    private sealed class StubCotizacionPagoCalculator : ICotizacionPagoCalculator
    {
        public CotizacionSimulacionResultado Resultado { get; init; } = ResultadoExitoso();
        public CotizacionSimulacionRequest? UltimoRequest { get; private set; }
        public int CallCount { get; private set; }

        public Task<CotizacionSimulacionResultado> SimularAsync(
            CotizacionSimulacionRequest request,
            CancellationToken cancellationToken = default)
        {
            UltimoRequest = request;
            CallCount++;
            return Task.FromResult(Resultado);
        }
    }

    private sealed class StubCotizacionService : ICotizacionService
    {
        public Task<CotizacionResultado> CrearAsync(CotizacionCrearRequest request, string usuario, CancellationToken cancellationToken = default) =>
            Task.FromResult(new CotizacionResultado { Id = 1, Numero = "COT-TEST" });

        public Task<CotizacionResultado?> ObtenerAsync(int id, CancellationToken cancellationToken = default) =>
            Task.FromResult<CotizacionResultado?>(null);

        public Task<CotizacionListadoResultado> ListarAsync(CotizacionFiltros filtros, CancellationToken cancellationToken = default) =>
            Task.FromResult(new CotizacionListadoResultado());

        public Task<CotizacionCancelacionResultado> CancelarAsync(int id, CotizacionCancelacionRequest request, string usuario, CancellationToken cancellationToken = default) =>
            Task.FromResult(new CotizacionCancelacionResultado { Exitoso = true, CotizacionId = id });

        public Task<CotizacionVencimientoResultado> VencerEmitidasAsync(DateTime fechaReferenciaUtc, string usuario, CancellationToken cancellationToken = default) =>
            Task.FromResult(new CotizacionVencimientoResultado { Exitoso = true });
    }

    private sealed class StubCotizacionConversionService : ICotizacionConversionService
    {
        public CotizacionConversionPreviewResultado PreviewResultado { get; init; } =
            new() { Convertible = true, CotizacionId = 1 };

        public CotizacionConversionResultado ConversionResultado { get; init; } =
            new() { Exitoso = true, CotizacionId = 1, VentaId = 10, NumeroVenta = "COT-202501-1" };

        public Task<CotizacionConversionPreviewResultado> PreviewConversionAsync(int cotizacionId, CancellationToken cancellationToken = default) =>
            Task.FromResult(PreviewResultado);

        public Task<CotizacionConversionResultado> ConvertirAVentaAsync(int cotizacionId, CotizacionConversionRequest request, string usuario, CancellationToken cancellationToken = default) =>
            Task.FromResult(ConversionResultado);
    }
}
