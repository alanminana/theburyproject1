using System.Net;

namespace TheBuryProject.Tests.Integration;

/// <summary>
/// Los documentos de clientes (DNI, comprobantes) se guardan bajo wwwroot/uploads/documentos-clientes
/// con nombre físico predecible (ClienteId_Tipo_timestamp). Antes del fix, UseStaticFiles los servía
/// directamente sin pasar por DocumentoClienteController.Descargar (autenticado + permiso), permitiendo
/// descargarlos sin sesión. Ahora un middleware previo devuelve 404 para esa ruta, sin importar si el
/// archivo existe físicamente, forzando el acceso exclusivo vía el controller.
/// </summary>
[Collection("HttpIntegration")]
public class UploadsDocumentosClientesEstaticoHttpTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;

    public UploadsDocumentosClientesEstaticoHttpTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Theory]
    [InlineData("/uploads/documentos-clientes/1_Dni_20260101120000.pdf")]
    [InlineData("/uploads/documentos-clientes/subcarpeta/otro.pdf")]
    public async Task Get_ArchivoDeDocumentoCliente_SinSesion_Devuelve404(string path)
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync(path);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Get_OtroArchivoEstatico_FueraDeDocumentosClientes_NoEsBloqueadoPorElMismoMiddleware()
    {
        var client = _factory.CreateClient();

        // Ruta estática cualquiera fuera de /uploads/documentos-clientes: el middleware nuevo no debe
        // interceptarla (puede dar 404 real de "no existe", pero no por el bloqueo agregado).
        var response = await client.GetAsync("/uploads/tickets/no-existe.pdf");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
