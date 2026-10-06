using Microsoft.EntityFrameworkCore;
using TheBuryProject.Models.Enums;
using TheBuryProject.Services.Documentos;
using TheBuryProject.Services.Interfaces;

namespace TheBuryProject.Tests.Integration;

/// <summary>
/// "Generar pendientes" es un reintento explícito de una persona: lo que quedó solo como Cancelado/Reemplazado
/// vuelve a emitirse; los flujos automáticos nunca reemiten un documento cancelado.
/// </summary>
public class DocumentoReemisionTests : DocumentoTestBase
{
    private int TipoId(string codigo) => Context.TiposDocumento.First(t => t.Codigo == codigo).Id;

    private static DocumentoOrigen Origen(int ventaId) => new() { VentaId = ventaId };

    [Fact]
    public async Task ReintentoExplicito_TrasCancelar_ReemiteConClaveNueva_YElCanceladoQuedaDeHistorico()
    {
        await SembrarConfiguracionInicialAsync();
        var venta = await SembrarVentaAsync();
        var primero = await EmitirVentaAsync(venta.Id);
        var pagare = primero.Generados.Single(d => d.TipoDocumentoId == TipoId("PAGARE"));
        await Motor.CancelarAsync(pagare.Id, "Cambia el tipo de pagaré");

        var r = await Motor.ReintentarEventoAsync(EventosDocumentales.ContratoCreditoSolicitado, Origen(venta.Id));

        var nuevo = Assert.Single(r.Generados);
        Assert.Equal(pagare.TipoDocumentoId, nuevo.TipoDocumentoId);
        Assert.NotEqual(pagare.Id, nuevo.Id);
        Assert.NotEqual(pagare.Numero, nuevo.Numero);
        Assert.StartsWith(pagare.ClaveIdempotencia + "#e", nuevo.ClaveIdempotencia);
        var original = await Context.DocumentosGenerados.AsNoTracking().FirstAsync(d => d.Id == pagare.Id);
        Assert.Equal(EstadoDocumentoGenerado.Cancelado, original.Estado);
        Assert.Single(r.YaExistentes);   // el contrato vigente no se duplica
    }

    [Fact]
    public async Task FlujoAutomatico_NoReemiteUnDocumentoCancelado()
    {
        await SembrarConfiguracionInicialAsync();
        var venta = await SembrarVentaAsync();
        var primero = await EmitirVentaAsync(venta.Id);
        var pagare = primero.Generados.Single(d => d.TipoDocumentoId == TipoId("PAGARE"));
        await Motor.CancelarAsync(pagare.Id, "Pagaré emitido por error");

        var automatico = await EmitirVentaAsync(venta.Id);

        Assert.Empty(automatico.Generados);
        Assert.Equal(2, await Context.DocumentosGenerados.CountAsync());
    }

    [Fact]
    public async Task ReintentoExplicito_ConTodoVigente_NoDuplica()
    {
        await SembrarConfiguracionInicialAsync();
        var venta = await SembrarVentaAsync();
        await EmitirVentaAsync(venta.Id);

        var r = await Motor.ReintentarEventoAsync(EventosDocumentales.ContratoCreditoSolicitado, Origen(venta.Id));

        Assert.Empty(r.Generados);
        Assert.Equal(2, r.YaExistentes.Count);
        Assert.Equal(2, await Context.DocumentosGenerados.CountAsync());
    }

    [Fact]
    public async Task ReintentoExplicito_TrasRegenerar_NoCreaUnTercerDocumento()
    {
        await SembrarConfiguracionInicialAsync();
        var venta = await SembrarVentaAsync();
        var primero = await EmitirVentaAsync(venta.Id);
        var contrato = primero.Generados.Single(d => d.TipoDocumentoId == TipoId("CONTRATO"));
        await Motor.RegenerarAsync(contrato.Id, "Actualizo el texto del contrato");

        var r = await Motor.ReintentarEventoAsync(EventosDocumentales.ContratoCreditoSolicitado, Origen(venta.Id));

        Assert.Empty(r.Generados);
        var vigentes = await Context.DocumentosGenerados.CountAsync(d =>
            d.Estado != EstadoDocumentoGenerado.Cancelado && d.Estado != EstadoDocumentoGenerado.Reemplazado);
        Assert.Equal(2, vigentes);   // contrato regenerado + pagaré original
    }

    [Fact]
    public async Task ReintentarVenta_ConPasoPorContrato_ReemiteContratoYPagareCancelados()
    {
        await SembrarConfiguracionInicialAsync();
        var venta = await SembrarVentaAsync();
        var primero = await EmitirVentaAsync(venta.Id);
        foreach (var d in primero.Generados)
            await Motor.CancelarAsync(d.Id, "Cambio de condiciones");

        var r = await Motor.ReintentarVentaAsync(venta.Id);

        Assert.Equal(2, r.Generados.Count);
        Assert.All(r.Generados, d => Assert.NotEqual(EstadoDocumentoGenerado.Cancelado, d.Estado));
        Assert.Equal(new[] { "CONTRATO", "PAGARE" },
            r.Generados.Select(d => Context.TiposDocumento.First(t => t.Id == d.TipoDocumentoId).Codigo).OrderBy(c => c).ToArray());
    }

    [Fact]
    public async Task ReintentarVenta_SinPasoPorContrato_NoEmiteContratoNiPagare()
    {
        await SembrarConfiguracionInicialAsync();
        var venta = await SembrarVentaAsync();   // nunca se emitió el contrato

        var r = await Motor.ReintentarVentaAsync(venta.Id);

        Assert.Empty(r.Generados);
        Assert.Equal(0, await Context.DocumentosGenerados.CountAsync());
    }
}
