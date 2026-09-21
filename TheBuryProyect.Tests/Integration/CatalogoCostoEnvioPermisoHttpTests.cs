using System.Text.RegularExpressions;

namespace TheBuryProject.Tests.Integration;

/// <summary>
/// Render real de /Catalogo: el input "Envío" de los modales de producto es solo lectura
/// para quien no tiene productos.editshippingcost y editable para quien sí lo tiene.
/// </summary>
public class CatalogoCostoEnvioPermisoHttpTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;

    public CatalogoCostoEnvioPermisoHttpTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Catalogo_SinPermisoCostoEnvio_RenderizaLosDosInputsDeEnvioReadonlyConLeyenda()
    {
        await _factory.SeedUserWithProductoEditSinCostoEnvioAsync();
        using var client = _factory.CreateClientWithUserId(CustomWebApplicationFactory.ProductoEditSinCostoEnvioUserId);

        var html = await (await client.GetAsync("/Catalogo")).Content.ReadAsStringAsync();

        Assert.True(InputEnvio(html, "modal-costoEnvio").Contains("readonly", StringComparison.Ordinal));
        Assert.True(InputEnvio(html, "prod-edit-costoEnvio").Contains("readonly", StringComparison.Ordinal));
        Assert.Equal(2, Regex.Matches(html, "data-costo-envio-solo-lectura").Count);
    }

    [Fact]
    public async Task Catalogo_ConPermisoCostoEnvio_RenderizaLosDosInputsDeEnvioEditables()
    {
        await _factory.SeedUserWithProductoEditConCostoEnvioAsync();
        using var client = _factory.CreateClientWithUserId(CustomWebApplicationFactory.ProductoEditConCostoEnvioUserId);

        var html = await (await client.GetAsync("/Catalogo")).Content.ReadAsStringAsync();

        Assert.False(InputEnvio(html, "modal-costoEnvio").Contains("readonly", StringComparison.Ordinal));
        Assert.False(InputEnvio(html, "prod-edit-costoEnvio").Contains("readonly", StringComparison.Ordinal));
        Assert.DoesNotContain("data-costo-envio-solo-lectura", html);
        Assert.DoesNotContain("Requiere el permiso «Editar costo de envío»", html);
    }

    private static string InputEnvio(string html, string id)
    {
        var match = Regex.Match(html, $"<input[^>]*id=\"{id}\"[^>]*>", RegexOptions.Singleline);
        Assert.True(match.Success, $"No se renderizó el input #{id}.");
        return match.Value;
    }
}
