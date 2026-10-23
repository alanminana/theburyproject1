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
/// creditos.configure (PROMPT 90) es distinto de creditos.view: lo reciben quienes arman o
/// administran créditos (SuperAdmin, Administrador, Gerente y Vendedor, que configura el crédito
/// en el wizard de Venta). Cajero, Contador y los roles sin módulo de créditos quedan solo lectura.
/// </summary>
public class RolesPermisosSeederCreditosConfigureTests : IDisposable
{
    private const string Claim = "creditos.configure";

    private readonly SqliteConnection _connection;
    private readonly AppDbContext _context;

    public RolesPermisosSeederCreditosConfigureTests()
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
    public async Task Seed_CreaLaAccionConfigurarEnCreditos()
    {
        await SembrarAsync();

        var accion = await _context.AccionesModulo
            .Include(a => a.Modulo)
            .SingleAsync(a => a.Modulo.Clave == "creditos" && a.Clave == "configure");

        Assert.True(accion.Activa);
    }

    [Theory]
    [InlineData(Roles.SuperAdmin, true)]
    [InlineData(Roles.Administrador, true)]
    [InlineData(Roles.Gerente, true)]
    [InlineData(Roles.Vendedor, true)]
    [InlineData(Roles.Cajero, false)]
    [InlineData(Roles.Repositor, false)]
    [InlineData(Roles.Tecnico, false)]
    [InlineData(Roles.Contador, false)]
    public async Task Seed_AsignaConfigureSoloALosRolesQueArmanCreditos(string rol, bool esperado)
    {
        await SembrarAsync();

        var roleId = await _context.Roles.Where(r => r.Name == rol).Select(r => r.Id).SingleAsync();
        var tiene = await _context.RolPermisos
            .AnyAsync(rp => rp.RoleId == roleId && rp.ClaimValue == Claim && !rp.IsDeleted);

        Assert.Equal(esperado, tiene);
    }

    [Theory]
    [InlineData(Roles.Vendedor)]   // grant específico
    [InlineData(Roles.Gerente)]    // grant de módulo
    [InlineData(Roles.Administrador)]
    public async Task Reseed_NoRevivePermisoRevocadoDesdeSeguridad(string rol)
    {
        await SembrarAsync();
        var roleId = await _context.Roles.Where(r => r.Name == rol).Select(r => r.Id).SingleAsync();

        // Revocación como la hace RolService.RemovePermissionFromRoleAsync (soft delete).
        var permiso = await _context.RolPermisos.SingleAsync(rp => rp.RoleId == roleId && rp.ClaimValue == Claim && !rp.IsDeleted);
        permiso.IsDeleted = true;
        await _context.SaveChangesAsync();

        // "Reinicio del ERP": el seeder corre de nuevo (varias veces) sobre la misma base.
        await SembrarAsync();
        await SembrarAsync();

        _context.ChangeTracker.Clear();
        Assert.False(await _context.RolPermisos.AnyAsync(rp => rp.RoleId == roleId && rp.ClaimValue == Claim && !rp.IsDeleted),
            $"El seeder reintrodujo {Claim} revocado para {rol}.");
        Assert.Equal(1, await _context.RolPermisos.IgnoreQueryFilters().CountAsync(rp => rp.RoleId == roleId && rp.ClaimValue == Claim));
    }

    [Fact]
    public async Task Seed_RolPersonalizadoConCreditosView_NoRecibeConfigure_NiTrasReseed()
    {
        await SembrarAsync();
        var custom = new IdentityRole { Id = Guid.NewGuid().ToString(), Name = "Personalizado93", NormalizedName = "PERSONALIZADO93" };
        _context.Roles.Add(custom);
        var view = await _context.AccionesModulo.Include(a => a.Modulo).SingleAsync(a => a.Modulo.Clave == "creditos" && a.Clave == "view");
        _context.RolPermisos.Add(new TheBuryProject.Models.Entities.RolPermiso
        {
            RoleId = custom.Id, ModuloId = view.ModuloId, AccionId = view.Id, ClaimValue = "creditos.view", IsDeleted = false
        });
        await _context.SaveChangesAsync();

        await SembrarAsync();

        var claims = await _context.RolPermisos.Where(rp => rp.RoleId == custom.Id && !rp.IsDeleted).Select(rp => rp.ClaimValue).ToListAsync();
        Assert.Equal(new[] { "creditos.view" }, claims);
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
