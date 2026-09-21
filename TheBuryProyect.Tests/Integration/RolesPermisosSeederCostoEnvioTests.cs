using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using TheBuryProject.Data;
using TheBuryProject.Data.Seeds;
using TheBuryProject.Models.Constants;

namespace TheBuryProject.Tests.Integration;

/// <summary>
/// El costo de envío del producto es un valor fijo: el permiso productos.editshippingcost
/// existe como acción asignable en Seguridad y solo Admin/SuperAdmin lo reciben por defecto.
/// </summary>
public class RolesPermisosSeederCostoEnvioTests : IDisposable
{
    private const string ClaimCostoEnvio = "productos.editshippingcost";

    private readonly SqliteConnection _connection;
    private readonly AppDbContext _context;

    public RolesPermisosSeederCostoEnvioTests()
    {
        _connection = new SqliteConnection($"DataSource={Guid.NewGuid():N};Mode=Memory;Cache=Shared");
        _connection.Open();

        _context = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(_connection)
            .Options);
        _context.Database.EnsureCreated();
    }

    public void Dispose()
    {
        _context.Dispose();
        _connection.Dispose();
    }

    [Fact]
    public async Task Seed_CreaLaAccionEditarCostoEnvioEnProductos()
    {
        await SembrarAsync();

        var accion = await _context.AccionesModulo
            .Include(a => a.Modulo)
            .SingleAsync(a => a.Modulo.Clave == "productos" && a.Clave == "editshippingcost");

        Assert.Equal("Editar costo de envío", accion.Nombre);
        Assert.True(accion.Activa);
    }

    [Theory]
    [InlineData(Roles.SuperAdmin, true)]
    [InlineData(Roles.Administrador, true)]
    [InlineData(Roles.Gerente, false)]
    [InlineData(Roles.Vendedor, false)]
    [InlineData(Roles.Cajero, false)]
    [InlineData(Roles.Repositor, false)]
    [InlineData(Roles.Tecnico, false)]
    [InlineData(Roles.Contador, false)]
    public async Task Seed_SoloAdminYSuperAdminRecibenElPermisoPorDefecto(string rol, bool esperado)
    {
        await SembrarAsync();

        var roleId = await _context.Roles
            .Where(r => r.Name == rol)
            .Select(r => r.Id)
            .SingleAsync();

        var tiene = await _context.RolPermisos
            .AnyAsync(rp => rp.RoleId == roleId && rp.ClaimValue == ClaimCostoEnvio && !rp.IsDeleted);

        Assert.Equal(esperado, tiene);
    }

    private async Task SembrarAsync()
    {
        var roleManager = new RoleManager<IdentityRole>(
            new RoleStore<IdentityRole>(_context),
            Array.Empty<IRoleValidator<IdentityRole>>(),
            new UpperInvariantLookupNormalizer(),
            new IdentityErrorDescriber(),
            NullLogger<RoleManager<IdentityRole>>.Instance);

        await RolesPermisosSeeder.SeedAsync(_context, roleManager);
    }
}
