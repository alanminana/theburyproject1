using System.Net;
using Xunit;

namespace TheBuryProject.Tests.Integration;

/// <summary>
/// Preproduccion P0: las herramientas de diagnostico (reset de contrasenas, creacion de usuarios, login de prueba,
/// volcado de usuarios/connection string) solo existen en Development. Fuera de Development (aqui: Testing, igual que
/// Production en esta regla) deben responder 404 incluso a un SuperAdmin autenticado, para cada accion.
/// </summary>
[Collection("HttpIntegration.3")]
public class DiagnosticoFueraDeDevelopmentHttpTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;

    public DiagnosticoFueraDeDevelopmentHttpTests(CustomWebApplicationFactory factory) => _factory = factory;

    [Theory]
    [InlineData("/Diagnostico")]
    [InlineData("/Diagnostico/Index")]
    [InlineData("/Diagnostico/ResetPassword")]
    [InlineData("/Diagnostico/MisPermisos")]
    [InlineData("/Diagnostico/PermisosDelRol")]
    [InlineData("/Diagnostico/AuditoriaPermisos")]
    public async Task Get_SuperAdminAutenticado_Devuelve404(string ruta)
    {
        await _factory.SeedTestUserAsync();
        using var admin = _factory.CreateAuthenticatedClient();

        var respuesta = await admin.GetAsync(ruta);

        Assert.Equal(HttpStatusCode.NotFound, respuesta.StatusCode);
    }

    [Theory]
    [InlineData("/Diagnostico/ForcePasswordReset")]
    [InlineData("/Diagnostico/ResetPassword")]
    [InlineData("/Diagnostico/CrearUsuarioPrueba")]
    [InlineData("/Diagnostico/TestLogin")]
    [InlineData("/Diagnostico/RefreshPermisos")]
    public async Task Post_SuperAdminAutenticado_NoEjecutaNada_Devuelve404_o_RechazaAntiforgery(string ruta)
    {
        await _factory.SeedTestUserAsync();
        using var admin = _factory.CreateAuthenticatedClient();
        var cuerpo = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["email"] = "admin@thebury.com",
            ["newPassword"] = "ClaveNueva-12345",
            ["password"] = "x"
        });

        var respuesta = await admin.PostAsync(ruta, cuerpo);

        // Nunca 2xx/3xx: ni ejecuta la accion ni redirige a un resultado exitoso.
        Assert.True(
            respuesta.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.BadRequest,
            $"Esperaba 404/400 y fue {(int)respuesta.StatusCode}");
    }
}
