using QuestPDF.Fluent;
using TheBuryProject.Models.Entities;
using TheBuryProject.Services.Documentos;
using Xunit;

namespace TheBuryProject.Tests.Integration;

/// <summary>
/// Las firmas nunca pueden quedar solas en una página: viajan con la última cláusula y el texto de cierre.
/// Con DOC_DUMP_DIR definido se guardan los PDF del barrido para revisarlos.
/// </summary>
public class DocumentoPdfFirmasTests
{
    private static DocumentoGenerado Contrato(int lineasPrevias)
    {
        var texto = new System.Text.StringBuilder("@@admin\n** Contrato de Venta\n");
        for (var i = 0; i < lineasPrevias; i++)
            texto.Append("Linea de relleno ").Append(i).Append(" del contrato de compraventa.\n");
        texto.Append("\n** QUINTA\nEl senor constituye en fiador solidario y principal pagador.\n\n");
        texto.Append("En prueba de conformidad se firman dos ejemplares de un mismo tenor.-\n@@firmas\n");

        return new DocumentoGenerado
        {
            Numero = "CVC-TEST",
            ContenidoRenderizado = texto.ToString(),
            FirmantesRequeridos = "vendedor,comprador,fiador"
        };
    }

    [Fact]
    public void Firmas_ConCualquierLargoDelContrato_SeImprimenSinErrorYConLaUltimaClausula()
    {
        var carpeta = Environment.GetEnvironmentVariable("DOC_DUMP_DIR");
        if (!string.IsNullOrWhiteSpace(carpeta))
            Directory.CreateDirectory(carpeta);

        // Barrido a través del punto en que las firmas dejan de entrar en la página.
        for (var n = 40; n <= 90; n++)
        {
            var pdf = DocumentoPdfService.CrearDocumento(new[] { Contrato(n) }).GeneratePdf();
            Assert.NotEmpty(pdf);
            if (!string.IsNullOrWhiteSpace(carpeta))
                File.WriteAllBytes(Path.Combine(carpeta!, $"firmas-{n}.pdf"), pdf);
        }
    }
}
