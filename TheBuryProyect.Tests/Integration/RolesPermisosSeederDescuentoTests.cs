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
/// DESCUENTO-PERMISO-01: ventas.discount existe como acción asignable desde Seguridad y NO se concede
/// por accidente: ni el grant en bloque del módulo "ventas" para Gerente ni los permisos específicos de
/// Vendedor/Cajero/etc. lo incluyen. Administrador lo recibe por el grant "todo salvo la lista" y
/// SuperAdmin pasa por el bypass del rol.
/// </summary>
public class RolesPermisosSeederDescuentoTests : IDisposable
{
    private const string ClaimDescuento = "ventas.discount";

    private readonly SqliteConnection _connection;
    private readonly AppDbContext _context;

    public RolesPermisosSeederDescuentoTests()
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
    public async Task Seed_CreaLaAccionAplicarDescuentosEnVentas()
    {
        await SembrarAsync();

        var accion = await _context.AccionesModulo
            .Include(a => a.Modulo)
            .SingleAsync(a => a.Modulo.Clave == "ventas" && a.Clave == "discount");

        Assert.Equal("Aplicar descuentos", accion.Nombre);
        Assert.True(accion.Activa);
        Assert.Equal(VentaConstants.PermisoDescuento.Claim, $"{accion.Modulo.Clave}.{accion.Clave}");
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
    public async Task Seed_SoloSuperAdminYAdministradorRecibenElPermisoPorDefecto(string rol, bool esperado)
    {
        await SembrarAsync();

        var roleId = await _context.Roles.Where(r => r.Name == rol).Select(r => r.Id).SingleAsync();

        var tiene = await _context.RolPermisos
            .AnyAsync(rp => rp.RoleId == roleId && rp.ClaimValue == ClaimDescuento && !rp.IsDeleted);

        Assert.Equal(esperado, tiene);
    }

    [Fact]
    public async Task Seed_Gerente_SigueRecibiendoElRestoDeLosPermisosDeVentas()
    {
        await SembrarAsync();
        var gerenteId = await _context.Roles.Where(r => r.Name == Roles.Gerente).Select(r => r.Id).SingleAsync();

        var claims = await _context.RolPermisos
            .Where(rp => rp.RoleId == gerenteId && rp.ClaimValue.StartsWith("ventas.") && !rp.IsDeleted)
            .Select(rp => rp.ClaimValue)
            .ToListAsync();

        Assert.Contains("ventas.create", claims);
        Assert.Contains("ventas.update", claims);
        Assert.Contains("ventas.invoice", claims);
        Assert.Contains("ventas.authorize", claims);
        Assert.DoesNotContain(ClaimDescuento, claims);
    }

    [Fact]
    public async Task Seed_Reejecutado_RespetaElPermisoRetiradoYNoLoReasignaAGerente()
    {
        await SembrarAsync();
        var adminId = await _context.Roles.Where(r => r.Name == Roles.Administrador).Select(r => r.Id).SingleAsync();
        var permiso = await _context.RolPermisos.SingleAsync(rp => rp.RoleId == adminId && rp.ClaimValue == ClaimDescuento);
        permiso.IsDeleted = true; // destildado desde Seguridad > Permisos de Rol
        await _context.SaveChangesAsync();

        await SembrarAsync();

        _context.ChangeTracker.Clear();
        Assert.True((await _context.RolPermisos.IgnoreQueryFilters()
            .SingleAsync(rp => rp.RoleId == adminId && rp.ClaimValue == ClaimDescuento)).IsDeleted);
        var gerenteId = await _context.Roles.Where(r => r.Name == Roles.Gerente).Select(r => r.Id).SingleAsync();
        Assert.False(await _context.RolPermisos.IgnoreQueryFilters()
            .AnyAsync(rp => rp.RoleId == gerenteId && rp.ClaimValue == ClaimDescuento));
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
