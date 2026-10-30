using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.Extensions.Logging.Abstractions;
using TheBuryProject.Controllers;
using TheBuryProject.Models.Entities;
using TheBuryProject.Services.Interfaces;
using TheBuryProject.Services.Models;

namespace TheBuryProject.Tests.Unit;

file sealed class ContratoServiceVerStub : IContratoVentaCreditoService
{
    public ContratoVentaCredito? Registrado { get; init; }

    public Task<ContratoVentaCreditoValidacionResult> ValidarDatosParaGenerarAsync(int ventaId) => throw new NotImplementedException();
    public ContratoVentaCreditoValidacionResult ValidarDatosClienteParaContrato(Cliente? cliente) => throw new NotImplementedException();
    public Task<ContratoVentaCredito> GenerarAsync(int ventaId, string usuario) => throw new NotImplementedException();
    public Task<ContratoVentaCredito> GenerarPdfAsync(int ventaId, string usuario) => throw new NotImplementedException();
    public Task<ContratoVentaCreditoPdfArchivo?> ObtenerPdfAsync(int ventaId) => Task.FromResult<ContratoVentaCreditoPdfArchivo?>(null);
    public Task<bool> ExisteContratoGeneradoAsync(int ventaId) => Task.FromResult(false);
    public Task<bool> ExistePlantillaActivaAsync() => throw new NotImplementedException();
    public Task<ContratoVentaCredito?> ObtenerContratoPorVentaAsync(int ventaId) => Task.FromResult(Registrado);
    public Task<ContratoVentaCredito?> ObtenerContratoPorCreditoAsync(int creditoId) => throw new NotImplementedException();
}

file sealed class DescuentoVerStub : IDescuentoVisibilidadService
{
    public bool PuedeVerDescuentos => true;
    public Task<bool> DebeDenegarVentaAsync(int ventaId) => Task.FromResult(false);
    public Task<bool> DebeDenegarFacturaAsync(int facturaId) => Task.FromResult(false);
    public Task<bool> DebeDenegarCotizacionAsync(int cotizacionId) => Task.FromResult(false);
    public Task<bool> DebeDenegarDocumentosAsync(IReadOnlyCollection<int> documentoIds) => Task.FromResult(false);
    public Task<IReadOnlySet<int>> DocumentosRestringidosAsync(IReadOnlyCollection<int> documentoIds) => throw new NotImplementedException();
    public Task<bool> DebeDenegarGrupoDocumentosAsync(Guid grupoImpresionId) => Task.FromResult(false);
    public Task<bool> DebeDenegarDocumentosDePagoAsync(int pagoId) => Task.FromResult(false);
}

file sealed class UsuarioVerStub : ICurrentUserService
{
    public string GetUsername() => "testuser";
    public string GetUserId() => "system";
    public bool IsAuthenticated() => true;
    public string? GetEmail() => null;
    public bool IsInRole(string role) => false;
    public bool HasPermission(string modulo, string accion) => true;
    public string? GetIpAddress() => null;
}

/// <summary>
/// H10: "Ver contrato" de un contrato emitido cuyo PDF no está en el servidor (copia restaurada sin App_Data) lleva a
/// "Generar PDF" en vez de dejar al operador en Details sin camino para regenerarlo.
/// </summary>
public class ContratoVentaCreditoControllerVerTests
{
    private static ContratoVentaCreditoController Crear(ContratoVentaCredito? registrado)
    {
        var controller = new ContratoVentaCreditoController(
            new ContratoServiceVerStub { Registrado = registrado },
            new UsuarioVerStub(),
            NullLogger<ContratoVentaCreditoController>.Instance,
            new DescuentoVerStub());
        var http = new DefaultHttpContext();
        controller.ControllerContext = new ControllerContext { HttpContext = http };
        controller.TempData = new TempDataDictionary(http, new NullTempDataProvider());
        return controller;
    }

    [Fact]
    public async Task Ver_ContratoEmitidoSinArchivo_RedirigeAPrepararParaRegenerar()
    {
        var controller = Crear(new ContratoVentaCredito { VentaId = 7, RutaArchivo = "App_Data/contratos-venta-credito/7/CVC-7.pdf" });

        var resultado = await controller.Ver(7);

        var redirect = Assert.IsType<RedirectToActionResult>(resultado);
        Assert.Equal("Preparar", redirect.ActionName);
        Assert.Equal(7, redirect.RouteValues!["ventaId"]);
        Assert.Contains("archivo PDF", (string)controller.TempData["Error"]!);
    }

    [Fact]
    public async Task Ver_SinContratoRegistrado_ConservaElMensajeYVuelveADetails()
    {
        var controller = Crear(registrado: null);

        var resultado = await controller.Ver(7);

        var redirect = Assert.IsType<RedirectToActionResult>(resultado);
        Assert.Equal("Details", redirect.ActionName);
        Assert.Equal("Venta", redirect.ControllerName);
        Assert.Equal("Contrato no encontrado. Genere el contrato antes de visualizarlo.", controller.TempData["Error"]);
    }

    private sealed class NullTempDataProvider : ITempDataProvider
    {
        public IDictionary<string, object> LoadTempData(HttpContext context) => new Dictionary<string, object>();
        public void SaveTempData(HttpContext context, IDictionary<string, object> values) { }
    }
}
