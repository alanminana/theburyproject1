using System.Net;

namespace TheBuryProject.Tests.Integration;

/// <summary>
/// Tests de integración de seguridad para el endpoint de vencimiento de cotizaciones.
/// Verifican que PermisoRequeridoAttribute(cotizaciones.expire) funciona en runtime:
/// - Usuario sin cotizaciones.expire → 403 Forbidden
/// - Usuario con cotizaciones.expire → no 403 (200 según lógica de negocio)
/// </summary>
[Collection("HttpIntegration")]
public class CotizacionVencimientoSecurityTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;

    private const string VencerUrl = "/api/cotizacion/vencer-expiradas";

    public CotizacionVencimientoSecurityTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
    }

    // ── SIN PERMISO → 403 ─────────────────────────────────────────────────

    [Fact]
    public async Task VencerExpiradas_SinPermisoExpire_DevuelveForbidden()
    {
        // NoPermsUserId no está en DB: PermissionClaimsTransformation devuelve vacío.
        // PermisoRequeridoAttribute ve: SuperAdmin=false, cotizaciones.expire=false → 403.
        using var client = _factory.CreateClientWithUserId(CustomWebApplicationFactory.NoPermsUserId);

        var response = await client.PostAsync(VencerUrl, null);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    // ── CON PERMISO → NO 403 ──────────────────────────────────────────────

    [Fact]
    public async Task VencerExpiradas_ConPermisoExpire_NoDevuelveForbidden()
    {
        // ExpirePermsUserId: seedeado con RolPermiso(cotizaciones.expire) + cotizaciones.view, sin SuperAdmin.
        // PermissionClaimsTransformation agrega Permission:cotizaciones.expire.
        // PermisoRequeridoAttribute: SuperAdmin=false, cotizaciones.expire=true → pasa autorización.
        // Sin cotizaciones expiradas en DB → resultado exitoso con 0 vencidas → 200.
        await _factory.SeedUserWithExpirePermissionAsync();
        using var client = _factory.CreateClientWithUserId(CustomWebApplicationFactory.ExpirePermsUserId);

        var response = await client.PostAsync(VencerUrl, null);

        Assert.NotEqual(HttpStatusCode.Forbidden, response.StatusCode);
    }

    // ── SUPERADMIN → EJECUTA ──────────────────────────────────────────────

    [Fact]
    public async Task VencerExpiradas_SuperAdmin_DevuelveOk()
    {
        await _factory.SeedTestUserAsync();
        using var client = _factory.CreateAuthenticatedClient();

        var response = await client.PostAsync(VencerUrl, null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
