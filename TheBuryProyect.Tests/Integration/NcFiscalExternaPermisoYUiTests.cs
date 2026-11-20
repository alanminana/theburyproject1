using System.Reflection;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using TheBuryProject.Controllers;
using TheBuryProject.Data;
using TheBuryProject.Data.Seeds;
using TheBuryProject.Filters;
using TheBuryProject.Models.Constants;

namespace TheBuryProject.Tests.Integration;

/// <summary>
/// P-02 / P3: <c>notascredito.linkfiscal</c> es un permiso propio (no devoluciones.approve) con asignación inicial mínima, y la pantalla
/// de detalle de la devolución permite cargar el vínculo con la NC fiscal externa solo a quien lo tiene.
/// </summary>
public class NcFiscalExternaPermisoYUiTests : IDisposable
{
    private const string Claim = "notascredito.linkfiscal";
    private readonly SqliteConnection _connection;
    private readonly AppDbContext _context;

    public NcFiscalExternaPermisoYUiTests()
    {
        _connection = new SqliteConnection($"DataSource={Guid.NewGuid():N};Mode=Memory;Cache=Shared");
        _connection.Open();
        _context = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(_connection).Options);
        _context.Database.EnsureCreated();
    }

    public void Dispose()
    {
        _context.Dispose();
        _connection.Dispose();
    }

    [Theory]
    [InlineData(Roles.SuperAdmin, true)]
    [InlineData(Roles.Administrador, true)]
    [InlineData(Roles.Gerente, false)]
    [InlineData(Roles.Contador, false)]
    [InlineData(Roles.Tecnico, false)]
    [InlineData(Roles.Vendedor, false)]
    [InlineData(Roles.Cajero, false)]
    [InlineData(Roles.Repositor, false)]
    public async Task Seed_AsignacionInicialMinima_SoloSuperAdminYAdministrador(string rol, bool esperado)
    {
        var roleManager = new RoleManager<IdentityRole>(
            new RoleStore<IdentityRole>(_context), Array.Empty<IRoleValidator<IdentityRole>>(),
            new UpperInvariantLookupNormalizer(), new IdentityErrorDescriber(), NullLogger<RoleManager<IdentityRole>>.Instance);
        await RolesPermisosSeeder.SeedAsync(_context, roleManager);

        var accion = await _context.AccionesModulo.Include(a => a.Modulo)
            .SingleAsync(a => a.Modulo.Clave == "notascredito" && a.Clave == "linkfiscal");
        Assert.True(accion.Activa);

        var roleId = await _context.Roles.Where(r => r.Name == rol).Select(r => r.Id).SingleAsync();
        var tiene = await _context.RolPermisos.AnyAsync(rp => rp.RoleId == roleId && rp.ClaimValue == Claim && !rp.IsDeleted);
        Assert.Equal(esperado, tiene);
    }

    [Fact]
    public void Controlador_VincularNcFiscal_ExigePermisoPropioAntiforgeryYPost()
    {
        var accion = typeof(DevolucionController).GetMethod(nameof(DevolucionController.VincularNcFiscal))!;

        var permiso = accion.GetCustomAttribute<PermisoRequeridoAttribute>();
        Assert.NotNull(permiso);
        Assert.Equal(("notascredito", "linkfiscal"), (permiso!.Modulo, permiso.Accion));
        Assert.NotNull(accion.GetCustomAttribute<HttpPostAttribute>());
        Assert.NotNull(accion.GetCustomAttribute<ValidateAntiForgeryTokenAttribute>());
    }

    [Fact]
    public void Vista_OfreceElFormularioSoloConPermisoLinkfiscal_ConAntiforgeryYSinEditarElVinculo()
    {
        var vista = File.ReadAllText(Path.Combine(FindRepoRoot(), "Views", "Devolucion", "Detalles.cshtml"));

        Assert.Contains("id=\"nc-fiscal-externa\"", vista);
        Assert.Contains("User.TienePermiso(\"notascredito\", \"linkfiscal\")", vista);
        Assert.DoesNotContain("TienePermiso(\"devoluciones\", \"approve\") && ncf", vista);
        var form = vista[vista.IndexOf("asp-action=\"VincularNcFiscal\"", StringComparison.Ordinal)..];
        Assert.Contains("@Html.AntiForgeryToken()", form[..800]);
        foreach (var campo in new[] { "name=\"Tipo\"", "name=\"PuntoVenta\"", "name=\"Numero\"", "name=\"Fecha\"", "name=\"Importe\"", "name=\"Cae\"" })
            Assert.Contains(campo, form);
        // La hora de registro se muestra en hora comercial argentina (el contenedor corre en UTC: ToLocalTime() mostraría UTC).
        Assert.Contains("Reloj.UtcAComercial(ncf.FiscalExternaRegistradaEn.Value)", vista);
        Assert.DoesNotContain("FiscalExternaRegistradaEn?.ToLocalTime()", vista);
        // Una vez vinculado se muestra solo lectura: el formulario está en la rama «else» del vínculo.
        Assert.True(vista.IndexOf("data-testid=\"nc-fiscal-vinculada\"", StringComparison.Ordinal)
                    < vista.IndexOf("asp-action=\"VincularNcFiscal\"", StringComparison.Ordinal));
    }

    private static string FindRepoRoot()
    {
        var current = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (current != null)
        {
            if (File.Exists(Path.Combine(current.FullName, "TheBuryProyect.csproj")))
                return current.FullName;
            current = current.Parent;
        }
        throw new DirectoryNotFoundException("No se encontró la raíz del repositorio.");
    }
}
