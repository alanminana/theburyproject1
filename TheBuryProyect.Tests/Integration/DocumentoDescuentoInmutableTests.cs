using Microsoft.EntityFrameworkCore;
using TheBuryProject.Models.Entities;
using TheBuryProject.Services;
using TheBuryProject.Services.Interfaces;

namespace TheBuryProject.Tests.Integration;

/// <summary>
/// DESCUENTO-PERMISO-03 (hallazgo de la revisión adversarial de #99): la restricción de un documento emitido no puede
/// depender de los descuentos ACTUALES de la venta, que se pueden quitar editando la venta (estados previos a la
/// confirmación) después de emitir. El documento emitido conserva la marca del momento de la emisión.
/// </summary>
public class DocumentoDescuentoInmutableTests : DocumentoTestBase
{
    private sealed class UsuarioSinDescuentoStub : ICurrentUserService
    {
        public string GetUsername() => "sin.descuento";
        public string GetUserId() => "sin-descuento-id";
        public bool IsAuthenticated() => true;
        public string? GetEmail() => null;
        public bool IsInRole(string role) => false;
        public bool HasPermission(string modulo, string accion) => false;
        public string? GetIpAddress() => null;
    }

    private DescuentoVisibilidadService ServicioSinPermiso() => new(Context, new UsuarioSinDescuentoStub());

    private async Task<(Venta Venta, List<DocumentoGenerado> Docs)> EmitirAsync(bool conDescuentos)
    {
        await SembrarConfiguracionInicialAsync();
        var venta = await SembrarVentaAsync();
        if (conDescuentos)
        {
            venta.Descuento = 100m;
            await Context.SaveChangesAsync();
        }

        var r = await EmitirVentaAsync(venta.Id);
        Assert.NotEmpty(r.Generados);
        return (venta, r.Generados.ToList());
    }

    private async Task QuitarDescuentosAsync(int ventaId)
    {
        var venta = await Context.Ventas.Include(v => v.Detalles).SingleAsync(v => v.Id == ventaId);
        venta.Descuento = 0m;
        foreach (var d in venta.Detalles)
        {
            d.Descuento = 0m;
            d.DescuentoGeneralProrrateado = 0m;
        }
        await Context.SaveChangesAsync();
    }

    [Fact]
    public async Task Emision_ConDescuentos_MarcaLaMetadataDeTodosLosDocumentos()
    {
        var (_, docs) = await EmitirAsync(conDescuentos: true);

        Assert.All(docs, d => Assert.True(DescuentoVisibilidadService.DocumentoEmitidoConDescuentos(d.MetadataJson), d.MetadataJson));
    }

    [Fact]
    public async Task Emision_SinDescuentos_NoMarcaLosDocumentos()
    {
        var (_, docs) = await EmitirAsync(conDescuentos: false);

        Assert.All(docs, d => Assert.False(DescuentoVisibilidadService.DocumentoEmitidoConDescuentos(d.MetadataJson), d.MetadataJson));
        Assert.Empty(await ServicioSinPermiso().DocumentosRestringidosAsync(docs.Select(d => d.Id).ToList()));
    }

    [Fact]
    public async Task DocumentoEmitidoConDescuentos_SigueRestringido_AunqueSeQuitenLosDescuentosDeLaVenta()
    {
        var (venta, docs) = await EmitirAsync(conDescuentos: true);
        var ids = docs.Select(d => d.Id).ToList();
        var servicio = ServicioSinPermiso();
        Assert.Equal(ids.Count, (await servicio.DocumentosRestringidosAsync(ids)).Count);

        await QuitarDescuentosAsync(venta.Id);

        Assert.Equal(ids.Count, (await servicio.DocumentosRestringidosAsync(ids)).Count);
        Assert.True(await servicio.DebeDenegarDocumentosAsync(ids));
        Assert.True(await servicio.DebeDenegarGrupoDocumentosAsync(docs[0].GrupoImpresionId!.Value));
    }

    [Fact]
    public async Task DocumentoAnteriorALaMarca_SinDescuentosActuales_NoSeRestringeEnBloque()
    {
        // Decisión funcional: lo ya emitido sin marca sigue clasificándose por el estado actual de la venta.
        var (_, docs) = await EmitirAsync(conDescuentos: false);
        foreach (var d in docs)
            d.MetadataJson = "{\"regla\":\"legado\"}";
        await Context.SaveChangesAsync();

        Assert.Empty(await ServicioSinPermiso().DocumentosRestringidosAsync(docs.Select(d => d.Id).ToList()));
    }

    [Fact]
    public async Task DocumentoAnteriorALaMarca_ConDescuentosActuales_SigueRestringido()
    {
        var (_, docs) = await EmitirAsync(conDescuentos: true);
        foreach (var d in docs)
            d.MetadataJson = "{\"regla\":\"legado\"}";
        await Context.SaveChangesAsync();

        Assert.Equal(docs.Count, (await ServicioSinPermiso().DocumentosRestringidosAsync(docs.Select(d => d.Id).ToList())).Count);
    }

