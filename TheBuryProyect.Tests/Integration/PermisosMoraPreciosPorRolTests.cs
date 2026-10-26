using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TheBuryProject.Data;
using TheBuryProject.Data.Seeds;
using TheBuryProject.Models.Constants;

namespace TheBuryProject.Tests.Integration;

/// <summary>
/// Permisos por defecto de los módulos Mora y Precios: Gerente gestiona cobranza y precios (sin configurar mora ni
/// administrar listas), Cajero y Contador solo consultan mora, Vendedor no recibe ninguno de los dos.
/// </summary>
[Collection("HttpIntegration")]
public class PermisosMoraPreciosPorRolTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;

    public PermisosMoraPreciosPorRolTests(CustomWebApplicationFactory factory) => _factory = factory;

    private async Task<HashSet<string>> ClaimsDelRolAsync(string rol)
    {
        await _factory.SeedTestUserAsync();
        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await RolesPermisosSeeder.SeedAsync(context, scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>());

        var roleId = await context.Roles.Where(r => r.Name == rol).Select(r => r.Id).SingleAsync();
        return (await context.RolPermisos
                .Where(p => p.RoleId == roleId && !p.IsDeleted)
                .Select(p => p.ClaimValue)
                .ToListAsync())
            .ToHashSet();
    }

    [Fact]
    public async Task Gerente_GestionaMoraYPrecios_PeroNoConfiguraNiAdministraListas()
    {
        var claims = await ClaimsDelRolAsync(Roles.Gerente);

        Assert.Contains("mora.view", claims);
        Assert.Contains("mora.manage", claims);
        Assert.DoesNotContain("mora.config", claims);
        Assert.Contains("precios.view", claims);
        Assert.Contains("precios.simulate", claims);
        Assert.Contains("precios.apply", claims);
        Assert.Contains("precios.revert", claims);
        Assert.DoesNotContain("precios.create", claims);
        Assert.DoesNotContain("precios.update", claims);
        Assert.DoesNotContain("precios.delete", claims);
    }

    [Theory]
    [InlineData("Cajero")]
    [InlineData("Contador")]
    public async Task CajeroYContador_SoloVenMora(string rol)
    {
        var claims = await ClaimsDelRolAsync(rol);

        Assert.Contains("mora.view", claims);
        Assert.DoesNotContain("mora.manage", claims);
        Assert.DoesNotContain("mora.config", claims);
    }

    [Fact]
    public async Task Vendedor_NoRecibeMoraNiPreciosPorDefecto()
    {
        var claims = await ClaimsDelRolAsync(Roles.Vendedor);

        Assert.DoesNotContain(claims, c => c.StartsWith("mora.") || c.StartsWith("precios."));
    }
}
