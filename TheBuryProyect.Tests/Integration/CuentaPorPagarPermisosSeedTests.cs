using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TheBuryProject.Data;
using TheBuryProject.Data.Seeds;
using TheBuryProject.Models.Constants;

namespace TheBuryProject.Tests.Integration;

/// <summary>
/// D-03 AC-03-7: el catálogo registra cuatro permisos separados de cuentas por pagar y los roles de sistema los reciben
/// según lo decidido (R-08): Administrador todos, Contador solo ver, el resto ninguno. Los permisos de otros módulos no cambian.
/// </summary>
[Collection("HttpIntegration.4")]
public class CuentaPorPagarPermisosSeedTests : IClassFixture<CustomWebApplicationFactory>, IAsyncLifetime
{
    private static readonly string[] Cuatro =
    {
        "cuentasporpagar.view", "cuentasporpagar.createinvoice", "cuentasporpagar.pay", "cuentasporpagar.cancel"
    };

    private readonly CustomWebApplicationFactory _factory;

    public CuentaPorPagarPermisosSeedTests(CustomWebApplicationFactory factory) => _factory = factory;

    public async Task InitializeAsync()
    {
        await _factory.SeedTestUserAsync();
        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await RolesPermisosSeeder.SeedAsync(context, scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>());
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private async Task<HashSet<string>> ClaimsDeAsync(string rol)
    {
        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var roleId = await context.Roles.Where(r => r.Name == rol).Select(r => r.Id).SingleAsync();
        var claims = await context.RolPermisos.IgnoreQueryFilters()
            .Where(p => p.RoleId == roleId && !p.IsDeleted && p.ClaimValue.StartsWith("cuentasporpagar."))
            .Select(p => p.ClaimValue).ToListAsync();
        return claims.ToHashSet();
    }

    [Fact]
    public async Task ElCatalogoRegistraCuatroAccionesSeparadasEnElModuloNuevo()
    {
        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var acciones = await context.AccionesModulo.Include(a => a.Modulo)
            .Where(a => a.Modulo.Clave == "cuentasporpagar").Select(a => a.Clave).ToListAsync();

        Assert.Equal(new[] { "cancel", "createinvoice", "pay", "view" }, acciones.OrderBy(a => a).ToArray());
    }

    [Fact]
    public async Task AdministradorRecibeLosCuatroPermisos()
    {
        Assert.Equal(Cuatro.OrderBy(c => c), (await ClaimsDeAsync(Roles.Administrador)).OrderBy(c => c));
    }

    [Fact]
    public async Task ContadorSoloVe()
    {
        Assert.Equal(new[] { "cuentasporpagar.view" }, (await ClaimsDeAsync(Roles.Contador)).ToArray());
    }

    [Theory]
    [InlineData("Gerente")]
    [InlineData("Vendedor")]
    [InlineData("Cajero")]
    [InlineData("Repositor")]
    [InlineData("Tecnico")]
    public async Task LosDemasRolesDeSistemaNoLosRecibenImplicitamente(string rol)
    {
        var nombre = typeof(Roles).GetField(rol)!.GetRawConstantValue()!.ToString()!;

        Assert.Empty(await ClaimsDeAsync(nombre));
    }
}
