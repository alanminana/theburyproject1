using Microsoft.EntityFrameworkCore;
using TheBuryProject.Models.Entities;
using TheBuryProject.Models.Enums;
using TheBuryProject.Services.Interfaces;

namespace TheBuryProject.Tests.Integration;

/// <summary>Sub-pestañas de Documentos emitidos: Pendientes, Finalizados y Anulados, con sus contadores.</summary>
public class DocumentoVistasTests : DocumentoTestBase
{
    private async Task SembrarDocumentosAsync()
    {
        await SembrarConfiguracionInicialAsync();
        var plantilla = await Context.PlantillasDocumento.OrderBy(p => p.Id).FirstAsync();
        var version = await Context.PlantillasDocumentoVersion.FirstAsync(v => v.PlantillaDocumentoId == plantilla.Id);

        var estados = new[]
        {
            EstadoDocumentoGenerado.Generado, EstadoDocumentoGenerado.Generado, EstadoDocumentoGenerado.Firmado,
            EstadoDocumentoGenerado.PendienteFirma, EstadoDocumentoGenerado.Cancelado, EstadoDocumentoGenerado.Reemplazado
        };
        for (var i = 0; i < estados.Length; i++)
        {
            Context.DocumentosGenerados.Add(new DocumentoGenerado
            {
                TipoDocumentoId = plantilla.TipoDocumentoId,
                PlantillaDocumentoId = plantilla.Id,
                PlantillaDocumentoVersionId = version.Id,
                Numero = $"TST-{i + 1:000}",
                EventoOrigen = "TEST",
                ClaveIdempotencia = $"test|{i}",
                Estado = estados[i],
                FechaGeneracionUtc = DateTime.UtcNow,
                UsuarioGeneracion = "tester",
                ContenidoRenderizado = "x",
                DatosSnapshotJson = "{}",
                ContentHash = "h"
            });
        }
        await Context.SaveChangesAsync();
    }

    [Fact]
    public async Task Vistas_FiltranPorGrupoDeEstadoYContabilizan()
    {
        await SembrarDocumentosAsync();

        async Task<List<string>> Numeros(VistaDocumento v)
            => (await Motor.BuscarAsync(new DocumentoFiltro { Vista = v })).Items.Select(d => d.Numero).OrderBy(n => n).ToList();

        Assert.Equal(6, (await Numeros(VistaDocumento.Todos)).Count);
        Assert.Equal(new[] { "TST-001", "TST-002", "TST-003" }, await Numeros(VistaDocumento.Finalizados));
        Assert.Equal(new[] { "TST-004" }, await Numeros(VistaDocumento.Pendientes));
        Assert.Equal(new[] { "TST-005", "TST-006" }, await Numeros(VistaDocumento.Anulados));

        var conteos = await Motor.ContarPorVistaAsync(new DocumentoFiltro { Vista = VistaDocumento.Anulados });
        Assert.Equal(6, conteos[VistaDocumento.Todos]);
        Assert.Equal(3, conteos[VistaDocumento.Finalizados]);
        Assert.Equal(1, conteos[VistaDocumento.Pendientes]);
        Assert.Equal(2, conteos[VistaDocumento.Anulados]);
    }

    [Fact]
    public async Task Contadores_RespetanTextoPeroIgnoranLaPestanaActiva()
    {
        await SembrarDocumentosAsync();

        var conteos = await Motor.ContarPorVistaAsync(new DocumentoFiltro { Texto = "TST-00", Vista = VistaDocumento.Pendientes });
        Assert.Equal(6, conteos[VistaDocumento.Todos]);

        var ninguno = await Motor.ContarPorVistaAsync(new DocumentoFiltro { Texto = "no-existe" });
        Assert.All(ninguno.Values, v => Assert.Equal(0, v));
    }
}
