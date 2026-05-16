using System.Net;
using System.Net.Http.Json;

namespace TheBuryProject.Tests.Integration;

/// <summary>
/// Tests de integración de seguridad para los endpoints de conversión de cotización.
/// Verifican que PermisoRequeridoAttribute(cotizaciones.convert) funciona en runtime:
/// - Usuario sin cotizaciones.convert → 403 Forbidden
/// - Usuario con cotizaciones.convert → no 403 (200 o 400 según datos)
///
/// Mecanismo: TestAuthHandler soporta X-Test-User-Id header.
/// - NoPermsUserId: no seedeado en DB → PermissionClaimsTransformation limpia SuperAdmin y permisos → 403
/// - ConvertPermsUserId: seedeado con RolPermiso(cotizaciones.convert) → PermissionClaimsTransformation agrega el claim → no 403
/// </summary>
[Collection("HttpIntegration")]
public class CotizacionConversionSecurityTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;

    private const string PreviewUrl = "/api/cotizacion/999/conversion/preview";
    private const string ConvertirUrl = "/api/cotizacion/999/conversion/convertir";

    public CotizacionConversionSecurityTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
    }

    // ── SIN PERMISO → 403 ─────────────────────────────────────────────────

    [Fact]
    public async Task Preview_SinPermisoConvert_DevuelveForbidden()
    {
        // NoPermsUserId no está en DB: PermissionClaimsTransformation devuelve vacío.
        // PermisoRequeridoAttribute ve: SuperAdmin=false, cotizaciones.convert=false → 403.
        using var client = _factory.CreateClientWithUserId(CustomWebApplicationFactory.NoPermsUserId);

        var response = await client.PostAsync(PreviewUrl, null);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Convertir_SinPermisoConvert_DevuelveForbidden()
    {
        using var client = _factory.CreateClientWithUserId(CustomWebApplicationFactory.NoPermsUserId);

        var response = await client.PostAsJsonAsync(ConvertirUrl, new { FormaPagoId = 1 });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    // ── CON PERMISO → NO 403 ──────────────────────────────────────────────

    [Fact]
    public async Task Preview_ConPermisoConvert_NoDevuelveForbidden()
    {
        // ConvertPermsUserId: seedeado con RolPermiso(cotizaciones.convert), sin SuperAdmin.
        // PermissionClaimsTransformation agrega Permission:cotizaciones.convert.
        // PermisoRequeridoAttribute: SuperAdmin=false, cotizaciones.convert=true → pasa autorización.
        // Cotizacion 999 no existe → servicio devuelve convertible=false → controller devuelve 200.
        await _factory.SeedUserWithConvertPermissionAsync();
        using var client = _factory.CreateClientWithUserId(CustomWebApplicationFactory.ConvertPermsUserId);

        var response = await client.PostAsync(PreviewUrl, null);

        Assert.NotEqual(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Convertir_ConPermisoConvert_NoDevuelveForbidden()
    {
        // Cotizacion 999 no existe → servicio devuelve Fallido → controller devuelve 400.
        // Lo importante: no es 403 → autorización correcta.
        await _factory.SeedUserWithConvertPermissionAsync();
        using var client = _factory.CreateClientWithUserId(CustomWebApplicationFactory.ConvertPermsUserId);

        var response = await client.PostAsJsonAsync(ConvertirUrl, new { });

        Assert.NotEqual(HttpStatusCode.Forbidden, response.StatusCode);
    }
}
