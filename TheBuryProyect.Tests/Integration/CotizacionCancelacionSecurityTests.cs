using System.Net;
using System.Net.Http.Json;

namespace TheBuryProject.Tests.Integration;

/// <summary>
/// Tests de integración de seguridad para el endpoint de cancelación de cotización.
/// Verifican que PermisoRequeridoAttribute(cotizaciones.cancel) funciona en runtime:
/// - Usuario sin cotizaciones.cancel → 403 Forbidden
/// - Usuario con cotizaciones.cancel → no 403 (200 o 400 según datos)
/// </summary>
[Collection("HttpIntegration")]
public class CotizacionCancelacionSecurityTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;

    private const string CancelarUrl = "/api/cotizacion/999/cancelar";

    public CotizacionCancelacionSecurityTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
    }

    // ── SIN PERMISO → 403 ─────────────────────────────────────────────────

    [Fact]
    public async Task Cancelar_SinPermisoCancel_DevuelveForbidden()
    {
        // NoPermsUserId no está en DB: PermissionClaimsTransformation devuelve vacío.
        // PermisoRequeridoAttribute ve: SuperAdmin=false, cotizaciones.cancel=false → 403.
        using var client = _factory.CreateClientWithUserId(CustomWebApplicationFactory.NoPermsUserId);

        var response = await client.PostAsJsonAsync(CancelarUrl, new { motivo = "Test" });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    // ── CON PERMISO → NO 403 ──────────────────────────────────────────────

    [Fact]
    public async Task Cancelar_ConPermisoCancel_NoDevuelveForbidden()
    {
        // CancelPermsUserId: seedeado con RolPermiso(cotizaciones.cancel) + cotizaciones.view, sin SuperAdmin.
        // PermissionClaimsTransformation agrega Permission:cotizaciones.cancel.
        // PermisoRequeridoAttribute: SuperAdmin=false, cotizaciones.cancel=true → pasa autorización.
        // Cotizacion 999 no existe → servicio devuelve Fallido → controller devuelve 400.
        // Lo importante: no es 403 → autorización correcta.
        await _factory.SeedUserWithCancelPermissionAsync();
        using var client = _factory.CreateClientWithUserId(CustomWebApplicationFactory.CancelPermsUserId);

        var response = await client.PostAsJsonAsync(CancelarUrl, new { motivo = "Motivo de prueba" });

        Assert.NotEqual(HttpStatusCode.Forbidden, response.StatusCode);
    }
}
