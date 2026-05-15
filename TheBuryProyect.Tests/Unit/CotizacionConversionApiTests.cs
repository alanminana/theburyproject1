using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using TheBuryProject.Controllers;
using TheBuryProject.Models.Enums;
using TheBuryProject.Services.Interfaces;
using TheBuryProject.Services.Models;

namespace TheBuryProject.Tests.Unit;

public sealed class CotizacionConversionApiTests
{
    // ─── PREVIEW ENDPOINT ────────────────────────────────────────────────

    [Fact]
    public async Task PreviewEndpoint_CotizacionConvertible_DevuelveOk()
    {
        var conversionService = new StubConversionService
        {
            PreviewResultado = new CotizacionConversionPreviewResultado
            {
                Convertible = true,
                CotizacionId = 5
            }
        };
        var controller = CreateController(conversionService);

        var result = await controller.ConversionPreview(5);

        var ok = Assert.IsType<OkObjectResult>(result);
        var preview = Assert.IsType<CotizacionConversionPreviewResultado>(ok.Value);
        Assert.True(preview.Convertible);
    }

    [Fact]
    public async Task PreviewEndpoint_CotizacionNoConvertible_DevuelveOkConErrores()
    {
        var conversionService = new StubConversionService
        {
            PreviewResultado = new CotizacionConversionPreviewResultado
            {
                Convertible = false,
                CotizacionId = 5,
                Errores = { "La cotización ya fue convertida a venta." }
            }
        };
        var controller = CreateController(conversionService);

        var result = await controller.ConversionPreview(5);

        var ok = Assert.IsType<OkObjectResult>(result);
        var preview = Assert.IsType<CotizacionConversionPreviewResultado>(ok.Value);
        Assert.False(preview.Convertible);
        Assert.NotEmpty(preview.Errores);
    }

    // ─── CONVERTIR ENDPOINT ───────────────────────────────────────────────

    [Fact]
    public async Task ConvertirEndpoint_ConversionExitosa_DevuelveOk()
    {
        var conversionService = new StubConversionService
        {
            ConversionResultado = new CotizacionConversionResultado
            {
                Exitoso = true,
                CotizacionId = 3,
                VentaId = 42,
                NumeroVenta = "COT-202501-1",
                EstadoVenta = EstadoVenta.Cotizacion
            }
        };
        var controller = CreateController(conversionService);

        var result = await controller.Convertir(3, new CotizacionConversionRequest(), CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        var resultadoOk = Assert.IsType<CotizacionConversionResultado>(ok.Value);
        Assert.True(resultadoOk.Exitoso);
        Assert.Equal(42, resultadoOk.VentaId);
    }

    [Fact]
    public async Task ConvertirEndpoint_ErrorFuncional_DevuelveBadRequest()
    {
        var conversionService = new StubConversionService
        {
            ConversionResultado = new CotizacionConversionResultado
            {
                Exitoso = false,
                CotizacionId = 3,
                Errores = { "La cotización ya fue convertida a venta." }
            }
        };
        var controller = CreateController(conversionService);

        var result = await controller.Convertir(3, new CotizacionConversionRequest(), CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public async Task ConvertirEndpoint_RequestNull_DevuelveBadRequest()
    {
        var controller = CreateController();

        var result = await controller.Convertir(1, null!, CancellationToken.None);

        var bad = Assert.IsType<BadRequestObjectResult>(result);
        Assert.NotNull(bad.Value);
    }

    [Fact]
    public async Task ConvertirEndpoint_LlamaServicioConUsuarioIdentity()
    {
        var conversionService = new StubConversionService
        {
            ConversionResultado = new CotizacionConversionResultado
            {
                Exitoso = true,
                CotizacionId = 7,
                VentaId = 99
            }
        };
        var controller = CreateController(conversionService);

        await controller.Convertir(7, new CotizacionConversionRequest(), CancellationToken.None);

        Assert.Equal(7, conversionService.UltimaCotizacionId);
    }

    // ─── CONSTRUCTOR / RUTAS ─────────────────────────────────────────────

    [Fact]
    public void ConversionService_EstaInyectadoEnController()
    {
        var constructor = Assert.Single(typeof(CotizacionApiController).GetConstructors());
        var paramTypes = constructor.GetParameters().Select(p => p.ParameterType).ToArray();

        Assert.Contains(typeof(ICotizacionConversionService), paramTypes);
    }

    // ─── HELPERS ─────────────────────────────────────────────────────────

    private static CotizacionApiController CreateController(StubConversionService? conversionService = null) =>
        new(
            new StubCalculator(),
            new StubCotizacionSvc(),
            conversionService ?? new StubConversionService(),
            NullLogger<CotizacionApiController>.Instance);

    private sealed class StubConversionService : ICotizacionConversionService
    {
        public CotizacionConversionPreviewResultado PreviewResultado { get; init; } =
            new() { Convertible = true, CotizacionId = 1 };

        public CotizacionConversionResultado ConversionResultado { get; init; } =
            new() { Exitoso = true, CotizacionId = 1, VentaId = 10, NumeroVenta = "COT-202501-1" };

        public int? UltimaCotizacionId { get; private set; }

        public Task<CotizacionConversionPreviewResultado> PreviewConversionAsync(int cotizacionId, CancellationToken cancellationToken = default) =>
            Task.FromResult(PreviewResultado);

        public Task<CotizacionConversionResultado> ConvertirAVentaAsync(int cotizacionId, CotizacionConversionRequest request, string usuario, CancellationToken cancellationToken = default)
        {
            UltimaCotizacionId = cotizacionId;
            return Task.FromResult(ConversionResultado);
        }
    }

    private sealed class StubCalculator : ICotizacionPagoCalculator
    {
        public Task<CotizacionSimulacionResultado> SimularAsync(CotizacionSimulacionRequest request, CancellationToken cancellationToken = default) =>
            Task.FromResult(new CotizacionSimulacionResultado { Exitoso = true });
    }

    private sealed class StubCotizacionSvc : ICotizacionService
    {
        public Task<CotizacionResultado> CrearAsync(CotizacionCrearRequest request, string usuario, CancellationToken cancellationToken = default) =>
            Task.FromResult(new CotizacionResultado { Id = 1, Numero = "COT-TEST" });

        public Task<CotizacionResultado?> ObtenerAsync(int id, CancellationToken cancellationToken = default) =>
            Task.FromResult<CotizacionResultado?>(null);

        public Task<CotizacionListadoResultado> ListarAsync(CotizacionFiltros filtros, CancellationToken cancellationToken = default) =>
            Task.FromResult(new CotizacionListadoResultado());
    }
}
