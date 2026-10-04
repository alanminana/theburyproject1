using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using TheBuryProject.Filters;
using TheBuryProject.Models.Entities;
using TheBuryProject.Models.Enums;
using TheBuryProject.Services;
using TheBuryProject.Services.Documentos;
using TheBuryProject.Services.Interfaces;

namespace TheBuryProject.Tests.Integration;

internal sealed class TempDataProviderVacio : ITempDataProvider
{
    public IDictionary<string, object> LoadTempData(HttpContext context) => new Dictionary<string, object>();
    public void SaveTempData(HttpContext context, IDictionary<string, object> values) { }
}

internal sealed class ControladorDePrueba : Controller { }

/// <summary>
/// Todo documento emitido por una acción se abre como PDF al terminar (cobro, confirmación, entrega, reintento…) y un recibo
/// reintentado recupera toda su cobranza.
/// </summary>
public class DocumentoAperturaAutomaticaTests : DocumentoTestBase
{
    private readonly DocumentosEmitidosTracker _tracker = new();
    private DocumentoService _motorConTracker = null!;

    private async Task PrepararAsync()
    {
        Context.PlantillasContratoCredito.Add(new PlantillaContratoCredito
        {
            Nombre = "Base", Activa = true, NombreVendedor = "Nombre del vendedor / comercio", DomicilioVendedor = "Domicilio del vendedor",
            CiudadFirma = "Ciudad", Jurisdiccion = "Provincia", InteresMoraDiarioPorcentaje = 0.05m, TextoContrato = "x", TextoPagare = "x",
            VigenteDesde = DateTime.UtcNow.Date.AddDays(-1)
        });
        await Context.SaveChangesAsync();
        await DocumentoConfiguracionCredito.ConfigurarAsync(Context, NullLogger.Instance);

        _motorConTracker = new DocumentoService(
            Context, new DocumentoContextoBuilder(Context), new DocumentoNumeracionService(Context, NullLogger<DocumentoNumeracionService>.Instance),
            new DocumentoPdfService(), new DocUserStub(), Auditoria, RelojComercial.Sistema, NullLogger<DocumentoService>.Instance, _tracker);
    }

    private async Task<(Venta Venta, PagoCuota P1, PagoCuota P2)> VentaConDosPagosAsync()
    {
        var venta = await SembrarVentaAsync(cuotas: 3);
        var c1 = await Context.Cuotas.FirstAsync(c => c.CreditoId == venta.CreditoId && c.NumeroCuota == 1);
        var c2 = await Context.Cuotas.FirstAsync(c => c.CreditoId == venta.CreditoId && c.NumeroCuota == 2);
        PagoCuota Nuevo(Cuota c) => new()
        {
            CuotaId = c.Id, FechaPagoComercial = DateOnly.FromDateTime(DateTime.Today), ImporteTotal = 300m, ImporteAplicadoCuota = 300m,
            ImporteAplicadoPunitorio = 0m, MedioPago = "Efectivo", Origen = OrigenPagoCuota.RegistradoPorSistema,
            Estado = EstadoPagoCuota.Aplicado, HistorialCompleto = true
        };
        var p1 = Nuevo(c1);
        var p2 = Nuevo(c2);
        Context.PagosCuota.AddRange(p1, p2);       // una sola operación: los dos quedan registrados en el mismo instante
        await Context.SaveChangesAsync();
        return (venta, p1, p2);
    }

    [Fact]
    public async Task LoEmitidoEnLaPeticion_QuedaRegistrado_ParaAbrirseComoPdf_YUnReintentoNoLoRepite()
    {
        await PrepararAsync();
        var venta = await SembrarVentaAsync();

        var r = await _motorConTracker.ProcesarEventoAsync(new DocumentoEventoRequest
        {
            Evento = EventosDocumentales.ContratoCreditoSolicitado, Origen = new DocumentoOrigen { VentaId = venta.Id }
        });

        Assert.Equal(r.Generados.Select(d => d.Id).OrderBy(i => i), _tracker.Ids.OrderBy(i => i));
        Assert.Equal(2, _tracker.Ids.Count);

        _tracker.Limpiar();
        await _motorConTracker.ProcesarEventoAsync(new DocumentoEventoRequest
        {
            Evento = EventosDocumentales.ContratoCreditoSolicitado, Origen = new DocumentoOrigen { VentaId = venta.Id }
        });
        Assert.Empty(_tracker.Ids);       // idempotente: lo que ya existía no se vuelve a abrir
    }

