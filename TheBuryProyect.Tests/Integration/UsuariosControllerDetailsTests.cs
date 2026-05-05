using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TheBuryProject.Data;
using TheBuryProject.Models.Entities;
using TheBuryProject.Tests.Infrastructure;

namespace TheBuryProject.Tests.Integration;

[Collection("HttpIntegration")]
public class UsuariosControllerDetailsTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;

    public UsuariosControllerDetailsTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Get_Details_UsuarioExistente_Devuelve200()
    {
        await _factory.SeedTestUserAsync();
        var client = _factory.CreateAuthenticatedClient();

        var response = await client.GetAsync($"/Usuarios/Details/{TestAuthHandler.UserId}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Get_Details_UsuarioInexistente_Devuelve404()
    {
        await _factory.SeedTestUserAsync();
        var client = _factory.CreateAuthenticatedClient();

        var response = await client.GetAsync("/Usuarios/Details/id-que-no-existe");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Get_Details_UsuarioExistente_RenderizaCamposNuevos()
    {
        // Verifica que los campos agregados en 3.5 se renderizan sin error.
        // El usuario sembrado no tiene Nombre/Apellido/Telefono/Sucursal,
        // por lo que la vista debe mostrar los fallbacks "No informado" / "Sin sucursal" / "Nunca".
        await _factory.SeedTestUserAsync();
        var client = _factory.CreateAuthenticatedClient();

        var response = await client.GetAsync($"/Usuarios/Details/{TestAuthHandler.UserId}");
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("testuser", html);
        Assert.Contains("No informado", html);
        Assert.Contains("Sin sucursal", html);
        Assert.Contains("Nunca", html);
    }

    [Fact]
    public async Task Get_Details_UsuarioInactivoConDatosDesactivacion_RenderizaDatos()
    {
        await _factory.SeedTestUserAsync();
        const string inactiveUserId = "inactive-details-user";
        var fechaDesactivacion = new DateTime(2026, 4, 29, 14, 30, 0, DateTimeKind.Utc);

        using (var scope = _factory.Services.CreateScope())
        {
            var contextFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>();
            await using var context = await contextFactory.CreateDbContextAsync();

            var user = await context.Users.SingleOrDefaultAsync(u => u.Id == inactiveUserId);
            if (user == null)
            {
                user = new ApplicationUser
                {
                    Id = inactiveUserId,
                    UserName = "inactiveuser",
                    NormalizedUserName = "INACTIVEUSER",
                    Email = "inactive@test.com",
                    NormalizedEmail = "INACTIVE@TEST.COM",
                    RowVersion = new byte[8]
                };
                context.Users.Add(user);
            }

            user.Activo = false;
            user.FechaDesactivacion = fechaDesactivacion;
            user.DesactivadoPor = "admin.test";
            user.MotivoDesactivacion = "Baja por prueba de integracion";
            await context.SaveChangesAsync();
        }

        var client = _factory.CreateAuthenticatedClient();

        var response = await client.GetAsync($"/Usuarios/Details/{inactiveUserId}");
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Inactivo", html);
        Assert.Contains("Fecha de desactivaci", html);
        Assert.Contains("29/04/2026 14:30", html);
        Assert.Contains("Desactivado por", html);
        Assert.Contains("admin.test", html);
        Assert.Contains("Motivo de desactivaci", html);
        Assert.Contains("Baja por prueba de integracion", html);
    }
}
