using System.Net;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TheBuryProject.Data;
using TheBuryProject.Data.Seeds;
using TheBuryProject.Models.Constants;

namespace TheBuryProject.Tests.Integration;

/// <summary>
/// R-03: la valoración gerencial es de solo lectura y tiene permiso propio (<c>valoracionstock.view</c>): Administrador y Contador
/// lo reciben; Gerente y el resto no. Pantalla y exportación exigen el permiso; sin él, 403/redirección y nada se entrega.
/// </summary>
[Collection("HttpIntegration.4")]
public class ValoracionStockPermisosHttpTests : IClassFixture<CustomWebApplicationFactory>, IAsyncLifetime
{
    private readonly CustomWebApplicationFactory _factory;

    public ValoracionStockPermisosHttpTests(CustomWebApplicationFactory factory) => _factory = factory;

    public async Task InitializeAsync()
    {
        await _factory.SeedTestUserAsync();
        await _factory.SeedUserWithValoracionStockLectorAsync();
        await _factory.SeedUserWithValoracionStockSinPermisoAsync();
        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await RolesPermisosSeeder.SeedAsync(context, scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>());
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private async Task<string[]> ClaimsDeAsync(string rol)
    {
        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var roleId = await context.Roles.Where(r => r.Name == rol).Select(r => r.Id).SingleAsync();
        return await context.RolPermisos.IgnoreQueryFilters()
            .Where(p => p.RoleId == roleId && !p.IsDeleted && p.ClaimValue.StartsWith("valoracionstock."))
            .Select(p => p.ClaimValue).ToArrayAsync();
    }

    [Fact]
    public async Task ElCatalogoRegistraUnaSolaAccionDeLectura()
    {
        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var acciones = await context.AccionesModulo.Include(a => a.Modulo)
            .Where(a => a.Modulo.Clave == "valoracionstock").Select(a => a.Clave).ToListAsync();
        Assert.Equal(new[] { "view" }, acciones.ToArray());
    }

    [Theory]
    [InlineData("Administrador")]
    [InlineData("Contador")]
    public async Task AdministradorYContadorRecibenElPermiso(string rol)
    {
        var nombre = rol == "Administrador" ? Roles.Administrador : Roles.Contador;
        Assert.Equal(new[] { "valoracionstock.view" }, await ClaimsDeAsync(nombre));
    }

    [Theory]
    [InlineData("Gerente")]
    [InlineData("Vendedor")]
    [InlineData("Cajero")]
    [InlineData("Repositor")]
    [InlineData("Tecnico")]
    public async Task LosDemasRolesDeSistemaNoLoRecibenImplicitamente(string rol)
    {
        var nombre = typeof(Roles).GetField(rol)!.GetRawConstantValue()!.ToString()!;
        Assert.Empty(await ClaimsDeAsync(nombre));
    }

    [Fact]
    public async Task ConPermiso_VeLaPantallaConLaLeyendaYPuedeExportar()
    {
        using var client = _factory.CreateClientWithUserId(CustomWebApplicationFactory.ValoracionStockLectorUserId);

        var index = await client.GetAsync("/ValoracionStock");
        var html = await index.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, index.StatusCode);
        Assert.Contains("Uso gerencial.", html);
        Assert.Contains("No es valuación fiscal ni contable; no modifica costos ni márgenes.", html);

        var excel = await client.GetAsync("/ValoracionStock/Exportar");
        Assert.Equal(HttpStatusCode.OK, excel.StatusCode);
        Assert.Equal("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", excel.Content.Headers.ContentType?.MediaType);
    }

    [Theory]
    [InlineData("/ValoracionStock")]
    [InlineData("/ValoracionStock/Exportar")]
    public async Task SinPermiso_NoVeNiExportaNada_AunqueTengaStockYReportes(string url)
    {
        using var client = _factory.CreateClientWithUserId(CustomWebApplicationFactory.ValoracionStockSinPermisoUserId);

        var resp = await client.GetAsync(url);

        Assert.True(resp.StatusCode == HttpStatusCode.Forbidden || resp.StatusCode == HttpStatusCode.Redirect,
            $"Se esperaba 403 o redirección a acceso denegado, llegó {(int)resp.StatusCode}.");
        Assert.NotEqual("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", resp.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public void ElControllerNoTieneAccionesQueEscriban()
    {
        var acciones = typeof(TheBuryProject.Controllers.ValoracionStockController)
            .GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.DeclaredOnly);
        Assert.NotEmpty(acciones);
        Assert.All(acciones, m =>
        {
            Assert.Empty(m.GetCustomAttributes(typeof(Microsoft.AspNetCore.Mvc.HttpPostAttribute), true));
            Assert.Empty(m.GetCustomAttributes(typeof(Microsoft.AspNetCore.Mvc.HttpPutAttribute), true));
            Assert.Empty(m.GetCustomAttributes(typeof(Microsoft.AspNetCore.Mvc.HttpDeleteAttribute), true));
        });
    }
}
