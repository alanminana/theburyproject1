using TheBuryProject.Helpers;

namespace TheBuryProject.Tests.Unit;

/// <summary>VALID-L11 — nombre original y tipo MIME de archivos subidos: columnas de 200 y 100 caracteres.</summary>
public class ArchivosCamposValidacionTests
{
    [Fact]
    public void Nombre_Corto_SeConserva()
        => Assert.Equal("dni frente.pdf", DocumentoValidationHelper.NombreParaMostrar("dni frente.pdf"));

    [Fact]
    public void Nombre_MuyLargo_SeRecortaConservandoLaExtension()
    {
        var resultado = DocumentoValidationHelper.NombreParaMostrar(new string('a', 500) + ".pdf");

        Assert.Equal(200, resultado.Length);
        Assert.EndsWith(".pdf", resultado);
    }

    [Fact]
    public void Nombre_ConRutaDelCliente_SoloQuedaElNombre()
        => Assert.Equal("comprobante.jpg", DocumentoValidationHelper.NombreParaMostrar(@"C:\Users\x\Desktop\comprobante.jpg"));

    [Fact]
    public void Nombre_ConExtensionLargaAbsurda_NoSuperaElMaximo()
        => Assert.True(DocumentoValidationHelper.NombreParaMostrar("x." + new string('e', 400)).Length <= 200);

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Mime_Vacio_EsNulo(string? valor)
        => Assert.Null(DocumentoValidationHelper.MimeAcotado(valor));

    [Fact]
    public void Mime_Normal_SeConserva()
        => Assert.Equal("application/pdf", DocumentoValidationHelper.MimeAcotado("application/pdf"));

    [Fact]
    public void Mime_MuyLargo_SeAcota()
        => Assert.Equal(100, DocumentoValidationHelper.MimeAcotado("application/" + new string('x', 300))!.Length);
}