    [Fact]
    public async Task ReintentarElReciboDeUnPago_RecuperaTodaLaCobranza_EnUnSoloRecibo()
    {
        await PrepararAsync();
        var (_, p1, p2) = await VentaConDosPagosAsync();

        // Se pide por el pago "de atrás": igual sale un único recibo, anclado al menor, con las dos cuotas.
        var r = await _motorConTracker.ReintentarEventoAsync(EventosDocumentales.PagoRegistrado, new DocumentoOrigen { PagoCuotaId = p2.Id });
        var recibo = Assert.Single(r.Generados);
        Assert.Equal(p1.Id, recibo.PagoCuotaId);
        Assert.Contains("Cuota: 1/3", recibo.ContenidoRenderizado);
        Assert.Contains("Cuota: 2/3", recibo.ContenidoRenderizado);
        Assert.Contains("Monto total: 600,00", recibo.ContenidoRenderizado);

        var otra = await _motorConTracker.ReintentarEventoAsync(EventosDocumentales.PagoRegistrado, new DocumentoOrigen { PagoCuotaId = p1.Id });
        Assert.Empty(otra.Generados);
        Assert.Equal(1, await Context.DocumentosGenerados.CountAsync(d => d.TipoDocumento.Codigo == "RECIBO"));
    }

    [Fact]
    public async Task ElBotonDePresupuestoDeCotizacion_SoloSeOfreceSiHayReglaActiva()
    {
        await PrepararAsync();

        Assert.False(await _motorConTracker.TieneReglaActivaAsync(EventosDocumentales.PresupuestoGenerado));
        Assert.True(await _motorConTracker.TieneReglaActivaAsync(EventosDocumentales.PresupuestoVentaGenerado));
        Assert.True(await _motorConTracker.TieneReglaActivaAsync(EventosDocumentales.PagoRegistrado));
    }

    // ------------------------------------------------------------------ Filtro de apertura

    private static (ResultExecutingContext Contexto, ControladorDePrueba Controlador) Contexto(IActionResult resultado, bool conPermiso = true, bool ajax = false)
    {
        var http = new DefaultHttpContext();
        if (ajax)
            http.Request.Headers.XRequestedWith = "XMLHttpRequest";
        http.User = conPermiso
            ? new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.Role, "SuperAdmin") }, "test"))
            : new ClaimsPrincipal(new ClaimsIdentity());
        var controlador = new ControladorDePrueba { TempData = new TempDataDictionary(http, new TempDataProviderVacio()) };
        var accion = new ActionContext(http, new RouteData(), new ActionDescriptor());
        return (new ResultExecutingContext(accion, new List<IFilterMetadata>(), resultado, controlador), controlador);
    }

    private static Task<ResultExecutedContext> Ejecutar(DocumentosEmitidosFilter filtro, ResultExecutingContext ctx)
        => Task.FromResult(new ResultExecutedContext(ctx, ctx.Filters, ctx.Result, ctx.Controller));

    [Fact]
    public async Task ElFiltro_DejaElAvisoParaAbrirLosDocumentos_CuandoLaAccionRedirige()
    {
        var tracker = new DocumentosEmitidosTracker();
        tracker.Registrar(new[] { 7, 8 });
        var (ctx, controlador) = Contexto(new RedirectToActionResult("Details", "Venta", new { id = 1 }));

        await new DocumentosEmitidosFilter(tracker).OnResultExecutionAsync(ctx, () => Ejecutar(null!, ctx));

        Assert.Equal("7,8", controlador.TempData[DocumentosEmitidosFilter.ClaveTempData]);
    }

    [Fact]
    public async Task ElFiltro_NoAvisa_SinDocumentos_SinPermiso_EnAjax_NiSiLaRedireccionYaAbreElDocumento()
    {
        var tracker = new DocumentosEmitidosTracker();

        var (sinDocs, c1) = Contexto(new RedirectToActionResult("Details", "Venta", new { id = 1 }));
        await new DocumentosEmitidosFilter(tracker).OnResultExecutionAsync(sinDocs, () => Ejecutar(null!, sinDocs));
        Assert.False(c1.TempData.ContainsKey(DocumentosEmitidosFilter.ClaveTempData));

        tracker.Registrar(new[] { 1 });

        var (sinPermiso, c2) = Contexto(new RedirectToActionResult("Details", "Venta", new { id = 1 }), conPermiso: false);
        await new DocumentosEmitidosFilter(tracker).OnResultExecutionAsync(sinPermiso, () => Ejecutar(null!, sinPermiso));
        Assert.False(c2.TempData.ContainsKey(DocumentosEmitidosFilter.ClaveTempData));

        var (ajax, c3) = Contexto(new RedirectToActionResult("Details", "Venta", new { id = 1 }), ajax: true);
        await new DocumentosEmitidosFilter(tracker).OnResultExecutionAsync(ajax, () => Ejecutar(null!, ajax));
        Assert.False(c3.TempData.ContainsKey(DocumentosEmitidosFilter.ClaveTempData));

        var (haciaElPdf, c4) = Contexto(new RedirectToActionResult("Ver", "Documento", new { id = 1 }));
        await new DocumentosEmitidosFilter(tracker).OnResultExecutionAsync(haciaElPdf, () => Ejecutar(null!, haciaElPdf));
        Assert.False(c4.TempData.ContainsKey(DocumentosEmitidosFilter.ClaveTempData));

        var (json, c5) = Contexto(new JsonResult(new { ok = true }));
        await new DocumentosEmitidosFilter(tracker).OnResultExecutionAsync(json, () => Ejecutar(null!, json));
        Assert.False(c5.TempData.ContainsKey(DocumentosEmitidosFilter.ClaveTempData));
    }
}