    [Fact]
    public async Task UsuarioConPermiso_NuncaTieneDocumentosRestringidos()
    {
        var (_, docs) = await EmitirAsync(conDescuentos: true);

        var conPermiso = new DescuentoVisibilidadService(Context, new DocUserStub());
        Assert.Empty(await conPermiso.DocumentosRestringidosAsync(docs.Select(d => d.Id).ToList()));
    }

    [Fact]
    public async Task ContratoPdf_ConDocumentoEmitidoConDescuentoGeneral_SigueDenegado_AunqueLaVentaYaNoLoTenga()
    {
        // Solo descuento general (líneas en cero): el guard del PDF del contrato debe consultar la marca de los documentos
        // emitidos de la venta, no solo la venta actual ni el snapshot legado.
        var (venta, docs) = await EmitirAsync(conDescuentos: true);
        await QuitarDescuentosAsync(venta.Id);
        var servicio = ServicioSinPermiso();

        Assert.True(await servicio.DebeDenegarVentaAsync(venta.Id));
        Assert.False(await new DescuentoVisibilidadService(Context, new DocUserStub()).DebeDenegarVentaAsync(venta.Id));
        Assert.NotEmpty(docs);
    }

    [Fact]
    public async Task ContratoPdf_SinDocumentosMarcadosNiDescuentos_NoSeDeniega()
    {
        var (venta, _) = await EmitirAsync(conDescuentos: false);

        Assert.False(await ServicioSinPermiso().DebeDenegarVentaAsync(venta.Id));
    }

    // ------------------------------------------------------------------ Contrato legado (snapshot inmutable)

    [Theory]
    [InlineData("{\"Venta\":{\"Productos\":[{\"Descuento\":10,\"Subtotal\":900}]}}", true)]
    [InlineData("{\"venta\":{\"productos\":[{\"descuento\":5.5}]}}", true)]
    [InlineData("{\"Venta\":{\"Productos\":[{\"Descuento\":0},{\"Descuento\":0.0}]}}", false)]
    [InlineData("{\"Venta\":{\"Productos\":[]}}", false)]
    [InlineData("{}", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    [InlineData("{\"Venta\":{\"ConDescuentos\":true,\"Productos\":[{\"Descuento\":0}]}}", true)]
    [InlineData("{\"Venta\":{\"ConDescuentos\":false,\"Productos\":[{\"Descuento\":0}]}}", false)]
    [InlineData("{no es json", true)]
    [InlineData("{\"Venta\":{\"Productos\":[{\"Descuento\":\"mucho\"}]}}", true)]
    public void SnapshotContrato_ClasificaPorElDescuentoDeLasLineasEmitidas(string? json, bool esperado)
    {
        Assert.Equal(esperado, DescuentoVisibilidadService.SnapshotContratoTieneDescuentos(json));
    }

    private async Task<Venta> VentaConContratoLegadoAsync(string snapshotJson, string sufijo)
    {
        await SembrarConfiguracionInicialAsync();
        var venta = await SembrarVentaAsync();
        var plantillaId = (await Context.PlantillasContratoCredito.FirstAsync()).Id;
        Context.ContratosVentaCredito.Add(new ContratoVentaCredito
        {
            VentaId = venta.Id,
            CreditoId = venta.CreditoId!.Value,
            ClienteId = venta.ClienteId!.Value,
            PlantillaContratoCreditoId = plantillaId,
            NumeroContrato = $"CVC-TEST-{sufijo}",
            NumeroPagare = $"PAG-TEST-{sufijo}",
            UsuarioGeneracion = "tester",
            DatosSnapshotJson = snapshotJson
        });
        await Context.SaveChangesAsync();
        return venta;
    }

    [Fact]
    public async Task ContratoLegado_EmitidoConDescuentos_SigueDenegado_AunqueLaVentaYaNoLosTenga()
    {
        var venta = await VentaConContratoLegadoAsync(
            "{\"Venta\":{\"Productos\":[{\"Descuento\":10,\"Subtotal\":810}]}}", "1");

        Assert.True(await ServicioSinPermiso().DebeDenegarVentaAsync(venta.Id));   // la venta actual no tiene descuentos
        Assert.False(await new DescuentoVisibilidadService(Context, new DocUserStub()).DebeDenegarVentaAsync(venta.Id));
    }

    [Fact]
    public async Task ContratoLegado_EmitidoSinDescuentos_NoSeDeniega()
    {
        var venta = await VentaConContratoLegadoAsync(
            "{\"Venta\":{\"Productos\":[{\"Descuento\":0,\"Subtotal\":900}]}}", "2");

        Assert.False(await ServicioSinPermiso().DebeDenegarVentaAsync(venta.Id));
    }
}
